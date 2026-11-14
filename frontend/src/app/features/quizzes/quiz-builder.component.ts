import { ChangeDetectionStrategy, Component, OnDestroy, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiError, ApiService } from '../../core/api.service';
import { QuizGenerationService } from '../../core/quiz-generation.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { Attempt, Book, BookDetail, Chapter, Difficulty, ExamTemplate, QuestionType, QuizDetail } from '../../core/models';
import { StoreService } from '../../core/store.service';
import { ToastService } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { EmptyStateComponent } from '../../shared/ui';

const ALL_TYPES: QuestionType[] = ['multipleChoice', 'trueFalse', 'fillBlank', 'shortAnswer', 'numerical', 'matching', 'ordering', 'structured'];

/** Sensible question types per subject (the student can change them). */
const SUBJECT_TYPES: Record<string, QuestionType[]> = {
  physics: ['multipleChoice', 'numerical', 'trueFalse', 'shortAnswer'],
  chemistry: ['multipleChoice', 'numerical', 'fillBlank', 'shortAnswer'],
  biology: ['multipleChoice', 'ordering', 'matching', 'shortAnswer'],
  geology: ['multipleChoice', 'matching', 'trueFalse', 'shortAnswer'],
  arabic: ['multipleChoice', 'fillBlank', 'shortAnswer', 'structured'],
  english: ['multipleChoice', 'fillBlank', 'matching', 'shortAnswer'],
  french: ['multipleChoice', 'fillBlank', 'matching', 'shortAnswer']
};

