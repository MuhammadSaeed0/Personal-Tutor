import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import DOMPurify from 'dompurify';
import katex from 'katex';
import 'katex/contrib/mhchem';
import { Marked } from 'marked';
import { I18nService } from '../core/i18n.service';
import { SourceReference } from '../core/models';
import { SourceTimesService, formatClock } from '../core/source-times.service';

export interface CitationClick {
  citation: SourceReference | null;
  page: number;
}

const BEYOND_MARKERS = ['معلومة إضافية من خارج الكتاب', 'Beyond the textbook', 'Au-delà du manuel'];

/**
 * Renders model/student Markdown safely: math via KaTeX (with mhchem for \ce{}), citation markers
 * ([1] for tutor passages, [p:12] for page references) as buttons that open the source page,
 * and everything sanitised with DOMPurify. Text direction follows the content (dir="auto").
 */
@Component({
  selector: 'rw-markdown',
  template: `<div class="prose" dir="auto" [innerHTML]="html()" (click)="onClick($event)"></div>`,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class MarkdownComponent {
  readonly content = input<string>('');
  readonly citations = input<SourceReference[]>([]);
  /** While streaming, hide the follow-up block the model appends at the end. */
  readonly streaming = input(false);
  /** Source of [p:N] references; video sources show them as times. */
  readonly bookId = input<string | null>(null);
  readonly citationClick = output<CitationClick>();

  private readonly i18n = inject(I18nService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly sourceTimes = inject(SourceTimesService);

  constructor() {
    effect(() => {
      this.sourceTimes.ensure(this.bookId());
      for (const c of this.citations()) this.sourceTimes.ensure(c.bookId);
    });
  }

  protected readonly html = computed<SafeHtml>(() => {
    let text = this.content() ?? '';
    const cut = text.indexOf('<followups>');
    if (cut >= 0) text = text.slice(0, cut);
    else if (this.streaming()) {
      const partial = text.lastIndexOf('<');
      if (partial >= 0 && '<followups>'.startsWith(text.slice(partial))) text = text.slice(0, partial);
    }
    // renderMarkdown sanitises with DOMPurify; only locally generated KaTeX markup is added afterwards.
    this.sourceTimes.times(); // re-render when video times arrive
    const bookId = this.bookId();
    const timeOf = (book: string | null, page: number) => this.sourceTimes.time(book ?? bookId, page);
    return this.sanitizer.bypassSecurityTrustHtml(renderMarkdown(text, this.citations(), this.i18n.lang(), timeOf));
  });

  protected onClick(event: MouseEvent): void {
    const target = (event.target as HTMLElement).closest('button.cite') as HTMLButtonElement | null;
    if (!target) return;
    const marker = target.dataset['marker'];
    const page = Number(target.dataset['page']);
    const citation = marker ? this.citations().find(c => c.marker === Number(marker)) ?? null : null;
    this.citationClick.emit({ citation, page: citation?.pageStart ?? page });
  }
}

const marked = new Marked({ gfm: true, breaks: false });

export function renderMarkdown(source: string, citations: SourceReference[], lang: string,
  timeOf: (bookId: string | null, page: number) => number | null = () => null): string {
  // 1. Protect math so Markdown doesn't mangle it.
  const math: string[] = [];
  const stash = (tex: string, display: boolean) => {
    math.push(renderMath(tex, display));
    return `@@MATH${math.length - 1}@@`;
  };
  let text = source
    .replace(/\$\$([\s\S]+?)\$\$/g, (_, tex) => stash(tex, true))
    .replace(/\\\[([\s\S]+?)\\\]/g, (_, tex) => stash(tex, true))
    .replace(/(^|[^\\$])\$([^\n$]+?)\$(?!\d)/g, (_, pre, tex) => pre + stash(tex, false))
    .replace(/\\\(([\s\S]+?)\\\)/g, (_, tex) => stash(tex, false))
    .replace(/\\ce\{((?:[^{}]|\{[^{}]*\})*)\}/g, (_, ce) => stash(`\\ce{${ce}}`, false));

  // 2. Citation markers → placeholders.
  const pageWord = lang === 'ar' ? 'ص' : 'p.';
  const where = (bookId: string | null, a: number, b?: number) => {
    const time = timeOf(bookId, a);
    return time !== null ? `▶ ${formatClock(time)}` : `${pageWord} ${a}${b && b !== a ? '–' + b : ''}`;
  };
  text = text.replace(/\[p:(\d+)(?:-(\d+))?\]/g, (_, a, b) => `@@PAGE${a}${b ? '_' + b : ''}@@`);
  const known = new Set(citations.map(c => c.marker));
  text = text.replace(/\[(\d{1,2})\](?!\()/g, (m, n) => (known.has(Number(n)) ? `@@CITE${n}@@` : m));

  let html = marked.parse(text, { async: false }) as string;

  html = html.replace(/@@CITE(\d+)@@/g, (_, n) => {
    const c = citations.find(x => x.marker === Number(n));
    const title = c ? `${escapeHtml(c.bookTitle)} — ${where(c.bookId, c.pageStart, c.pageEnd)}` : '';
    return `<button type="button" class="cite" data-marker="${n}" title="${title}" aria-label="${title}">${n}</button>`;
  });
  html = html.replace(/@@PAGE(\d+)(?:_(\d+))?@@/g, (_, a, b) => {
    const label = where(null, Number(a), b ? Number(b) : undefined);
    return `<button type="button" class="cite page" data-page="${a}" aria-label="${label}">${label}</button>`;
  });

  // 3. Visually separate content the tutor marked as not from the textbook.
  for (const marker of BEYOND_MARKERS) {
    html = html.replace(new RegExp(`<p><strong>(${escapeRegex(marker)}[^<]*)</strong>`, 'g'), '<p class="beyond-textbook"><strong>$1</strong>');
  }

  const clean = DOMPurify.sanitize(html, {
    ADD_ATTR: ['data-marker', 'data-page', 'aria-label'],
    FORBID_TAGS: ['style', 'script', 'iframe', 'form', 'input', 'img'],
    FORBID_ATTR: ['style']
  }) as string;

  // KaTeX output (generated here from TeX with trust disabled) needs inline styles, so it is added after sanitising.
  return clean.replace(/@@MATH(\d+)@@/g, (_, i) => math[Number(i)] ?? '');
}

function renderMath(tex: string, display: boolean): string {
  try {
    return katex.renderToString(tex, { displayMode: display, throwOnError: false, strict: 'ignore', output: 'html', trust: false, maxExpand: 500 });
  } catch {
    return `<code>${escapeHtml(tex)}</code>`;
  }
}

function escapeHtml(s: string): string {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function escapeRegex(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
