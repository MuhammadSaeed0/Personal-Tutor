import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { Attempt, StudyTask, TodayOverview } from '../../core/models';
import { StoreService } from '../../core/store.service';
import { ToastService } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { TaskItemComponent } from '../planner/task-item.component';

/**
 * "Today's study": one guided session through everything due today: flashcards, then mistakes to retest,
 * then the plan's tasks, then a short summary. Each step links to the screen that does the work.
 */
@Component({
  selector: 'rw-today',
  imports: [RouterLink, TranslatePipe, IconComponent, TaskItemComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page narrow">
      <header class="page-header">
        <div><div class="eyebrow">{{ i18n.date(todayIso, 'weekday') }}</div><h1>{{ 'today.title' | t }}</h1><p>{{ 'today.subtitle' | t }}</p></div>
        <a class="btn" routerLink="/focus"><rw-icon name="timer" [size]="16" /> {{ 'today.focus' | t }}</a>
      </header>

      @if (data(); as d) {
        <div class="progress card">
          <div class="row between small"><span>{{ 'today.progress' | t: { done: doneSteps(), total: 3 } }}</span>
            <span class="muted">{{ 'today.minutes' | t: { n: d.minutesToday } }} · {{ 'today.streak' | t: { n: d.currentStreak } }}</span></div>
          <div class="bar"><span [style.width.%]="(100 * doneSteps()) / 3"></span></div>
        </div>

        <ol class="steps">
          <li class="card step" [class.done]="d.cardsDue === 0">
            <span class="num-badge">1</span>
            <div class="grow">
              <h3><rw-icon name="layers" [size]="18" /> {{ 'today.cards' | t }}</h3>
              @if (d.cardsDue > 0) { <p class="small muted">{{ 'today.cardsDue' | t: { n: d.cardsDue } }}</p> }
              @else { <p class="small muted">{{ (d.newCards > 0 ? 'today.cardsNone' : 'today.cardsDone') | t: { n: d.newCards } }}</p> }
            </div>
            @if (d.cardsDue > 0 || d.newCards > 0) { <a class="btn" [class.primary]="d.cardsDue > 0" routerLink="/flashcards/review">{{ 'today.review' | t }}</a> }
            @else { <rw-icon name="check-circle" [size]="22" class="ok" /> }
          </li>

          <li class="card step" [class.done]="d.mistakesDue === 0">
            <span class="num-badge">2</span>
            <div class="grow">
              <h3><rw-icon name="rotate-ccw" [size]="18" /> {{ 'today.mistakes' | t }}</h3>
              @if (d.mistakesDue === 0) { <p class="small muted">{{ 'today.mistakesDone' | t }}</p> }
              @else {
                <div class="row wrap subjects">
                  @for (s of d.mistakesBySubject; track s.subjectId) {
                    <button class="btn sm primary" type="button" (click)="retest(s.subjectId)" [disabled]="busy()">
                      <span class="subject-dot" [style.background]="store.subject(s.subjectId)?.color"></span>
                      {{ store.subjectName(s.subjectId) }} ({{ i18n.number(s.count) }})</button>
                  }
                </div>
              }
            </div>
            @if (d.mistakesDue === 0) { <rw-icon name="check-circle" [size]="22" class="ok" /> }
            @else { <a class="btn ghost sm" routerLink="/mistakes">{{ 'today.allMistakes' | t }}</a> }
          </li>

          <li class="card step tasks" [class.done]="planDone()">
            <span class="num-badge">3</span>
            <div class="grow">
              <h3><rw-icon name="calendar" [size]="18" /> {{ 'today.plan' | t }}</h3>
              @if (d.plan && d.plan.tasks.length > 0) {
                @for (task of d.plan.tasks; track task.id) {
                  <rw-task-item [task]="task" (toggle)="toggleTask($event)" (skip)="updateTask($event, 'skipped')" />
                }
              } @else {
                <p class="small muted">{{ 'today.noPlan' | t }} <a routerLink="/plan">{{ 'nav.plan' | t }}</a></p>
              }
            </div>
          </li>
        </ol>

        @if (doneSteps() === 3) {
          <div class="card summary" role="status">
            <rw-icon name="sparkles" [size]="22" />
            <div><h3>{{ 'today.allDone' | t }}</h3><p class="small muted">{{ 'today.allDoneText' | t: { n: d.minutesToday } }}</p></div>
          </div>
        }
      } @else {
        <div class="card"><span class="spinner"></span></div>
      }
    </div>
  `,
  styles: [`
    .progress { display: flex; flex-direction: column; gap: 8px; margin-bottom: 16px; }
    .steps { list-style: none; padding: 0; margin: 0; display: flex; flex-direction: column; gap: 12px; }
    .step { display: flex; gap: 14px; align-items: flex-start; }
    .step h3 { display: flex; align-items: center; gap: 8px; margin: 0 0 4px; font-size: var(--text-md); }
    .step p { margin: 0; }
    .step.done { opacity: .75; }
    .num-badge { flex: none; width: 28px; height: 28px; border-radius: 50%; display: grid; place-items: center; background: var(--brand-soft); color: var(--brand); font-weight: 700; }
    .step.done .num-badge { background: var(--brand); color: var(--surface); }
    .subjects { gap: 8px; margin-top: 6px; }
    .ok { color: var(--brand); }
    .summary { display: flex; gap: 12px; align-items: center; margin-top: 16px; color: var(--brand); }
    .summary h3 { margin: 0; color: var(--ink); }
    @media (max-width: 560px) { .step { flex-wrap: wrap; } }
  `]
})
export class TodayComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  protected readonly i18n = inject(I18nService);
  protected readonly store = inject(StoreService);
  protected readonly todayIso = new Date().toISOString();

  protected readonly data = signal<TodayOverview | null>(null);
  protected readonly busy = signal(false);

  protected readonly planDone = computed(() => {
    const tasks = this.data()?.plan?.tasks ?? [];
    return tasks.every(t => t.status !== 'pending');
  });
  protected readonly doneSteps = computed(() => {
    const d = this.data();
    if (!d) return 0;
    return (d.cardsDue === 0 ? 1 : 0) + (d.mistakesDue === 0 ? 1 : 0) + (this.planDone() ? 1 : 0);
  });

  constructor() { void this.load(); }

  private async load(): Promise<void> {
    try {
      this.data.set(await this.api.get<TodayOverview>('/api/today'));
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected async retest(subjectId: number): Promise<void> {
    this.busy.set(true);
    try {
      const quiz = await this.api.post<{ quizId: string }>('/api/mistakes/retest', { subjectId });
      const attempt = await this.api.post<Attempt>(`/api/quizzes/${quiz.quizId}/attempts`);
      void this.router.navigate(['/quiz/attempt', attempt.id]);
    } catch (e) {
      this.toast.error(e);
    } finally {
      this.busy.set(false);
    }
  }

  protected toggleTask(task: StudyTask): Promise<void> {
    return this.updateTask(task, task.status === 'completed' ? 'pending' : 'completed');
  }

  protected async updateTask(task: StudyTask, status: StudyTask['status']): Promise<void> {
    try {
      const updated = await this.api.patch<StudyTask>(`/api/plan/tasks/${task.id}`, { status });
      this.data.update(d => d && d.plan ? { ...d, plan: { ...d.plan, tasks: d.plan.tasks.map(t => (t.id === task.id ? updated : t)) } } : d);
    } catch (e) {
      this.toast.error(e);
    }
  }
}
