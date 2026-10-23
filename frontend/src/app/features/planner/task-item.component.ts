import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { Router } from '@angular/router';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { StudyTask } from '../../core/models';
import { StoreService } from '../../core/store.service';
import { IconComponent } from '../../shared/icon.component';

const ICONS: Record<string, string> = {
  read: 'book-open', reviewFlashcards: 'layers', practiceQuiz: 'list-checks', reviseWeakTopic: 'target', examPreparation: 'graduation-cap'
};

/** One study-plan task with start / complete / skip actions. */
@Component({
  selector: 'rw-task-item',
  imports: [TranslatePipe, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="task" [class.done]="task().status === 'completed'" [class.skipped]="task().status === 'skipped'">
      <button class="check-btn" type="button" (click)="toggle.emit(task())" [attr.aria-pressed]="task().status === 'completed'"
        [attr.aria-label]="(task().status === 'completed' ? 'plan.markPending' : 'plan.markDone') | t">
        @if (task().status === 'completed') { <rw-icon name="check" [size]="14" /> }
      </button>
      <span class="type-icon" [style.color]="color()" [style.background]="color() + '1a'"><rw-icon [name]="icon()" [size]="16" /></span>
      <div class="grow text">
        <span class="title" dir="auto">{{ title() }}</span>
        <span class="meta xsmall">
          <span class="subtle">{{ subject() }}</span>
          <span class="subtle num">· {{ i18n.duration(task().estimatedMinutes) }}</span>
          @if (task().reasonCode) { <span class="badge" [class.danger]="task().reasonCode === 'exam_soon'" [class.warning]="task().reasonCode === 'weak_topic'">{{ 'plan.reasons.' + task().reasonCode | t }}</span> }
        </span>
      </div>
      @if (task().status === 'pending') {
        <button class="btn soft sm" type="button" (click)="start()">{{ 'plan.start' | t }}</button>
        @if (showSkip()) {
          <button class="btn ghost icon sm" type="button" (click)="skip.emit(task())" [attr.aria-label]="'plan.skip' | t"><rw-icon name="x" [size]="14" /></button>
        }
      }
    </div>
  `,
  styles: [`
    .task { display: flex; align-items: center; gap: 10px; padding: 10px 0; border-bottom: 1px solid var(--line); min-width: 0; }
    :host:last-child .task { border-bottom: 0; }
    .check-btn {
      width: 22px; height: 22px; border-radius: 7px; border: 1.5px solid var(--line-strong); background: var(--surface);
      display: grid; place-items: center; cursor: pointer; flex: none; color: var(--brand-ink);
    }
    .check-btn:hover { border-color: var(--brand); }
    .done .check-btn { background: var(--brand); border-color: var(--brand); }
    .done .title, .skipped .title { text-decoration: line-through; color: var(--ink-3); }
    .type-icon { width: 32px; height: 32px; border-radius: 9px; display: grid; place-items: center; flex: none; }
    .text { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
    .title { font-size: var(--text-sm); font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .meta { display: flex; gap: 6px; align-items: center; flex-wrap: wrap; }
  `]
})
export class TaskItemComponent {
  readonly task = input.required<StudyTask>();
  readonly showSkip = input(true);
  readonly toggle = output<StudyTask>();
  readonly skip = output<StudyTask>();

  protected readonly i18n = inject(I18nService);
  private readonly store = inject(StoreService);
  private readonly router = inject(Router);

  protected readonly icon = computed(() => ICONS[this.task().type] ?? 'book-open');
  protected readonly color = computed(() => this.store.subject(this.task().subjectId)?.color ?? '#0e6b66');
  protected readonly subject = computed(() => this.store.subjectName(this.task().subjectId));
  protected readonly title = computed(() => {
    const t = this.task();
    const target = t.chapterTitle ?? t.bookTitle ?? '';
    switch (t.type) {
      case 'read':
        return t.pageStart ? this.i18n.t('plan.titles.readPages', { target, from: t.pageStart, to: t.pageEnd }) : this.i18n.t('plan.titles.read', { target });
      case 'reviewFlashcards': return this.i18n.t('plan.titles.cards');
      case 'practiceQuiz': return this.i18n.t('plan.titles.quiz', { target });
      case 'reviseWeakTopic': return this.i18n.t('plan.titles.weak', { topic: t.topic ?? target });
      default: return this.i18n.t('plan.titles.exam', { target: t.bookTitle ?? '' });
    }
  });

  protected start(): void {
    const t = this.task();
    switch (t.type) {
      case 'read':
      case 'reviseWeakTopic':
        if (t.bookId) void this.router.navigate(['/read', t.bookId], { queryParams: { page: t.pageStart ?? undefined, task: t.id } });
        break;
      case 'reviewFlashcards':
        void this.router.navigate(['/flashcards/review'], { queryParams: { subjectId: t.subjectId ?? undefined } });
        break;
      case 'practiceQuiz':
        void this.router.navigate(['/quizzes/new'], { queryParams: { bookId: t.bookId, chapterId: t.chapterId ?? undefined } });
        break;
      default:
        void this.router.navigate(['/quizzes/new'], { queryParams: { bookId: t.bookId, mode: 'exam' } });
    }
  }
}
