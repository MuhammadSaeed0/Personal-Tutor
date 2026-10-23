import { ChangeDetectionStrategy, Component, OnDestroy, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { Attempt, AttemptResult, Question, QuizResponse, Reveal } from '../../core/models';
import { ToastService } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { MarkdownComponent } from '../../shared/markdown.component';
import { ConfirmService, clock } from '../../shared/ui';

@Component({
  selector: 'rw-attempt',
  imports: [FormsModule, RouterLink, TranslatePipe, IconComponent, MarkdownComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './attempt.component.html',
  styleUrl: './attempt.component.scss'
})
export class AttemptComponent implements OnDestroy {
  readonly attemptId = input.required<string>();
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  protected readonly attempt = signal<Attempt | null>(null);
  protected readonly index = signal(0);
  protected readonly answers = signal<Record<string, unknown>>({});
  protected readonly marked = signal<Record<string, boolean>>({});
  protected readonly hintsShown = signal<Record<string, boolean>>({});
  protected readonly reveals = signal<Record<string, Reveal>>({});
  protected readonly saving = signal(false);
  protected readonly submitting = signal(false);
  protected readonly now = signal(Date.now());
  protected readonly sheetOpen = signal(false);

  private timer?: ReturnType<typeof setInterval>;
  private saveTimers = new Map<string, ReturnType<typeof setTimeout>>();

  protected readonly questions = computed(() => this.attempt()?.quiz.questions ?? []);
  protected readonly quiz = computed(() => this.attempt()?.quiz.quiz ?? null);
  protected readonly current = computed<Question | null>(() => this.questions()[this.index()] ?? null);
  protected readonly answeredCount = computed(() => this.questions().filter(q => this.isAnswered(q.id)).length);
  protected readonly remaining = computed(() => {
    const deadline = this.attempt()?.deadline;
    return deadline ? Math.max(0, (new Date(deadline).getTime() - this.now()) / 1000) : null;
  });
  protected readonly elapsed = computed(() => {
    const start = this.attempt()?.startedAt;
    return start ? (this.now() - new Date(start).getTime()) / 1000 : 0;
  });
  protected readonly clockText = computed(() => clock(this.remaining() ?? this.elapsed()));

  constructor() {
    effect(() => {
      const id = this.attemptId();
      untracked(() => void this.load(id));
    });
    effect(() => {
      // Time is up: submit automatically (the server also enforces the deadline).
      if (this.remaining() === 0 && !this.submitting()) untracked(() => void this.submit(true));
    });
  }

  private async load(id: string): Promise<void> {
    try {
      const attempt = await this.api.get<Attempt>(`/api/attempts/${id}`);
      if (attempt.status === 'submitted') {
        void this.router.navigate(['/quiz/result', id], { replaceUrl: true });
        return;
      }
      const answers: Record<string, unknown> = {};
      const marked: Record<string, boolean> = {};
      for (const r of attempt.responses) {
        if (r.answer !== null) answers[r.questionId] = r.answer;
        marked[r.questionId] = r.markedForReview;
      }
      this.answers.set(answers);
      this.marked.set(marked);
      this.reveals.set(Object.fromEntries(attempt.revealed.map(r => [r.questionId, r])));
      this.attempt.set(attempt);
      this.timer = setInterval(() => this.now.set(Date.now()), 1000);
    } catch (e) {
      this.toast.error(e);
      void this.router.navigateByUrl('/quizzes');
    }
  }

  protected isAnswered(id: string): boolean {
    const a = this.answers()[id];
    return a !== undefined && a !== null && a !== '' && !(Array.isArray(a) && a.some(x => x === null || x === undefined));
  }

  protected setAnswer(q: Question, value: unknown, immediate = false): void {
    if (this.reveals()[q.id]) return;
    this.answers.update(map => ({ ...map, [q.id]: value }));
    this.scheduleSave(q, immediate ? 0 : 700);
  }

  protected matchingAnswer(q: Question): (number | null)[] {
    const current = this.answers()[q.id] as (number | null)[] | undefined;
    return current ?? (q.options.left ?? []).map(() => null);
  }

  protected setMatch(q: Question, leftIndex: number, rightIndex: string): void {
    const next = [...this.matchingAnswer(q)];
    next[leftIndex] = rightIndex === '' ? null : Number(rightIndex);
    this.setAnswer(q, next);
  }

  protected orderingAnswer(q: Question): number[] {
    return (this.answers()[q.id] as number[] | undefined) ?? (q.options.items ?? []).map((_, i) => i);
  }

  protected move(q: Question, position: number, delta: number): void {
    const order = [...this.orderingAnswer(q)];
    const target = position + delta;
    if (target < 0 || target >= order.length) return;
    [order[position], order[target]] = [order[target], order[position]];
    this.setAnswer(q, order);
  }

  protected toggleMark(q: Question): void {
    this.marked.update(m => ({ ...m, [q.id]: !m[q.id] }));
    this.scheduleSave(q, 0);
  }

  protected showHint(q: Question): void {
    this.hintsShown.update(m => ({ ...m, [q.id]: true }));
    this.scheduleSave(q, 0);
  }

  private scheduleSave(q: Question, delay: number): void {
    clearTimeout(this.saveTimers.get(q.id));
    this.saveTimers.set(q.id, setTimeout(() => void this.save(q), delay));
  }

  /**
   * Saves one response. In reveal-immediately practice mode the backend grades (and locks) any answer it
   * receives, so autosave only stores flags there; the answer itself is sent by "check" or on submit.
   */
  private async save(q: Question, commit = false): Promise<Reveal | null> {
    const attempt = this.attempt();
    if (!attempt) return null;
    this.saving.set(true);
    try {
      const result = await this.api.put<{ response: QuizResponse; reveal: Reveal | null }>(`/api/attempts/${attempt.id}/responses/${q.id}`, {
        answer: this.quiz()?.revealImmediately && !commit ? null : this.answers()[q.id] ?? null,
        markedForReview: !!this.marked()[q.id], hintUsed: !!this.hintsShown()[q.id]
      });
      return result.reveal;
    } catch (e) {
      this.toast.error(e);
      return null;
    } finally {
      this.saving.set(false);
    }
  }

  /** Practice mode with immediate feedback: grade this answer now. */
  protected async check(q: Question): Promise<void> {
    clearTimeout(this.saveTimers.get(q.id));
    const reveal = await this.save(q, true);
    if (reveal) this.reveals.update(m => ({ ...m, [q.id]: reveal }));
  }

  protected go(i: number): void {
    this.index.set(Math.max(0, Math.min(this.questions().length - 1, i)));
    this.sheetOpen.set(false);
  }

  protected async submit(auto = false): Promise<void> {
    const attempt = this.attempt();
    if (!attempt || this.submitting()) return;
    if (!auto) {
      const unanswered = this.questions().length - this.answeredCount();
      const ok = await this.confirm.ask({
        title: this.i18n.t('attempt.submitTitle'),
        message: unanswered > 0 ? this.i18n.t('attempt.submitUnanswered', { n: unanswered }) : this.i18n.t('attempt.submitMessage'),
        confirm: this.i18n.t('attempt.submit')
      });
      if (!ok) return;
    }
    this.submitting.set(true);
    for (const t of this.saveTimers.values()) clearTimeout(t);
    try {
      // Flush any pending answers before submitting.
      await Promise.all(this.questions().filter(q => this.isAnswered(q.id) && !this.reveals()[q.id]).map(q => this.save(q, true)));
      await this.api.post<AttemptResult>(`/api/attempts/${attempt.id}/submit`);
      if (auto) this.toast.show(this.i18n.t('attempt.timeUp'));
      void this.router.navigate(['/quiz/result', attempt.id], { replaceUrl: true });
    } catch (e) {
      this.toast.error(e);
      this.submitting.set(false);
    }
  }

  protected typeLabel(q: Question): string {
    return this.i18n.t('questionTypes.' + q.type);
  }

  ngOnDestroy(): void {
    if (this.timer) clearInterval(this.timer);
    for (const t of this.saveTimers.values()) clearTimeout(t);
  }
}
