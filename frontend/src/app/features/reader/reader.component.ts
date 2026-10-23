import { HttpClient, HttpEventType } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, HostListener, OnDestroy, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import * as pdfjs from 'pdfjs-dist';
import type { PDFDocumentProxy } from 'pdfjs-dist';
import { ApiService } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { BookDetail, Bookmark, Highlight, PageTextDto, ReaderState } from '../../core/models';
import { StoreService } from '../../core/store.service';
import { ToastService } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { BookNotesPanelComponent } from './book-notes-panel.component';
import { PdfViewerComponent } from './pdf-viewer.component';
import { ReaderContentsComponent } from './reader-contents.component';
import { ReaderStore, RightTab, TextSelection } from './reader-store';
import { orientSelection } from './selection-text';
import { StudyToolsPanelComponent } from './study-tools-panel.component';
import { TutorPanelComponent } from '../tutor/tutor-panel.component';

pdfjs.GlobalWorkerOptions.workerSrc = '/assets/pdfjs/pdf.worker.min.mjs';

const HEARTBEAT_MS = 30_000;

@Component({
  selector: 'rw-reader',
  imports: [FormsModule, RouterLink, TranslatePipe, IconComponent, PdfViewerComponent, ReaderContentsComponent, TutorPanelComponent, BookNotesPanelComponent, StudyToolsPanelComponent],
  providers: [ReaderStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './reader.component.html',
  styleUrl: './reader.component.scss'
})
export class ReaderComponent implements OnDestroy {
  readonly bookId = input.required<string>();

  protected readonly store = inject(ReaderStore);
  protected readonly i18n = inject(I18nService);
  protected readonly app = inject(StoreService);
  private readonly api = inject(ApiService);
  private readonly http = inject(HttpClient);
  private readonly toast = inject(ToastService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly host = inject(ElementRef<HTMLElement>);

  protected readonly doc = signal<PDFDocumentProxy | null>(null);
  protected readonly loadProgress = signal(0);
  protected readonly loadError = signal<string | null>(null);
  protected readonly scale = signal(1.2);
  protected readonly fitMode = signal<'width' | 'page' | 'custom'>('width');
  protected readonly leftOpen = signal(true);
  protected readonly focusMode = signal(false);
  protected readonly night = signal(false);
  protected readonly bookmarks = signal<Bookmark[]>([]);
  protected readonly pageInput = signal('1');
  protected readonly mobileSheet = signal<'contents' | RightTab | null>(null);
  protected readonly activeHighlight = signal<Highlight | null>(null);
  protected annotationDraft = '';

  private baseSize = { width: 595, height: 842 };
  private secondsOnPage = 0;
  private pageEnteredAt = Date.now();
  private heartbeat?: ReturnType<typeof setInterval>;
  private resizeObserver?: ResizeObserver;
  private readonly viewerArea = viewChild<ElementRef<HTMLElement>>('viewerArea');

  protected readonly isBookmarked = computed(() => this.bookmarks().some(b => b.pageNumber === this.store.currentPage()));
  protected readonly subject = computed(() => this.app.subject(this.store.book()?.subjectId));
  protected readonly selectionPosition = computed(() => {
    const sel = this.store.selection();
    if (!sel) return null;
    const top = sel.anchor.y > 120 ? sel.anchor.y - 58 : sel.anchor.bottom + 10;
    return { left: Math.min(Math.max(sel.anchor.x, 180), window.innerWidth - 180), top };
  });

  constructor() {
    effect(() => {
      const id = this.bookId();
      untracked(() => void this.load(id));
    });
    effect(() => this.pageInput.set(String(this.store.currentPage())));
    effect(() => {
      // Pages stay readable on small screens: start fitted to width, panels closed.
      if (window.innerWidth < 960) untracked(() => { this.leftOpen.set(false); this.store.rightOpen.set(false); });
    });
    effect(() => {
      // Re-fit when panels open/close (the viewer width changes).
      this.leftOpen(); this.store.rightOpen(); this.focusMode();
      untracked(() => setTimeout(() => this.applyFit(), 260));
    });
  }

  private async load(id: string): Promise<void> {
    try {
      const [detail, state, highlights] = await Promise.all([
        this.api.get<BookDetail>(`/api/books/${id}`),
        this.api.get<ReaderState>(`/api/books/${id}/reader-state`),
        this.api.get<Highlight[]>(`/api/books/${id}/highlights`)
      ]);
      this.store.detail.set(detail);
      this.store.pageCount.set(detail.book.pageCount);
      this.store.highlights.set(highlights);
      this.bookmarks.set(state.bookmarks);
      if (detail.book.status !== 'ready' && detail.book.status !== 'readyWithWarnings') {
        void this.router.navigate(['/library', id]);
        return;
      }

      const requested = Number(this.route.snapshot.queryParamMap.get('page'));
      const startPage = requested > 0 ? requested : state.lastPage;
      this.store.currentPage.set(startPage);

      const data = await this.downloadPdf(id);
      const doc = await pdfjs.getDocument({
        data,
        cMapUrl: '/assets/pdfjs/cmaps/',
        cMapPacked: true,
        standardFontDataUrl: '/assets/pdfjs/standard_fonts/',
        wasmUrl: '/assets/pdfjs/wasm/'
      }).promise;
      this.doc.set(doc);
      setTimeout(() => this.store.navigate(startPage), 150);
      this.startHeartbeat();
    } catch (e) {
      this.loadError.set(this.i18n.t('reader.loadFailed'));
      console.error(e);
    }
  }

  /** Downloads the PDF through the authenticated API (the token is never placed in a URL). */
  private downloadPdf(id: string): Promise<ArrayBuffer> {
    return new Promise((resolve, reject) => {
      this.http.get(`/api/books/${id}/file`, { responseType: 'arraybuffer', reportProgress: true, observe: 'events' }).subscribe({
        next: event => {
          if (event.type === HttpEventType.DownloadProgress && event.total) this.loadProgress.set(Math.round((100 * event.loaded) / event.total));
          if (event.type === HttpEventType.Response && event.body) resolve(event.body);
        },
        error: reject
      });
    });
  }

  protected onBaseSize(size: { width: number; height: number }): void {
    this.baseSize = size;
    this.applyFit();
    this.resizeObserver?.disconnect();
    const area = this.viewerArea()?.nativeElement;
    if (area) {
      this.resizeObserver = new ResizeObserver(() => this.applyFit());
      this.resizeObserver.observe(area);
    }
  }

  private applyFit(): void {
    const area = this.viewerArea()?.nativeElement;
    if (!area || this.fitMode() === 'custom') return;
    const width = area.clientWidth - 40;
    const height = area.clientHeight - 40;
    const scale = this.fitMode() === 'width'
      ? width / this.baseSize.width
      : Math.min(width / this.baseSize.width, height / this.baseSize.height);
    this.scale.set(Math.max(0.4, Math.min(3, Math.round(scale * 100) / 100)));
  }

  protected zoom(delta: number): void {
    this.fitMode.set('custom');
    this.scale.update(s => Math.max(0.4, Math.min(3.5, Math.round((s + delta) * 10) / 10)));
  }

  protected fit(mode: 'width' | 'page'): void {
    this.fitMode.set(mode);
    this.applyFit();
  }

  protected onPageChange(page: number): void {
    if (page === this.store.currentPage()) return;
    this.flushProgress();
    this.store.currentPage.set(page);
  }

  protected goToInput(): void {
    const page = Number(this.pageInput());
    if (page >= 1 && page <= this.store.pageCount()) this.store.navigate(page);
    else this.pageInput.set(String(this.store.currentPage()));
  }

  protected step(delta: number): void {
    this.store.navigate(this.store.currentPage() + delta);
  }

  protected async onSelection(selection: TextSelection | null): Promise<void> {
    this.store.selection.set(selection);
    if (!selection) return;
    // pdf.js returns Arabic selections in visual order and presentation forms; restore logical text
    // by comparing with the page text the server extracted.
    const text = orientSelection(selection.text, await this.pageText(selection.page));
    if (this.store.selection() === selection) this.store.selection.set({ ...selection, text });
  }

  private readonly pageTexts = new Map<number, Promise<string | null>>();

  private pageText(page: number): Promise<string | null> {
    const book = this.store.book();
    if (!book) return Promise.resolve(null);
    if (!this.pageTexts.has(page)) {
      this.pageTexts.set(page, this.api.get<PageTextDto>(`/api/books/${book.id}/pages/${page}/text`).then(p => p.text).catch(() => null));
    }
    return this.pageTexts.get(page)!;
  }

  protected selectionAction(action: string): void {
    const sel = this.store.selection();
    if (!sel) return;
    if (action === 'flashcard') {
      this.store.rightTab.set('tools');
      this.store.rightOpen.set(true);
      this.mobileSheet.set(window.innerWidth < 960 ? 'tools' : null);
      return;
    }
    if (action === 'note') {
      this.store.rightTab.set('notes');
      this.store.rightOpen.set(true);
      this.mobileSheet.set(window.innerWidth < 960 ? 'notes' : null);
      return;
    }
    this.store.askTutor({ action, question: '', selectedText: sel.text, page: sel.page });
    if (window.innerWidth < 960) this.mobileSheet.set('tutor');
    window.getSelection()?.removeAllRanges();
    this.store.selection.set(null);
  }

  protected async highlight(color: string): Promise<void> {
    const sel = this.store.selection();
    const book = this.store.book();
    if (!sel || !book || sel.rects.length === 0) return;
    try {
      const created = await this.api.post<Highlight>(`/api/books/${book.id}/highlights`, { pageNumber: sel.page, text: sel.text.slice(0, 4000), color, rects: sel.rects });
      this.store.highlights.update(list => [...list, created]);
      window.getSelection()?.removeAllRanges();
      this.store.selection.set(null);
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected openHighlight(h: Highlight): void {
    this.activeHighlight.set(h);
    this.annotationDraft = h.annotation ?? '';
  }

  protected async saveAnnotation(): Promise<void> {
    const h = this.activeHighlight();
    const book = this.store.book();
    if (!h || !book) return;
    try {
      const updated = await this.api.patch<Highlight>(`/api/books/${book.id}/highlights/${h.id}`, { annotation: this.annotationDraft });
      this.store.highlights.update(list => list.map(x => (x.id === h.id ? updated : x)));
      this.activeHighlight.set(null);
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected async deleteHighlight(): Promise<void> {
    const h = this.activeHighlight();
    const book = this.store.book();
    if (!h || !book) return;
    try {
      await this.api.delete(`/api/books/${book.id}/highlights/${h.id}`);
      this.store.highlights.update(list => list.filter(x => x.id !== h.id));
      this.activeHighlight.set(null);
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected explainHighlight(): void {
    const h = this.activeHighlight();
    if (!h) return;
    this.store.askTutor({ action: 'explain', question: '', selectedText: h.text, page: h.pageNumber });
    this.activeHighlight.set(null);
  }

  protected async toggleBookmark(): Promise<void> {
    const book = this.store.book();
    if (!book) return;
    const page = this.store.currentPage();
    const existing = this.bookmarks().find(b => b.pageNumber === page);
    try {
      if (existing) {
        await this.api.delete(`/api/books/${book.id}/bookmarks/${existing.id}`);
        this.bookmarks.update(list => list.filter(b => b.id !== existing.id));
      } else {
        const created = await this.api.post<Bookmark>(`/api/books/${book.id}/bookmarks`, { pageNumber: page, label: this.store.currentChapter()?.title ?? null });
        this.bookmarks.update(list => [...list, created].sort((a, b) => a.pageNumber - b.pageNumber));
      }
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected async removeBookmark(bookmark: Bookmark): Promise<void> {
    const book = this.store.book();
    if (!book) return;
    await this.api.delete(`/api/books/${book.id}/bookmarks/${bookmark.id}`);
    this.bookmarks.update(list => list.filter(b => b.id !== bookmark.id));
  }

  protected toggleFullscreen(): void {
    if (document.fullscreenElement) void document.exitFullscreen();
    else void this.host.nativeElement.requestFullscreen?.();
  }

  protected toggleRight(tab: RightTab): void {
    if (window.innerWidth < 960) {
      this.mobileSheet.set(this.mobileSheet() === tab ? null : tab);
      this.store.rightTab.set(tab);
      return;
    }
    if (this.store.rightOpen() && this.store.rightTab() === tab) this.store.rightOpen.set(false);
    else {
      this.store.rightTab.set(tab);
      this.store.rightOpen.set(true);
    }
  }

  // ---- Reading progress (time on page) -------------------------------------------------------------

  private startHeartbeat(): void {
    this.pageEnteredAt = Date.now();
    this.heartbeat = setInterval(() => this.flushProgress(), HEARTBEAT_MS);
  }

  private flushProgress(): void {
    const book = this.store.book();
    if (!book || document.visibilityState !== 'visible') {
      this.pageEnteredAt = Date.now();
      return;
    }
    const now = Date.now();
    this.secondsOnPage = Math.min(120, Math.round((now - this.pageEnteredAt) / 1000));
    this.pageEnteredAt = now;
    if (this.secondsOnPage < 2) return;
    void this.api.put(`/api/books/${book.id}/progress`, { page: this.store.currentPage(), secondsOnPage: this.secondsOnPage }).catch(() => undefined);
  }

  @HostListener('document:visibilitychange')
  protected onVisibility(): void {
    if (document.visibilityState === 'hidden') this.flushProgress();
    else this.pageEnteredAt = Date.now();
  }

  @HostListener('document:keydown', ['$event'])
  protected onKey(event: KeyboardEvent): void {
    const target = event.target as HTMLElement;
    if (target.closest('input, textarea, select, [contenteditable="true"]')) return;
    const rtl = this.i18n.dir() === 'rtl';
    switch (event.key) {
      case 'PageDown': case 'n': this.step(1); break;
      case 'PageUp': case 'p': this.step(-1); break;
      case 'ArrowRight': if (event.altKey || !target.closest('.scroller')) { this.step(rtl ? -1 : 1); } else return; break;
      case 'ArrowLeft': if (event.altKey || !target.closest('.scroller')) { this.step(rtl ? 1 : -1); } else return; break;
      case '+': case '=': this.zoom(0.1); break;
      case '-': this.zoom(-0.1); break;
      case 'f': this.toggleFullscreen(); break;
      case 'b': void this.toggleBookmark(); break;
      case 't': this.toggleRight('tutor'); break;
      case 'Escape': this.store.selection.set(null); this.activeHighlight.set(null); this.focusMode.set(false); break;
      default: return;
    }
    event.preventDefault();
  }

  ngOnDestroy(): void {
    this.flushProgress();
    if (this.heartbeat) clearInterval(this.heartbeat);
    this.resizeObserver?.disconnect();
    void this.doc()?.loadingTask.destroy();
  }
}
