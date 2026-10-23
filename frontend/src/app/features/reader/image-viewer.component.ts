import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, OnDestroy, afterNextRender, computed, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { TranslatePipe } from '../../core/i18n.service';
import { PageTextDto } from '../../core/models';
import { MarkdownComponent } from '../../shared/markdown.component';
import { TextSelection } from './reader-store';

/**
 * Reader for photo sources: one photo per page, loaded through the authenticated API as you scroll, with the
 * text the AI read from each photo available underneath (selectable, so the selection tools work here too).
 */
@Component({
  selector: 'rw-image-viewer',
  imports: [TranslatePipe, MarkdownComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="scroller" #scroller>
      @for (n of pages(); track n) {
        <figure class="photo" [attr.data-page]="n" [style.width.%]="zoom() * 100">
          <div class="frame" [style.aspect-ratio]="ratios()[n] ?? 0.72">
            @if (urls()[n]; as url) {
              <img [src]="url" [alt]="'common.pageN' | t: { n }" (load)="onImageLoad(n, $event)" />
            } @else {
              <span class="spinner"></span>
            }
          </div>
          <figcaption>
            <span class="subtle xsmall num">{{ 'common.pageN' | t: { n } }}</span>
            <button class="btn ghost sm" type="button" (click)="toggleText(n)" [attr.aria-expanded]="texts()[n] !== undefined">
              {{ (texts()[n] !== undefined ? 'reader.hideText' : 'reader.showText') | t }}
            </button>
          </figcaption>
          @if (texts()[n] !== undefined) {
            <div class="page-text" (mouseup)="onMouseUp(n)" (touchend)="onMouseUp(n)">
              @if (texts()[n]) { <rw-markdown [content]="texts()[n]!" /> } @else { <p class="muted small">{{ 'reader.noPageText' | t }}</p> }
            </div>
          }
        </figure>
      }
    </div>
  `,
  styles: [`
    :host { display: block; height: 100%; }
    .scroller { height: 100%; overflow: auto; padding: 20px; display: flex; flex-direction: column; align-items: center; gap: 22px; }
    .photo { margin: 0; max-width: 100%; display: flex; flex-direction: column; gap: 8px; }
    .frame { width: 100%; background: var(--surface); border-radius: 6px; box-shadow: var(--shadow-2); display: grid; place-items: center; overflow: hidden; }
    .frame img { width: 100%; height: 100%; object-fit: contain; display: block; }
    figcaption { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
    .page-text { background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius); padding: 14px 16px; user-select: text; }
  `]
})
export class ImageViewerComponent implements OnDestroy {
  readonly bookId = input.required<string>();
  readonly pageCount = input.required<number>();
  /** Width relative to the available space (1 = fit width). */
  readonly zoom = input(1);
  readonly goTo = input<{ page: number; nonce: number } | null>(null);
  readonly pageChange = output<number>();
  readonly selectionChange = output<TextSelection | null>();

  private readonly http = inject(HttpClient);
  private readonly api = inject(ApiService);
  private readonly scroller = viewChild.required<ElementRef<HTMLElement>>('scroller');

  protected readonly pages = computed(() => Array.from({ length: this.pageCount() }, (_, i) => i + 1));
  protected readonly urls = signal<Record<number, string>>({});
  protected readonly ratios = signal<Record<number, number>>({});
  protected readonly texts = signal<Record<number, string | null>>({});

  private readonly requested = new Set<number>();
  private loadObserver?: IntersectionObserver;
  private visibleObserver?: IntersectionObserver;
  private readonly visibility = new Map<number, number>();
  private current = 0;

  constructor() {
    afterNextRender(() => this.observe());
    effect(() => {
      const target = this.goTo();
      if (!target) return;
      untracked(() => this.scroller().nativeElement.querySelector(`[data-page="${target.page}"]`)?.scrollIntoView({ block: 'start' }));
    });
  }

  private observe(): void {
    const root = this.scroller().nativeElement;
    this.loadObserver = new IntersectionObserver(entries => {
      for (const e of entries) if (e.isIntersecting) void this.load(Number((e.target as HTMLElement).dataset['page']));
    }, { root, rootMargin: '1200px 0px' });
    this.visibleObserver = new IntersectionObserver(entries => {
      for (const e of entries) this.visibility.set(Number((e.target as HTMLElement).dataset['page']), e.intersectionRatio);
      const best = [...this.visibility.entries()].sort((a, b) => b[1] - a[1])[0];
      if (best && best[1] > 0 && best[0] !== this.current) {
        this.current = best[0];
        this.pageChange.emit(best[0]);
      }
    }, { root, threshold: [0, 0.25, 0.5, 0.75, 1] });
    root.querySelectorAll<HTMLElement>('.photo').forEach(el => { this.loadObserver!.observe(el); this.visibleObserver!.observe(el); });
  }

  private async load(page: number): Promise<void> {
    if (this.requested.has(page)) return;
    this.requested.add(page);
    try {
      const blob = await firstValueFrom(this.http.get(`/api/books/${this.bookId()}/images/${page}`, { responseType: 'blob' }));
      this.urls.update(u => ({ ...u, [page]: URL.createObjectURL(blob) }));
    } catch {
      this.requested.delete(page);
    }
  }

  protected onImageLoad(page: number, event: Event): void {
    const img = event.target as HTMLImageElement;
    if (img.naturalWidth && img.naturalHeight) this.ratios.update(r => ({ ...r, [page]: img.naturalWidth / img.naturalHeight }));
  }

  protected async toggleText(page: number): Promise<void> {
    if (this.texts()[page] !== undefined) {
      this.texts.update(t => { const copy = { ...t }; delete copy[page]; return copy; });
      return;
    }
    try {
      const dto = await this.api.get<PageTextDto>(`/api/books/${this.bookId()}/pages/${page}/text`);
      this.texts.update(t => ({ ...t, [page]: dto.text || null }));
    } catch {
      this.texts.update(t => ({ ...t, [page]: null }));
    }
  }

  protected onMouseUp(page: number): void {
    setTimeout(() => {
      const selection = window.getSelection();
      const text = selection?.toString().trim() ?? '';
      if (!selection || !text || selection.rangeCount === 0) { this.selectionChange.emit(null); return; }
      const rect = selection.getRangeAt(0).getBoundingClientRect();
      this.selectionChange.emit({ text, page, rects: [], anchor: { x: rect.left + rect.width / 2, y: rect.top, bottom: rect.bottom } });
    });
  }

  ngOnDestroy(): void {
    this.loadObserver?.disconnect();
    this.visibleObserver?.disconnect();
    for (const url of Object.values(this.urls())) URL.revokeObjectURL(url);
  }
}