@Component({
  selector: 'rw-quiz-builder',
  imports: [FormsModule, RouterLink, TranslatePipe, IconComponent, EmptyStateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './quiz-builder.component.html',
  styleUrl: './quiz-builder.component.scss'
})
export class QuizBuilderComponent implements OnDestroy {
  protected readonly i18n = inject(I18nService);
  protected readonly store = inject(StoreService);
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  protected readonly allTypes = ALL_TYPES;
  protected readonly books = signal<Book[]>([]);
  protected readonly chapters = signal<Chapter[]>([]);
  protected readonly loaded = signal(false);
  protected readonly generating = signal(false);
  /** Exam built from the student's Ministry template for the book's subject. */
  protected readonly ministry = signal(false);
  protected readonly templates = signal<ExamTemplate[]>([]);
  protected readonly template = computed(() => this.templates().find(t => t.subjectId === this.book()?.subjectId) ?? null);
  /** The Ministry template decides counts, types, marks and time, so those settings are hidden. */
  protected readonly ministryOn = computed(() => this.mode() === 'exam' && this.ministry() && !!this.template());
  private readonly jobs = inject(QuizGenerationService);
  private readonly pendingId = signal<string | null>(null);
  protected readonly followResult = effect(() => {
    const id = this.pendingId();
    const state = id ? this.jobs.states()[id] : undefined;
    if (id && state && state.status !== 'generating') untracked(() => void this.onFinished(id, state.status as 'ready' | 'failed', state.errorCode));
  });

  protected readonly bookId = signal<string | null>(null);
  protected readonly mode = signal<'practice' | 'exam'>('practice');
  protected scope: 'book' | 'chapter' | 'pages' = 'book';
  protected chapterId: string | null = null;
  protected pageStart = 1;
  protected pageEnd = 10;
  protected difficulty: Difficulty = 'medium';
  protected count = 10;
  /** Questions built around a drawing (counted within the total; added to an exam blueprint). */
  protected readonly diagramCount = signal(0);
  protected readonly types = signal<QuestionType[]>(['multipleChoice', 'trueFalse', 'fillBlank', 'shortAnswer']);
  protected useBlueprint = false;
  protected blueprint: Record<string, number> = { multipleChoice: 10, shortAnswer: 3, structured: 1 };
  protected timeLimit = 0;
  protected hints = true;
  protected reveal = false;
  protected focusWeak = false;
  protected language = '';
  protected title = '';

  protected readonly book = computed(() => this.books().find(b => b.id === this.bookId()) ?? null);
  protected readonly blueprintTotal = () => Object.values(this.blueprint).reduce((a, b) => a + (Number(b) || 0), 0);

  constructor() {
    this.api.get<ExamTemplate[]>('/api/exam-templates').then(t => this.templates.set(t)).catch(() => { /* the option stays hidden */ });
    void this.init();
  }

  private async init(): Promise<void> {
    const params = this.route.snapshot.queryParamMap;
    const books = (await this.api.get<Book[]>('/api/books')).filter(b => b.status === 'ready' || b.status === 'readyWithWarnings');
    this.books.set(books);
    if (params.get('mode') === 'exam') this.setMode('exam');
    const requested = params.get('bookId');
    const book = books.find(b => b.id === requested) ?? books[0];
    if (book) await this.pickBook(book.id);
    if (params.get('chapterId')) { this.scope = 'chapter'; this.chapterId = params.get('chapterId'); }
    if (params.get('pageStart')) { this.scope = 'pages'; this.pageStart = Number(params.get('pageStart')); this.pageEnd = Number(params.get('pageEnd') ?? this.pageStart + 4); }
    // Set last: plain fields above are rendered by the change detection this signal triggers.
    this.loaded.set(true);
  }

  protected async pickBook(id: string): Promise<void> {
    this.bookId.set(id);
    const book = this.books().find(b => b.id === id);
    const code = this.store.subject(book?.subjectId)?.code;
    if (code && SUBJECT_TYPES[code] && this.mode() === 'practice') this.types.set(SUBJECT_TYPES[code]);
    this.chapterId = null;
    try {
      const detail = await this.api.get<BookDetail>(`/api/books/${id}`);
      this.chapters.set(detail.chapters);
      this.pageEnd = Math.min(detail.book.pageCount, 10);
    } catch { this.chapters.set([]); }
  }

  protected setMode(mode: 'practice' | 'exam'): void {
    this.mode.set(mode);
    if (mode === 'exam') {
      this.timeLimit = this.timeLimit || 60;
      this.hints = false;
      this.reveal = false;
      this.count = Math.max(this.count, 20);
    } else {
      this.timeLimit = 0;
    }
  }

  protected toggleType(type: QuestionType): void {
    this.types.update(list => (list.includes(type) ? (list.length > 1 ? list.filter(t => t !== type) : list) : [...list, type]));
  }

  protected async generate(): Promise<void> {
    const book = this.book();
    if (!book) return;
    const body: Record<string, unknown> = {
      bookId: book.id, difficulty: this.difficulty, count: this.count, types: this.types(), mode: this.mode(), diagramCount: Math.min(this.diagramCount(), this.maxDiagrams()),
      timeLimitMinutes: this.timeLimit > 0 ? this.timeLimit : null, hintsAllowed: this.hints, revealImmediately: this.reveal,
      focusWeakAreas: this.focusWeak, language: this.language || null, title: this.title.trim() || null
    };
    if (this.scope === 'chapter' && this.chapterId) body['chapterId'] = this.chapterId;
    if (this.scope === 'pages') { body['pageStart'] = this.pageStart; body['pageEnd'] = this.pageEnd; }
    if (this.mode() === 'exam' && this.useBlueprint) body['blueprint'] = Object.fromEntries(Object.entries(this.blueprint).filter(([, v]) => Number(v) > 0));
    if (this.ministryOn()) {
      body['ministryFormat'] = true;
      delete body['blueprint'];
    }

    this.generating.set(true);
    try {
      // The server only queues the quiz; its questions are written in the background.
      const queued = await this.api.post<QuizDetail>('/api/quizzes', body);
      this.pendingId.set(queued.quiz.id);
      this.jobs.watch(queued.quiz.id);
      this.jobs.track(queued.quiz.id, queued.quiz.title);
    } catch (e) {
      this.toast.error(e);
      this.generating.set(false);
    }
  }

  /** Reacts to the quiz this screen is waiting for. */
  private async onFinished(id: string, status: 'ready' | 'failed', errorCode: string | null): Promise<void> {
    this.pendingId.set(null);
    this.jobs.unwatch(id);
    try {
      if (status === 'failed') {
        this.toast.error(new ApiError({ status: 503, code: errorCode ?? 'unknown_error' }));
        return;
      }
      const quiz = await this.api.get<QuizDetail>(`/api/quizzes/${id}`);
      if (quiz.quiz.excludedQuestionCount > 0) this.toast.show(this.i18n.t('builder.excluded', { n: quiz.quiz.excludedQuestionCount }));
      const attempt = await this.api.post<Attempt>(`/api/quizzes/${id}/attempts`);
      void this.router.navigate(['/quiz/attempt', attempt.id]);
    } catch (e) {
      this.toast.error(e);
    } finally {
      this.generating.set(false);
    }
  }

  ngOnDestroy(): void {
    // Leaving the page keeps the quiz going; a toast will say when it is ready.
    const id = this.pendingId();
    if (id) this.jobs.unwatch(id);
  }

  protected maxDiagrams(): number {
    return this.mode() === 'exam' && this.useBlueprint ? 20 : Math.min(20, Number(this.count) || 0);
  }

  /** Which subject's drawing examples to show. */
  protected diagramSubject(): string {
    const code = this.store.subject(this.books().find(b => b.id === this.bookId())?.subjectId)?.code ?? '';
    return ['physics', 'chemistry', 'biology', 'geology'].includes(code) ? code : 'other';
  }
}
