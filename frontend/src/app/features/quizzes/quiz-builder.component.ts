import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { Attempt, Book, BookDetail, Chapter, Difficulty, QuestionType, QuizDetail } from '../../core/models';
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
  geography: ['multipleChoice', 'matching', 'trueFalse', 'shortAnswer'],
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
export class QuizBuilderComponent {
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

  protected readonly bookId = signal<string | null>(null);
  protected readonly mode = signal<'practice' | 'exam'>('practice');
  protected scope: 'book' | 'chapter' | 'pages' = 'book';
  protected chapterId: string | null = null;
  protected pageStart = 1;
  protected pageEnd = 10;
  protected difficulty: Difficulty = 'medium';
  protected count = 10;
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
      bookId: book.id, difficulty: this.difficulty, count: this.count, types: this.types(), mode: this.mode(),
      timeLimitMinutes: this.timeLimit > 0 ? this.timeLimit : null, hintsAllowed: this.hints, revealImmediately: this.reveal,
      focusWeakAreas: this.focusWeak, language: this.language || null, title: this.title.trim() || null
    };
    if (this.scope === 'chapter' && this.chapterId) body['chapterId'] = this.chapterId;
    if (this.scope === 'pages') { body['pageStart'] = this.pageStart; body['pageEnd'] = this.pageEnd; }
    if (this.mode() === 'exam' && this.useBlueprint) body['blueprint'] = Object.fromEntries(Object.entries(this.blueprint).filter(([, v]) => Number(v) > 0));

    this.generating.set(true);
    try {
      const quiz = await this.api.post<QuizDetail>('/api/quizzes', body);
      if (quiz.quiz.excludedQuestionCount > 0) this.toast.show(this.i18n.t('builder.excluded', { n: quiz.quiz.excludedQuestionCount }));
      const attempt = await this.api.post<Attempt>(`/api/quizzes/${quiz.quiz.id}/attempts`);
      void this.router.navigate(['/quiz/attempt', attempt.id]);
    } catch (e) {
      this.toast.error(e);
    } finally {
      this.generating.set(false);
    }
  }
}
