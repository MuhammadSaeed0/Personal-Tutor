import {
  ChangeDetectionStrategy, Component, ElementRef, Injector, NgZone, OnDestroy, afterNextRender, effect, inject, input, output, signal, untracked, viewChild
} from '@angular/core';
import type { PDFDocumentProxy, PDFPageProxy, RenderTask } from 'pdfjs-dist';
import { TextLayer } from 'pdfjs-dist';
import { Highlight } from '../../core/models';
import { TextSelection } from './reader-store';

interface PageSize { width: number; height: number; }

/**
 * Continuous-scroll PDF renderer built on pdf.js. Pages render lazily when near the viewport (canvas +
 * selectable text layer) and are released when far away, so large textbooks stay responsive.
 */
@Component({
  selector: 'rw-pdf-viewer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './pdf-viewer.component.html',
  styleUrl: './pdf-viewer.component.scss'
})
export class PdfViewerComponent implements OnDestroy {
  readonly doc = input.required<PDFDocumentProxy>();
  readonly scale = input(1);
  readonly highlights = input<Highlight[]>([]);
  readonly goTo = input<{ page: number; nonce: number } | null>(null);
  readonly pageChange = output<number>();
  readonly selectionChange = output<TextSelection | null>();
  readonly highlightClick = output<Highlight>();
  readonly baseSize = output<PageSize>();

  private readonly scroller = viewChild.required<ElementRef<HTMLElement>>('scroller');
  private readonly zone = inject(NgZone);
  private readonly injector = inject(Injector);

  protected readonly sizes = signal<PageSize[]>([]);
  private readonly pages = new Map<number, PDFPageProxy>();
  private readonly rendered = new Map<number, { scale: number; task?: RenderTask; textLayer?: TextLayer }>();
  private observer?: IntersectionObserver;
  private visible = new Set<number>();
  private lastEmitted = 0;
  private scrollRaf = 0;

  constructor() {
    effect(() => {
      const doc = this.doc();
      untracked(() => void this.init(doc));
    });

    effect(() => {
      const scale = this.scale();
      untracked(() => this.rescale(scale));
    });

    effect(() => {
      const target = this.goTo();
      if (target) untracked(() => this.scrollToPage(target.page));
    });
  }

  private async init(doc: PDFDocumentProxy): Promise<void> {
    this.cleanup();
    const first = await doc.getPage(1);
    this.pages.set(1, first);
    const vp = first.getViewport({ scale: 1 });
    const base = { width: vp.width, height: vp.height };
    this.baseSize.emit(base);
    this.sizes.set(Array.from({ length: doc.numPages }, () => base));
    // Observe once Angular has rendered the page placeholders.
    afterNextRender({ read: () => this.observe() }, { injector: this.injector });
  }

  private observe(): void {
    this.observer?.disconnect();
    const root = this.scroller().nativeElement;
    this.zone.runOutsideAngular(() => {
      this.observer = new IntersectionObserver(entries => {
        for (const entry of entries) {
          const page = Number((entry.target as HTMLElement).dataset['page']);
          if (entry.isIntersecting) {
            this.visible.add(page);
            void this.renderPage(page);
          } else {
            this.visible.delete(page);
            this.release(page);
          }
        }
      }, { root, rootMargin: '900px 0px' });
      root.querySelectorAll<HTMLElement>('.page').forEach(el => this.observer!.observe(el));
    });
  }

  protected pageStyle(size: PageSize): Record<string, string> {
    const s = this.scale();
    return {
      width: `${Math.floor(size.width * s)}px`,
      height: `${Math.floor(size.height * s)}px`,
      '--scale-factor': String(s),
      '--user-unit': '1',
      '--total-scale-factor': String(s)
    };
  }

  private async getPage(n: number): Promise<PDFPageProxy> {
    let page = this.pages.get(n);
    if (!page) {
      page = await this.doc().getPage(n);
      this.pages.set(n, page);
      const vp = page.getViewport({ scale: 1 });
      const current = this.sizes()[n - 1];
      if (!current || Math.abs(current.width - vp.width) > 1 || Math.abs(current.height - vp.height) > 1) {
        this.zone.run(() => this.sizes.update(list => list.map((s, i) => (i === n - 1 ? { width: vp.width, height: vp.height } : s))));
      }
    }
    return page;
  }

  private async renderPage(n: number): Promise<void> {
    const scale = this.scale();
    const existing = this.rendered.get(n);
    if (existing && existing.scale === scale) return;
    existing?.task?.cancel();
    existing?.textLayer?.cancel();

    const el = this.pageElement(n);
    if (!el) return;
    const entry: { scale: number; task?: RenderTask; textLayer?: TextLayer } = { scale };
    this.rendered.set(n, entry);

    try {
      const page = await this.getPage(n);
      if (this.rendered.get(n) !== entry) return;
      const viewport = page.getViewport({ scale });
      const ratio = Math.min(window.devicePixelRatio || 1, 2.5);

      const canvas = document.createElement('canvas');
      canvas.width = Math.floor(viewport.width * ratio);
      canvas.height = Math.floor(viewport.height * ratio);
      canvas.style.width = `${Math.floor(viewport.width)}px`;
      canvas.style.height = `${Math.floor(viewport.height)}px`;
      canvas.className = 'page-canvas';
      canvas.setAttribute('aria-hidden', 'true');
      const context = canvas.getContext('2d', { alpha: false })!;

      entry.task = page.render({ canvas, canvasContext: context, viewport, transform: ratio !== 1 ? [ratio, 0, 0, ratio, 0, 0] : undefined });
      await entry.task.promise;
      if (this.rendered.get(n) !== entry) return;

      const textDiv = document.createElement('div');
      textDiv.className = 'textLayer';
      entry.textLayer = new TextLayer({ textContentSource: page.streamTextContent(), container: textDiv, viewport });
      await entry.textLayer.render();
      if (this.rendered.get(n) !== entry) return;

      el.querySelector('.page-canvas')?.remove();
      el.querySelector('.textLayer')?.remove();
      el.prepend(canvas);
      el.querySelector('.layers')!.prepend(textDiv);
      el.classList.add('loaded');
    } catch (error) {
      if ((error as { name?: string })?.name !== 'RenderingCancelledException') console.warn('Page render failed', n, error);
    }
  }

