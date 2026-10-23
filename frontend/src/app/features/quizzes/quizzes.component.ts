import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { Attempt, AttemptSummary, Quiz } from '../../core/models';
import { StoreService } from '../../core/store.service';
import { ToastService } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { ConfirmService, EmptyStateComponent } from '../../shared/ui';

@Component({
  selector: 'rw-quizzes',
  imports: [RouterLink, TranslatePipe, IconComponent, EmptyStateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <header class="page-header">
        <div><div class="eyebrow">{{ 'nav.quizzes' | t }}</div><h1>{{ 'quizzes.title' | t }}</h1><p>{{ 'quizzes.subtitle' | t }}</p></div>
        <div class="row">
          <a class="btn" routerLink="/quizzes/new" [queryParams]="{ mode: 'exam' }"><rw-icon name="graduation-cap" [size]="16" /> {{ 'quizzes.newExam' | t }}</a>
          <a class="btn primary" routerLink="/quizzes/new"><rw-icon name="plus" [size]="16" /> {{ 'quizzes.newQuiz' | t }}</a>
        </div>
      </header>

      <div class="tabs" role="tablist" style="margin-bottom: 16px">
        <button role="tab" type="button" [attr.aria-selected]="tab() === 'quizzes'" (click)="tab.set('quizzes')">{{ 'quizzes.myQuizzes' | t }}</button>
        <button role="tab" type="button" [attr.aria-selected]="tab() === 'history'" (click)="tab.set('history')">{{ 'quizzes.history' | t }}</button>
      </div>

      @if (tab() === 'quizzes') {
        @if (loaded() && quizzes().length === 0) {
          <div class="card"><rw-empty icon="list-checks" [title]="'quizzes.emptyTitle' | t" [text]="'quizzes.emptyText' | t">
            <a class="btn primary" routerLink="/quizzes/new">{{ 'quizzes.newQuiz' | t }}</a></rw-empty></div>
        }
        <div class="list-grid">
          @for (q of quizzes(); track q.id) {
            <article class="card quiz">
              <div class="row between">
                <span class="badge" [class.gold]="q.mode === 'exam'" [class.brand]="q.mode === 'practice'">{{ ('quizzes.modes.' + q.mode) | t }}</span>
                <span class="subject-dot" [style.background]="store.subject(q.subjectId)?.color"></span>
              </div>
              <h3 dir="auto">{{ q.title }}</h3>
              <p class="xsmall subtle">
                {{ 'quizzes.questionsN' | t: { n: q.questionCount } }} · {{ 'difficulty.' + q.difficulty | t }}
                @if (q.timeLimitSeconds) { · {{ i18n.duration(q.timeLimitSeconds / 60) }} }
              </p>
              @if (q.bestPercent != null) {
                <div class="best"><span class="small">{{ 'quizzes.best' | t }}</span><strong class="num">{{ i18n.number(q.bestPercent) }}%</strong>
                  <span class="xsmall subtle">({{ 'quizzes.attemptsN' | t: { n: q.attemptCount } }})</span></div>
              }
              <div class="row foot">
                <button class="btn primary sm" type="button" (click)="start(q)" [disabled]="busy()">{{ (q.attemptCount > 0 ? 'quizzes.retake' : 'quizzes.start') | t }}</button>
                <button class="btn ghost icon sm" type="button" (click)="remove(q)" [attr.aria-label]="'common.delete' | t"><rw-icon name="trash" [size]="14" /></button>
              </div>
            </article>
          }
        </div>
      } @else {
        <div class="card">
          @for (a of attempts(); track a.id) {
            <a class="list-item attempt" [routerLink]="a.status === 'submitted' ? ['/quiz/result', a.id] : ['/quiz/attempt', a.id]">
              <span class="score num" [class.good]="a.percent >= 75" [class.low]="a.percent < 50 && a.status === 'submitted'">{{ a.status === 'submitted' ? i18n.number(a.percent) + '%' : ('quizzes.inProgress' | t) }}</span>
              <div class="grow"><span class="small" dir="auto">{{ a.quizTitle }}</span><br><span class="xsmall subtle">{{ store.subjectName(a.subjectId) }} · {{ i18n.date(a.submittedAt ?? a.startedAt) }}</span></div>
              <rw-icon name="chevron-right" [size]="16" />
            </a>
          } @empty { <p class="muted small">{{ 'quizzes.noHistory' | t }}</p> }
        </div>
      }
    </div>
  `,
  styles: [`
    .list-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 14px; }
    .quiz { display: flex; flex-direction: column; gap: 8px; }
    .quiz h3 { font-size: var(--text-md); }
    .quiz p { margin: 0; }
    .best { display: flex; align-items: baseline; gap: 6px; }
    .best strong { color: var(--brand); }
    .foot { margin-top: auto; justify-content: space-between; }
    .attempt { color: var(--ink); text-decoration: none !important; }
    .score { min-width: 72px; font-weight: 700; font-size: var(--text-sm); }
    .score.good { color: var(--success); } .score.low { color: var(--danger); }
  `]
})
export class QuizzesComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly store = inject(StoreService);
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  protected readonly tab = signal<'quizzes' | 'history'>('quizzes');
  protected readonly quizzes = signal<Quiz[]>([]);
  protected readonly attempts = signal<AttemptSummary[]>([]);
  protected readonly loaded = signal(false);
  protected readonly busy = signal(false);
  protected readonly hasData = computed(() => this.quizzes().length > 0);

  constructor() {
    void Promise.all([this.api.get<Quiz[]>('/api/quizzes'), this.api.get<AttemptSummary[]>('/api/attempts')]).then(([q, a]) => {
      this.quizzes.set(q);
      this.attempts.set(a);
      this.loaded.set(true);
    });
  }

  protected async start(quiz: Quiz): Promise<void> {
    this.busy.set(true);
    try {
      const attempt = await this.api.post<Attempt>(`/api/quizzes/${quiz.id}/attempts`);
      void this.router.navigate(['/quiz/attempt', attempt.id]);
    } catch (e) {
      this.toast.error(e);
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(quiz: Quiz): Promise<void> {
    if (!(await this.confirm.ask({ title: this.i18n.t('quizzes.deleteTitle'), message: this.i18n.t('quizzes.deleteMessage'), confirm: this.i18n.t('common.delete'), danger: true }))) return;
    await this.api.delete(`/api/quizzes/${quiz.id}`);
    this.quizzes.update(list => list.filter(q => q.id !== quiz.id));
  }
}
