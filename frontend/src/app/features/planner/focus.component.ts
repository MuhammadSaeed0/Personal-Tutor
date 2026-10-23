import { ChangeDetectionStrategy, Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { Book, Paged, StudySession } from '../../core/models';
import { StoreService } from '../../core/store.service';
import { ToastService } from '../../core/toast.service';
import { BrandComponent } from '../../layout/brand.component';
import { IconComponent } from '../../shared/icon.component';
import { RingComponent, clock } from '../../shared/ui';

type Phase = 'setup' | 'focus' | 'break' | 'finished';

/** Distraction-free focus timer (Pomodoro presets or custom), connected to subjects and books. */
@Component({
  selector: 'rw-focus',
  imports: [FormsModule, RouterLink, TranslatePipe, IconComponent, RingComponent, BrandComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './focus.component.html',
  styleUrl: './focus.component.scss'
})
export class FocusComponent implements OnDestroy {
  protected readonly i18n = inject(I18nService);
  protected readonly store = inject(StoreService);
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);

  protected readonly phase = signal<Phase>('setup');
  protected readonly running = signal(false);
  protected readonly remaining = signal(0);
  protected readonly total = signal(0);
  protected readonly focusedSeconds = signal(0);
  protected readonly cycles = signal(0);
  protected readonly history = signal<StudySession[]>([]);
  protected readonly books = signal<Book[]>([]);

  protected focusMinutes = this.auth.profile()?.preferences.focusMinutes ?? 25;
  protected breakMinutes = this.auth.profile()?.preferences.breakMinutes ?? 5;
  protected goal = '';
  protected notes = '';
  protected subjectId: number | null = null;
  protected bookId: string | null = null;

  private session: StudySession | null = null;
  private interval?: ReturnType<typeof setInterval>;
  private lastTick = 0;

  protected readonly progress = computed(() => (this.total() ? (100 * (this.total() - this.remaining())) / this.total() : 0));
  protected readonly display = computed(() => clock(this.remaining()));

  constructor() {
    void this.loadHistory();
    void this.api.get<Book[]>('/api/books').then(b => this.books.set(b.filter(x => x.status === 'ready' || x.status === 'readyWithWarnings')));
  }

  protected preset(focus: number, rest: number): void {
    this.focusMinutes = focus;
    this.breakMinutes = rest;
  }

  protected async start(): Promise<void> {
    try {
      const book = this.books().find(b => b.id === this.bookId);
      this.session = await this.api.post<StudySession>('/api/sessions', {
        subjectId: book?.subjectId ?? this.subjectId, bookId: this.bookId, goal: this.goal || null, plannedMinutes: this.focusMinutes
      });
      this.focusedSeconds.set(0);
      this.cycles.set(0);
      this.beginPhase('focus');
    } catch (e) {
      this.toast.error(e);
    }
  }

  private beginPhase(phase: 'focus' | 'break'): void {
    const seconds = (phase === 'focus' ? this.focusMinutes : this.breakMinutes) * 60;
    this.phase.set(phase);
    this.total.set(seconds);
    this.remaining.set(seconds);
    this.resume();
  }

  protected pause(): void {
    this.running.set(false);
    clearInterval(this.interval);
  }

  protected resume(): void {
    this.running.set(true);
    this.lastTick = Date.now();
    clearInterval(this.interval);
    this.interval = setInterval(() => this.tick(), 500);
  }

  private tick(): void {
    const now = Date.now();
    const delta = (now - this.lastTick) / 1000;
    this.lastTick = now;
    if (this.phase() === 'focus') this.focusedSeconds.update(s => s + delta);
    const left = Math.max(0, this.remaining() - delta);
    this.remaining.set(left);
    if (left > 0) return;
    this.chime();
    if (this.phase() === 'focus') {
      this.cycles.update(c => c + 1);
      this.beginPhase('break');
    } else {
      this.beginPhase('focus');
    }
  }

  protected skipBreak(): void {
    this.beginPhase('focus');
  }

  protected stop(): void {
    this.pause();
    this.phase.set('finished');
  }

  protected async finish(): Promise<void> {
    if (this.session) {
      try {
        await this.api.post(`/api/sessions/${this.session.id}/complete`, { focusedSeconds: Math.round(this.focusedSeconds()), notes: this.notes || null });
        this.toast.success('focus.saved');
      } catch (e) {
        this.toast.error(e);
      }
    }
    this.session = null;
    this.notes = '';
    this.phase.set('setup');
    await this.loadHistory();
  }

  private async loadHistory(): Promise<void> {
    try {
      const page = await this.api.get<Paged<StudySession>>('/api/sessions', { pageSize: 8 });
      this.history.set(page.items);
    } catch { /* history is secondary */ }
  }

  private chime(): void {
    try {
      const ctx = new AudioContext();
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();
      osc.frequency.value = 660;
      gain.gain.setValueAtTime(0.0001, ctx.currentTime);
      gain.gain.exponentialRampToValueAtTime(0.15, ctx.currentTime + 0.02);
      gain.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + 0.8);
      osc.connect(gain).connect(ctx.destination);
      osc.start();
      osc.stop(ctx.currentTime + 0.85);
    } catch { /* audio unavailable */ }
  }

  protected minutes(seconds: number): string {
    return this.i18n.duration(Math.round(seconds / 60));
  }

  ngOnDestroy(): void {
    clearInterval(this.interval);
    if (this.session && this.focusedSeconds() > 30) {
      void this.api.post(`/api/sessions/${this.session.id}/complete`, { focusedSeconds: Math.round(this.focusedSeconds()), notes: this.notes || null }).catch(() => undefined);
    }
  }
}
