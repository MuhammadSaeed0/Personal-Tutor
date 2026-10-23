import { Dialog } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { AttemptSummary, Book, BookDetail, Chapter, FlashcardStats, SubjectStats, TopicStat } from '../../core/models';
import { StoreService } from '../../core/store.service';
import { IconComponent } from '../../shared/icon.component';
import { EmptyStateComponent, RingComponent } from '../../shared/ui';
import { UploadDialogComponent } from '../library/upload-dialog.component';

interface SubjectTool { route: string; icon: string; label: string; }

const TOOLS: Record<string, SubjectTool[]> = {
  chemistry: [{ route: 'periodic-table', icon: 'flask-conical', label: 'tools.periodicTable' }, { route: 'equation-balancer', icon: 'sigma', label: 'tools.balancer' }],
  physics: [{ route: 'units', icon: 'atom', label: 'tools.units' }],
  french: [{ route: 'practice', icon: 'languages', label: 'tools.languagePractice' }],
  english: [{ route: 'practice', icon: 'languages', label: 'tools.languagePractice' }],
  arabic: [{ route: 'practice', icon: 'scroll-text', label: 'tools.languagePractice' }]
};

@Component({
  selector: 'rw-subject',
  imports: [RouterLink, TranslatePipe, IconComponent, EmptyStateComponent, RingComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './subject.component.html',
  styleUrl: './subject.component.scss'
})
export class SubjectComponent {
  readonly code = input.required<string>();
  protected readonly i18n = inject(I18nService);
  protected readonly store = inject(StoreService);
  private readonly api = inject(ApiService);
  private readonly dialog = inject(Dialog);

  protected readonly subject = computed(() => this.store.subjects().find(s => s.code === this.code()));
  protected readonly books = signal<Book[]>([]);
  protected readonly chapters = signal<Record<string, Chapter[]>>({});
  protected readonly stats = signal<SubjectStats | null>(null);
  protected readonly topics = signal<TopicStat[]>([]);
  protected readonly cards = signal<FlashcardStats | null>(null);
  protected readonly attempts = signal<AttemptSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly yearFilter = signal<string | null>(null);

  protected readonly tools = computed(() => TOOLS[this.code()] ?? []);
  protected readonly years = computed(() => [...new Set(this.books().map(b => b.academicYear).filter((y): y is string => !!y))]);
  protected readonly visibleBooks = computed(() => this.books().filter(b => !this.yearFilter() || b.academicYear === this.yearFilter()));
  protected readonly weak = computed(() => this.topics().filter(t => !t.isChapter && t.questionAttempts + t.cardReviews >= 2 && t.estimatedMastery < 0.6).slice(0, 5));
  /** First chapter (in book order) that isn't completed or already known. */
  protected readonly nextLesson = computed(() => {
    for (const book of this.visibleBooks()) {
      const next = (this.chapters()[book.id] ?? []).find(c => c.level === 1 && (c.status === 'notStarted' || c.status === 'inProgress'));
      if (next) return { book, chapter: next };
    }
    return null;
  });
  protected readonly masteryPercent = computed(() => Math.round((this.stats()?.estimatedMastery ?? 0) * 100));

  constructor() {
    effect(() => {
      const subject = this.subject();
      if (subject) untracked(() => void this.load(subject.id));
    });
  }

  private async load(subjectId: number): Promise<void> {
    this.loading.set(true);
    try {
      const [books, allStats, topics, cards, attempts] = await Promise.all([
        this.api.get<Book[]>('/api/books', { subjectId }),
        this.api.get<SubjectStats[]>('/api/analytics/subjects'),
        this.api.get<TopicStat[]>('/api/analytics/topics', { subjectId }),
        this.api.get<FlashcardStats>('/api/flashcards/stats', { subjectId }),
        this.api.get<AttemptSummary[]>('/api/attempts', { subjectId })
      ]);
      this.books.set(books);
      this.stats.set(allStats.find(s => s.subjectId === subjectId) ?? null);
      this.topics.set(topics);
      this.cards.set(cards);
      this.attempts.set(attempts.slice(0, 6));
      const details = await Promise.all(books.filter(b => b.status === 'ready' || b.status === 'readyWithWarnings')
        .map(b => this.api.get<BookDetail>(`/api/books/${b.id}`).catch(() => null)));
      const map: Record<string, Chapter[]> = {};
      for (const d of details) if (d) map[d.book.id] = d.chapters;
      this.chapters.set(map);
    } finally {
      this.loading.set(false);
    }
  }

  protected upload(): void {
    this.dialog.open(UploadDialogComponent, { autoFocus: 'first-tabbable' }).closed.subscribe(() => {
      const s = this.subject();
      if (s) void this.load(s.id);
    });
  }

  protected chapterProgress(bookId: string): { done: number; total: number } {
    const list = (this.chapters()[bookId] ?? []).filter(c => c.level === 1);
    return { done: list.filter(c => c.status === 'completed' || c.status === 'alreadyKnown').length, total: list.length };
  }
}