  private release(n: number): void {
    const entry = this.rendered.get(n);
    if (!entry) return;
    entry.task?.cancel();
    entry.textLayer?.cancel();
    this.rendered.delete(n);
    const el = this.pageElement(n);
    if (el) {
      el.querySelector('.page-canvas')?.remove();
      el.querySelector('.textLayer')?.remove();
      el.classList.remove('loaded');
    }
  }

  private rescale(scale: number): void {
    if (!this.sizes().length) return;
    const root = this.scroller().nativeElement;
    const ratio = root.scrollTop / Math.max(1, root.scrollHeight);
    requestAnimationFrame(() => {
      root.scrollTop = ratio * root.scrollHeight;
      for (const n of [...this.rendered.keys()]) if (!this.visible.has(n)) this.release(n);
      for (const n of this.visible) void this.renderPage(n);
    });
    void scale;
  }

  scrollToPage(page: number): void {
    const el = this.pageElement(page);
    if (!el) {
      setTimeout(() => this.pageElement(page)?.scrollIntoView({ block: 'start' }), 50);
      return;
    }
    this.scroller().nativeElement.scrollTop = el.offsetTop - 12;
  }

  protected onScroll(): void {
    cancelAnimationFrame(this.scrollRaf);
    this.scrollRaf = requestAnimationFrame(() => {
      const root = this.scroller().nativeElement;
      const probe = root.scrollTop + root.clientHeight * 0.35;
      let current = 1;
      for (const el of Array.from(root.querySelectorAll<HTMLElement>('.page'))) {
        if (el.offsetTop <= probe) current = Number(el.dataset['page']);
        else break;
      }
      if (current !== this.lastEmitted) {
        this.lastEmitted = current;
        this.pageChange.emit(current);
      }
    });
  }

  /** Reports the selected text, its page and rectangles relative to the page (0–1 fractions). */
  protected onPointerUp(event?: MouseEvent | TouchEvent | KeyboardEvent): void {
    const point = event && 'clientX' in event ? { x: event.clientX, y: event.clientY } : null;
    setTimeout(() => {
      const selection = window.getSelection();
      if (!selection || selection.isCollapsed || selection.rangeCount === 0) {
        this.selectionChange.emit(null);
        if (point) this.detectHighlightClick(point);
        return;
      }
      const text = selection.toString().replace(/\s+/g, ' ').trim();
      const range = selection.getRangeAt(0);
      const pageEl = (range.commonAncestorContainer instanceof Element ? range.commonAncestorContainer : range.commonAncestorContainer.parentElement)?.closest<HTMLElement>('.page');
      if (!text || !pageEl) {
        this.selectionChange.emit(null);
        return;
      }
      const box = pageEl.getBoundingClientRect();
      const rects = Array.from(range.getClientRects())
        .filter(r => r.width > 1 && r.height > 1)
        .map(r => ({
          x: clamp((r.left - box.left) / box.width), y: clamp((r.top - box.top) / box.height),
          w: clamp(r.width / box.width), h: clamp(r.height / box.height)
        }))
        .filter(r => r.w > 0 && r.h > 0)
        .slice(0, 200);
      const bounds = range.getBoundingClientRect();
      this.selectionChange.emit({
        text: text.slice(0, 6000), page: Number(pageEl.dataset['page']), rects,
        anchor: { x: bounds.left + bounds.width / 2, y: bounds.top, bottom: bounds.bottom }
      });
    }, 10);
  }

  /** Highlights sit under the text layer (so text stays selectable); clicks are matched by position. */
  private detectHighlightClick(point: { x: number; y: number }): void {
    const el = document.elementFromPoint(point.x, point.y)?.closest<HTMLElement>('.page');
    if (!el) return;
    const page = Number(el.dataset['page']);
    const box = el.getBoundingClientRect();
    const x = (point.x - box.left) / box.width;
    const y = (point.y - box.top) / box.height;
    const hit = this.highlightsFor(page).find(h => h.rects.some(r => x >= r.x && x <= r.x + r.w && y >= r.y && y <= r.y + r.h));
    if (hit) this.zone.run(() => this.highlightClick.emit(hit));
  }

  protected highlightsFor(page: number): Highlight[] {
    return this.highlights().filter(h => h.pageNumber === page);
  }

  private pageElement(n: number): HTMLElement | null {
    return this.scroller().nativeElement.querySelector<HTMLElement>(`.page[data-page="${n}"]`);
  }

  private cleanup(): void {
    this.observer?.disconnect();
    for (const n of [...this.rendered.keys()]) this.release(n);
    this.pages.clear();
    this.visible.clear();
  }

  ngOnDestroy(): void {
    this.cleanup();
  }
}

function clamp(v: number): number {
  return Math.min(1, Math.max(0, v));
}
