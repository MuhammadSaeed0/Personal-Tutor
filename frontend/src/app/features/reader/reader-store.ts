import { Injectable, computed, inject, signal } from '@angular/core';
import { I18nService } from '../../core/i18n.service';
import { BookDetail, Chapter, Highlight, HighlightRect, SourceKind } from '../../core/models';
import { formatClock } from '../../core/source-times.service';

export interface TextSelection {
  text: string;
  page: number;
  rects: HighlightRect[];
  /** Viewport rectangle of the selection, for positioning the toolbar. */
  anchor: { x: number; y: number; bottom: number };
}

export interface TutorRequest {
  action: string | null;
  question: string;
  selectedText: string | null;
  page: number | null;
  nonce: number;
}

export type RightTab = 'tutor' | 'notes' | 'tools';

/** State shared by the reader's panels (provided per reader instance). */
@Injectable()
export class ReaderStore {
  private readonly i18n = inject(I18nService);
  readonly detail = signal<BookDetail | null>(null);
  readonly currentPage = signal(1);
  readonly pageCount = signal(0);
  readonly highlights = signal<Highlight[]>([]);
  readonly selection = signal<TextSelection | null>(null);
  /** Page navigation requests (nonce forces re-trigger for the same page). */
  readonly goTo = signal<{ page: number; nonce: number } | null>(null);
  readonly tutorRequest = signal<TutorRequest | null>(null);
  readonly rightTab = signal<RightTab>('tutor');
  readonly rightOpen = signal(true);

  readonly book = computed(() => this.detail()?.book ?? null);
  readonly chapters = computed(() => this.detail()?.chapters ?? []);
  readonly currentChapter = computed<Chapter | null>(() => {
    const page = this.currentPage();
    return [...this.chapters()].filter(c => c.startPage <= page && c.endPage >= page).sort((a, b) => b.level - a.level)[0] ?? null;
  });
  readonly currentPageInfo = computed(() => this.detail()?.pages.find(p => p.pageNumber === this.currentPage()) ?? null);
  readonly kind = computed<SourceKind>(() => this.book()?.kind ?? 'pdf');
  private readonly startTimes = computed(() => new Map((this.detail()?.pages ?? []).map(p => [p.pageNumber, p.startSeconds])));

  /** Video sources: start time of a transcript segment ("page"). */
  pageTime(page: number): number | null {
    return this.startTimes().get(page) ?? null;
  }

  /** "p. 12" for documents, "▶ 4:10" for videos. */
  pageLabel(page: number): string {
    const time = this.kind() === 'youTube' ? this.pageTime(page) : null;
    return time !== null ? `▶ ${formatClock(time)}` : this.i18n.t('common.pageN', { n: page });
  }

  private nonce = 0;

  navigate(page: number): void {
    const max = this.pageCount() || Number.MAX_SAFE_INTEGER;
    this.goTo.set({ page: Math.min(Math.max(1, Math.round(page)), max), nonce: ++this.nonce });
  }

  askTutor(request: Omit<TutorRequest, 'nonce'>): void {
    this.rightTab.set('tutor');
    this.rightOpen.set(true);
    this.tutorRequest.set({ ...request, nonce: ++this.nonce });
  }
}
