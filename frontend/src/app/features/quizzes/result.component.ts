import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { Attempt, AttemptResult, QuestionReview } from '../../core/models';
import { ToastService } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { MarkdownComponent } from '../../shared/markdown.component';
import { RingComponent, clock } from '../../shared/ui';

@Component({
  selector: 'rw-result',
  imports: [RouterLink, TranslatePipe, IconComponent, MarkdownComponent, RingComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './result.component.html',
  styleUrl: './result.component.scss'
})
export class ResultComponent {
  readonly attemptId = input.required<string>();
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  protected readonly result = signal<AttemptResult | null>(null);
  protected readonly filter = signal<'all' | 'wrong' | 'pending'>('all');
  protected readonly cardsCreated = signal(false);

  protected readonly visible = computed(() => {
    const r = this.result();
    if (!r) return [];
    return r.questions.filter(q => this.filter() === 'all'
      || (this.filter() === 'wrong' && q.response?.isCorrect === false)
      || (this.filter() === 'pending' && q.response?.gradingMethod === 'pending'));
  });
  protected readonly color = computed(() => {
    const p = this.result()?.percent ?? 0;
    return p >= 75 ? 'var(--success)' : p >= 50 ? 'var(--warning)' : 'var(--danger)';
  });
  protected readonly duration = computed(() => clock(this.result()?.durationSeconds ?? 0));

  constructor() {
    effect(() => {
      const id = this.attemptId();
      untracked(() => void this.api.get<AttemptResult>(`/api/attempts/${id}/result`).then(r => this.result.set(r)).catch(e => {
        this.toast.error(e);
        void this.router.navigateByUrl('/quizzes');
      }));
    });
  }

  protected studentAnswer(q: QuestionReview): string {
    const a = q.response?.answer;
    if (a === null || a === undefined || a === '') return this.i18n.t('result.noAnswer');
    const o = q.question.options;
    switch (q.question.type) {
      case 'multipleChoice': return o.choices?.[a as number] ?? String(a);
      case 'trueFalse': return this.i18n.t(a ? 'attempt.true' : 'attempt.false');
      case 'matching': return (a as (number | null)[]).map((r, i) => `${o.left?.[i]} → ${r === null ? '—' : o.right?.[r]}`).join('\n');
      case 'ordering': return (a as number[]).map(i => o.items?.[i]).join(' → ');
      default: return String(a);
    }
  }

  protected async selfAssess(q: QuestionReview, correct: boolean): Promise<void> {
    const r = this.result();
    if (!r) return;
    try {
      this.result.set(await this.api.post<AttemptResult>(`/api/attempts/${r.attemptId}/responses/${q.question.id}/self-assess`, { correct }));
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected async cardsFromMistakes(): Promise<void> {
    const r = this.result();
    if (!r) return;
    try {
      const cards = await this.api.post<unknown[]>(`/api/flashcards/from-attempt/${r.attemptId}`);
      this.cardsCreated.set(true);
      this.toast.success('tools.cardsSaved', { n: cards.length });
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected async retake(): Promise<void> {
    const r = this.result();
    if (!r) return;
    const attempt = await this.api.post<Attempt>(`/api/quizzes/${r.quizId}/attempts`);
    void this.router.navigate(['/quiz/attempt', attempt.id]);
  }
}
