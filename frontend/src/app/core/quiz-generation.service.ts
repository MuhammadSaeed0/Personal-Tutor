import { Injectable, effect, inject, signal, untracked } from '@angular/core';
import { ApiError, ApiService } from './api.service';
import { I18nService } from './i18n.service';
import { QuizDetail } from './models';
import { RealtimeService } from './realtime.service';
import { ToastService, errorMessage } from './toast.service';

export type QuizGenerationState = { status: 'generating' | 'ready' | 'failed'; errorCode: string | null; title: string };

/**
 * Follows quizzes being written in the background. Updates arrive over SignalR, and every few seconds
 * the server is also asked directly, because real-time delivery is best effort.
 * When nobody on screen is waiting for a finished quiz, a toast says so (it never starts an attempt by itself).
 */
@Injectable({ providedIn: 'root' })
export class QuizGenerationService {
  private readonly api = inject(ApiService);
  private readonly realtime = inject(RealtimeService);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(I18nService);

  readonly states = signal<Record<string, QuizGenerationState>>({});
  private readonly watched = new Set<string>();
  private timer: ReturnType<typeof setInterval> | null = null;

  constructor() {
    effect(() => {
      const updates = this.realtime.quizzes();
      untracked(() => {
        for (const [id, u] of Object.entries(updates)) {
          const current = this.states()[id];
          if (current?.status === 'generating' && u.status !== 'generating') this.finish(id, { status: u.status, errorCode: u.errorCode, title: u.title });
        }
      });
    });
  }

  /** Starts following a quiz the server has just queued. */
  track(id: string, title: string): void {
    this.states.update(s => ({ ...s, [id]: { status: 'generating', errorCode: null, title } }));
    this.timer ??= setInterval(() => void this.poll(), 3000);
  }

  /** A screen that will react to the result itself (no toast needed while it is open). */
  watch(id: string): void { this.watched.add(id); }
  unwatch(id: string): void { this.watched.delete(id); }

  private async poll(): Promise<void> {
    const pending = Object.entries(this.states()).filter(([, s]) => s.status === 'generating').map(([id]) => id);
    if (pending.length === 0) {
      if (this.timer) clearInterval(this.timer);
      this.timer = null;
      return;
    }
    for (const id of pending) {
      try {
        const q = (await this.api.get<QuizDetail>(`/api/quizzes/${id}`)).quiz;
        if (q.status !== 'generating') this.finish(id, { status: q.status, errorCode: q.errorCode, title: q.title });
      } catch (e) {
        if (e instanceof ApiError && e.status === 404) this.states.update(s => { const { [id]: _, ...rest } = s; return rest; });
      }
    }
  }

  private finish(id: string, state: QuizGenerationState): void {
    if (this.states()[id]?.status !== 'generating') return;
    this.states.update(s => ({ ...s, [id]: state }));
    if (this.watched.has(id)) return;
    if (state.status === 'ready') {
      this.toast.show(this.i18n.t('builder.readyToast', { title: state.title }), 'success', 9000, { link: ['/quizzes'], label: this.i18n.t('builder.openQuizzes') });
    } else {
      this.toast.show(this.i18n.t('builder.failedToast', { title: state.title }) + ' ' + errorMessage(this.i18n, new ApiError({ status: 503, code: state.errorCode ?? 'unknown_error' })), 'error', 9000);
    }
  }
}
