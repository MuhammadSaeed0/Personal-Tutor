import {
  ChangeDetectionStrategy, Component, ElementRef, OnDestroy, effect, inject, input, output, signal, untracked, viewChild
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import type { PDFDocumentProxy } from 'pdfjs-dist';
import { ApiService } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { Bookmark, SearchHit } from '../../core/models';
import { IconComponent } from '../../shared/icon.component';
import { ReaderStore } from './reader-store';

@Component({
  selector: 'rw-reader-contents',
  imports: [FormsModule, TranslatePipe, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="panel">
      <div class="tabs" role="tablist">
        <button role="tab" type="button" [attr.aria-selected]="tab() === 'toc'" (click)="tab.set('toc')">{{ 'reader.toc' | t }}</button>
        <button role="tab" type="button" [attr.aria-selected]="tab() === 'pages'" (click)="tab.set('pages')">{{ 'reader.pages' | t }}</button>
        <button role="tab" type="button" [attr.aria-selected]="tab() === 'bookmarks'" (click)="tab.set('bookmarks')">{{ 'reader.bookmarks' | t }}</button>
        <button role="tab" type="button" [attr.aria-selected]="tab() === 'search'" (click)="tab.set('search')" [attr.aria-label]="'common.search' | t"><rw-icon name="search" [size]="15" /></button>
      </div>

      <div class="content">
        @switch (tab()) {
          @case ('toc') {
            @if (store.chapters().length === 0) {
              <p class="muted small pad">{{ 'reader.noToc' | t }}</p>
              <p class="subtle xsmall pad">{{ 'reader.noTocHint' | t }}</p>
            }
            <ul class="toc">
              @for (c of store.chapters(); track c.id) {
                <li>
                  <button type="button" class="toc-item" [class.sub]="c.level > 1" [class.current]="store.currentChapter()?.id === c.id" (click)="go(c.startPage)">
                    <span class="status" [class]="'status ' + c.status" aria-hidden="true"></span>
                    <span class="grow" dir="auto">{{ c.title }}</span>
                    <span class="subtle xsmall num">{{ c.startPage }}</span>
                  </button>
                </li>
              }
            </ul>
          }
          @case ('pages') {
            <div class="thumbs" #thumbs>
              @for (n of pageNumbers(); track n) {
                <button type="button" class="thumb" [class.current]="store.currentPage() === n" [attr.data-page]="n" (click)="go(n)" [attr.aria-label]="'common.pageN' | t: { n }">
                  <canvas></canvas><span class="num xsmall">{{ n }}</span>
                </button>
              }
            </div>
          }
          @case ('bookmarks') {
            @for (b of bookmarks(); track b.id) {
              <div class="bm">
                <button type="button" class="grow bm-link" (click)="go(b.pageNumber)">
                  <rw-icon name="bookmark" [size]="15" />
                  <span class="grow"><span dir="auto">{{ b.label || ('common.pageN' | t: { n: b.pageNumber }) }}</span><br><span class="subtle xsmall">{{ 'common.pageN' | t: { n: b.pageNumber } }}</span></span>
                </button>
                <button class="btn ghost icon sm" type="button" (click)="removeBookmark.emit(b)" [attr.aria-label]="'common.remove' | t"><rw-icon name="x" [size]="14" /></button>
              </div>
            } @empty {
              <p class="muted small pad">{{ 'reader.noBookmarks' | t }}</p>
            }
          }
          @case ('search') {
            <form class="search" (ngSubmit)="search()">
              <input class="input" [(ngModel)]="query" name="q" [placeholder]="'reader.searchBook' | t" dir="auto" [attr.aria-label]="'reader.searchBook' | t" />
              <button class="btn icon" type="submit" [attr.aria-label]="'common.search' | t"><rw-icon name="search" [size]="16" /></button>
            </form>
            @if (searching()) { <p class="pad"><span class="spinner"></span></p> }
            @for (hit of hits(); track hit.chunkId) {
              <button type="button" class="hit" (click)="go(hit.pageStart)">
                <span class="badge brand num">{{ 'common.pageN' | t: { n: hit.pageStart } }}</span>
                <span class="small" dir="auto">{{ hit.snippet }}</span>
              </button>
            }
            @if (searched() && !searching() && hits().length === 0) { <p class="muted small pad">{{ 'reader.noResults' | t }}</p> }
          }
        }
      </div>
    </div>
  `,
  styles: [`
    :host { display: flex; flex-direction: column; min-height: 0; }
    .panel { display: flex; flex-direction: column; height: 100%; min-height: 0; }
    .tabs { padding: 0 8px; flex: none; }
    .content { flex: 1; overflow-y: auto; padding: 8px; }
    .pad { padding: 8px; margin: 0; }
    .toc { list-style: none; margin: 0; padding: 0; }
    .toc-item { width: 100%; display: flex; align-items: center; gap: 8px; padding: 8px; border: 0; background: none; border-radius: 8px; text-align: start; cursor: pointer; font-size: var(--text-sm); color: var(--ink); }
    .toc-item:hover { background: var(--surface-sunken); }
    .toc-item.sub { padding-inline-start: 26px; font-size: var(--text-xs); color: var(--ink-2); }
    .toc-item.current { background: var(--brand-soft); color: var(--brand-strong); font-weight: 600; }
    .status { width: 8px; height: 8px; border-radius: 50%; border: 1.5px solid var(--line-strong); flex: none; }
    .status.inProgress { border-color: var(--brand); background: linear-gradient(90deg, var(--brand) 50%, transparent 50%); }
    .status.completed, .status.alreadyKnown { background: var(--brand); border-color: var(--brand); }
    .thumbs { display: grid; grid-template-columns: repeat(2, 1fr); gap: 10px; }
    .thumb { border: 2px solid transparent; background: var(--surface-sunken); border-radius: 6px; padding: 4px; cursor: pointer; display: flex; flex-direction: column; align-items: center; gap: 4px; }
    .thumb canvas { width: 100%; aspect-ratio: 0.72; background: #fff; border-radius: 2px; }
    .thumb.current { border-color: var(--brand); }
    .bm { display: flex; align-items: center; gap: 4px; border-bottom: 1px solid var(--line); }
    .bm-link { display: flex; gap: 8px; align-items: flex-start; border: 0; background: none; text-align: start; padding: 8px; cursor: pointer; font-size: var(--text-sm); color: var(--ink); }
    .bm-link rw-icon { color: var(--brand); margin-top: 2px; }
    .search { display: flex; gap: 6px; margin-bottom: 8px; }
    .hit { display: flex; flex-direction: column; align-items: flex-start; gap: 4px; width: 100%; border: 0; background: none; text-align: start; padding: 10px 8px; border-bottom: 1px solid var(--line); cursor: pointer; color: var(--ink); }
    .hit:hover { background: var(--surface-2); }
  `]
})
export class ReaderContentsComponent implements OnDestroy {
  readonly bookmarks = input<Bookmark[]>([]);
  readonly doc = input<PDFDocumentProxy | null>(null);
  readonly removeBookmark = output<Bookmark>();
  readonly navigated = output<void>();

  protected readonly store = inject(ReaderStore);
  private readonly api = inject(ApiService);
  protected readonly i18n = inject(I18nService);
  private readonly thumbs = viewChild<ElementRef<HTMLElement>>('thumbs');

  protected readonly tab = signal<'toc' | 'pages' | 'bookmarks' | 'search'>('toc');
  protected readonly hits = signal<SearchHit[]>([]);
  protected readonly searching = signal(false);
  protected readonly searched = signal(false);
  protected readonly pageNumbers = signal<number[]>([]);
  protected query = '';
  private observer?: IntersectionObserver;
  private readonly drawn = new Set<number>();

  constructor() {
    effect(() => {
      const count = this.store.pageCount();
      this.pageNumbers.set(Array.from({ length: count }, (_, i) => i + 1));
    });
    effect(() => {
      const el = this.thumbs()?.nativeElement;
      const doc = this.doc();
      if (el && doc) untracked(() => setTimeout(() => this.observeThumbs(el, doc), 0));
    });
  }

  protected go(page: number): void {
    this.store.navigate(page);
    this.navigated.emit();
  }

  protected async search(): Promise<void> {
    const book = this.store.book();
    if (!book || this.query.trim().length < 2) return;
    this.searching.set(true);
    this.searched.set(true);
    try {
      const result = await this.api.post<{ hits: SearchHit[] }>('/api/search', { query: this.query.trim(), bookIds: [book.id], limit: 25 });
      this.hits.set(result.hits.sort((a, b) => a.pageStart - b.pageStart));
    } finally {
      this.searching.set(false);
    }
  }

  private observeThumbs(container: HTMLElement, doc: PDFDocumentProxy): void {
    this.observer?.disconnect();
    this.drawn.clear();
    this.observer = new IntersectionObserver(entries => {
      for (const entry of entries) {
        if (!entry.isIntersecting) continue;
        const el = entry.target as HTMLElement;
        const n = Number(el.dataset['page']);
        if (this.drawn.has(n)) continue;
        this.drawn.add(n);
        void this.drawThumb(doc, n, el.querySelector('canvas')!);
      }
    }, { root: container.parentElement, rootMargin: '300px 0px' });
    container.querySelectorAll('.thumb').forEach(el => this.observer!.observe(el));
  }

  private async drawThumb(doc: PDFDocumentProxy, n: number, canvas: HTMLCanvasElement): Promise<void> {
    try {
      const page = await doc.getPage(n);
      const viewport = page.getViewport({ scale: 0.22 });
      canvas.width = viewport.width;
      canvas.height = viewport.height;
      await page.render({ canvas, canvasContext: canvas.getContext('2d')!, viewport }).promise;
    } catch { /* thumbnail failures are cosmetic */ }
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
  }
}
