import { ChangeDetectionStrategy, Component, ElementRef, HostListener, OnDestroy, computed, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { ApiService } from '../../core/api.service';
import { TranslatePipe } from '../../core/i18n.service';
import { Book, Chapter, PageTextDto } from '../../core/models';
import { formatClock } from '../../core/source-times.service';
import { TextSelection } from './reader-store';

const PLAYER_ORIGIN = 'https://www.youtube-nocookie.com';

/**
 * Reader for YouTube sources: the embedded player next to the transcript. The current segment follows playback,
 * clicking a time seeks the video, and transcript text can be selected for the tutor tools. The player is driven
 * through YouTube's postMessage protocol, so no YouTube script runs in the application's own page.
 */
@Component({
  selector: 'rw-video-viewer',
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="video-layout">
      <div class="player">
        @if (embedUrl(); as url) {
          <iframe #frame [src]="url" [title]="book().title" (load)="onFrameLoad()"
            allow="autoplay; encrypted-media; picture-in-picture; fullscreen" allowfullscreen referrerpolicy="strict-origin-when-cross-origin"></iframe>
        }
      </div>
      <div class="meta">
        <span class="small muted" dir="auto">{{ book().author }}</span>
        <label class="follow small"><input type="checkbox" [checked]="follow()" (change)="follow.set(!follow())" /> {{ 'reader.followVideo' | t }}</label>
      </div>
      <div class="transcript" #list (mouseup)="onMouseUp()" (wheel)="pauseFollow()" (touchmove)="pauseFollow()">
        @if (loading()) { <p class="pad"><span class="spinner"></span></p> }
        @for (seg of segments(); track seg.pageNumber) {
          @if (topicAt(seg.pageNumber); as topic) { <h3 class="topic" dir="auto">{{ topic.title }}</h3> }
          <div class="seg" [class.current]="seg.pageNumber === currentPage()" [attr.data-page]="seg.pageNumber">
            <button class="time num" type="button" (click)="seekToPage(seg.pageNumber)" [attr.aria-label]="('reader.playFrom' | t) + ' ' + clock(seg.startSeconds ?? 0)">{{ clock(seg.startSeconds ?? 0) }}</button>
            <p dir="auto">{{ seg.text }}</p>
          </div>
        }
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; height: 100%; }
    .video-layout { height: 100%; display: flex; flex-direction: column; padding: 16px 20px 0; gap: 10px; max-width: 980px; margin: 0 auto; }
    .player { flex: none; width: 100%; aspect-ratio: 16 / 9; max-height: 52vh; background: #000; border-radius: var(--radius-lg); overflow: hidden; box-shadow: var(--shadow-2); }
    .player iframe { width: 100%; height: 100%; border: 0; display: block; }
    .meta { display: flex; justify-content: space-between; align-items: center; gap: 8px; }
    .follow { display: inline-flex; align-items: center; gap: 6px; color: var(--ink-2); cursor: pointer; }
    .transcript { flex: 1; overflow: auto; padding-bottom: 24px; user-select: text; }
    .topic { font-size: var(--text-base); margin: 18px 0 6px; color: var(--brand-strong); }
    .seg { display: flex; gap: 12px; align-items: flex-start; padding: 8px 10px; border-radius: var(--radius); }
    .seg.current { background: var(--brand-soft); }
    .seg p { margin: 0; line-height: 1.9; color: var(--ink); }
    .time { flex: none; border: 0; background: var(--surface-sunken); color: var(--brand-strong); border-radius: 6px; padding: 2px 8px; font-size: var(--text-xs); cursor: pointer; direction: ltr; margin-top: 4px; }
    .time:hover { background: var(--brand); color: var(--brand-ink); }
    .pad { padding: 16px; }
  `]
})
export class VideoViewerComponent implements OnDestroy {
  readonly book = input.required<Book>();
  readonly chapters = input<Chapter[]>([]);
  readonly goTo = input<{ page: number; nonce: number } | null>(null);
  readonly pageChange = output<number>();
  readonly selectionChange = output<TextSelection | null>();

  private readonly api = inject(ApiService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly frame = viewChild<ElementRef<HTMLIFrameElement>>('frame');
  private readonly list = viewChild.required<ElementRef<HTMLElement>>('list');

  protected readonly segments = signal<PageTextDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly follow = signal(true);
  private readonly time = signal(0);
  private connected = false;
  private listenTimer?: ReturnType<typeof setInterval>;
  private followPausedUntil = 0;
  private lastEmitted = 0;

  protected readonly embedUrl = computed<SafeResourceUrl | null>(() => {
    const id = this.book().externalId;
    if (!id || !/^[A-Za-z0-9_-]{11}$/.test(id)) return null;
    const origin = encodeURIComponent(window.location.origin);
    return this.sanitizer.bypassSecurityTrustResourceUrl(`${PLAYER_ORIGIN}/embed/${id}?enablejsapi=1&rel=0&playsinline=1&origin=${origin}`);
  });

  protected readonly currentPage = computed(() => {
    const t = this.time();
    let page = this.segments()[0]?.pageNumber ?? 1;
    for (const s of this.segments()) {
      if ((s.startSeconds ?? 0) <= t + 0.5) page = s.pageNumber;
      else break;
    }
    return page;
  });

  private readonly topics = computed(() => new Map(this.chapters().filter(c => c.level === 1).map(c => [c.startPage, c])));

  constructor() {
    effect(() => {
      const id = this.book().id;
      untracked(() => void this.loadTranscript(id));
    });
    effect(() => {
      const page = this.currentPage();
      if (!this.connected || page === this.lastEmitted) return;
      untracked(() => {
        this.lastEmitted = page;
        this.pageChange.emit(page);
        if (this.follow() && Date.now() > this.followPausedUntil) this.scrollTo(page, 'nearest');
      });
    });
    effect(() => {
      const target = this.goTo();
      if (!target) return;
      untracked(() => {
        // Opening the reader on a page only scrolls; an explicit jump (citation, contents) also moves the video.
        if (target.nonce > 1 || this.connected) this.seekToPage(target.page);
        else this.scrollTo(target.page, 'start');
      });
    });
  }

  private async loadTranscript(bookId: string): Promise<void> {
    this.loading.set(true);
    try {
      this.segments.set(await this.api.get<PageTextDto[]>(`/api/books/${bookId}/pages/text`));
    } finally {
      this.loading.set(false);
    }
  }

  protected topicAt(page: number): Chapter | undefined {
    return this.topics().get(page);
  }

  protected clock(seconds: number): string {
    return formatClock(seconds);
  }

  protected onFrameLoad(): void {
    // The player starts reporting its state once it receives "listening"; retry until it answers.
    this.connected = false;
    clearInterval(this.listenTimer);
    let tries = 0;
    const listen = () => {
      if (this.connected || ++tries > 20) { clearInterval(this.listenTimer); return; }
      this.post({ event: 'listening', id: 'riwaq', channel: 'widget' });
    };
    listen();
    this.listenTimer = setInterval(listen, 500);
  }

  @HostListener('window:message', ['$event'])
  protected onMessage(event: MessageEvent): void {
    if (event.origin !== PLAYER_ORIGIN || event.source !== this.frame()?.nativeElement.contentWindow) return;
    let data: { event?: string; info?: { currentTime?: number } } | null = null;
    try { data = typeof event.data === 'string' ? JSON.parse(event.data) : event.data; } catch { return; }
    if (!data) return;
    this.connected = true;
    const current = data.info?.currentTime;
    if (typeof current === 'number' && Number.isFinite(current)) this.time.set(current);
  }

  protected seekToPage(page: number): void {
    const seg = this.segments().find(s => s.pageNumber === page);
    if (!seg) return;
    const seconds = seg.startSeconds ?? 0;
    this.post({ event: 'command', func: 'seekTo', args: [seconds, true] });
    this.post({ event: 'command', func: 'playVideo', args: [] });
    this.time.set(seconds);
    this.scrollTo(page, 'start');
  }

  protected pauseFollow(): void {
    this.followPausedUntil = Date.now() + 6000;
  }

  private scrollTo(page: number, block: ScrollLogicalPosition): void {
    this.list().nativeElement.querySelector(`[data-page="${page}"]`)?.scrollIntoView({ block, behavior: 'smooth' });
  }

  private post(message: object): void {
    this.frame()?.nativeElement.contentWindow?.postMessage(JSON.stringify(message), PLAYER_ORIGIN);
  }

  protected onMouseUp(): void {
    setTimeout(() => {
      const selection = window.getSelection();
      const text = selection?.toString().trim() ?? '';
      if (!selection || !text || selection.rangeCount === 0) { this.selectionChange.emit(null); return; }
      const range = selection.getRangeAt(0);
      const seg = (range.startContainer.parentElement ?? null)?.closest<HTMLElement>('.seg');
      const page = Number(seg?.dataset['page'] ?? this.currentPage());
      const rect = range.getBoundingClientRect();
      this.selectionChange.emit({ text, page, rects: [], anchor: { x: rect.left + rect.width / 2, y: rect.top, bottom: rect.bottom } });
    });
  }

  ngOnDestroy(): void {
    clearInterval(this.listenTimer);
  }
}
