import { ChangeDetectionStrategy, Component, HostListener, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { Flashcard, IntervalPreview, Rating } from '../../core/models';
import { StoreService } from '../../core/store.service';
import { ToastService } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { MarkdownComponent } from '../../shared/markdown.component';
import { EmptyStateComponent } from '../../shared/ui';

const RATINGS: Rating[] = ['again', 'hard', 'good', 'easy'];

/** Spaced-repetition review session (FSRS). */
@Component({
  selector: 'rw-review',
  imports: [RouterLink, TranslatePipe, IconComponent, MarkdownComponent, EmptyStateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page review">
      <header class="top">
        <a routerLink="/flashcards" class="btn ghost sm"><rw-icon name="arrow-left" [size]="14" /> {{ 'nav.flashcards' | t }}</a>
        @if (queue().length > 0 && !finished()) {
          <div class="progress grow"><div class="bar"><span [style.width.%]="(100 * index()) / queue().length"></span></div></div>
          <span class="small num subtle">{{ index() + 1 }} / {{ queue().length }}</span>
        }
      </header>

      @if (loading()) {
        <div class="skeleton" style="height: 360px; border-radius: 22px"></div>
      } @else if (queue().length === 0) {
        <div class="card"><rw-empty icon="check-circle" [title]="'review.nothingDue' | t" [text]="'review.nothingDueText' | t">
          <a class="btn" routerLink="/flashcards">{{ 'nav.flashcards' | t }}</a></rw-empty></div>
      } @else if (finished()) {
        <div class="card done">
          <span class="done-icon"><rw-icon name="trophy" [size]="28" /></span>
          <h2>{{ 'review.sessionDone' | t }}</h2>
          <div class="summary">
            <div class="stat"><span class="value num">{{ i18n.number(reviewed()) }}</span><span class="label">{{ 'review.reviewed' | t }}</span></div>
            <div class="stat"><span class="value num">{{ i18n.number(forgotten()) }}</span><span class="label">{{ 'review.forgotten' | t }}</span></div>
            <div class="stat"><span class="value num">{{ i18n.duration(minutes()) }}</span><span class="label">{{ 'review.time' | t }}</span></div>
          </div>
          <p class="muted small">{{ 'review.doneText' | t }}</p>
          <div class="row"><a class="btn" routerLink="/">{{ 'nav.home' | t }}</a><button class="btn primary" type="button" (click)="restart()">{{ 'review.again' | t }}</button></div>
        </div>
      } @else if (current(); as card) {
        <article class="flashcard card" [class.flipped]="revealed()" (click)="!revealed() && reveal()">
          <div class="meta row between">
            <span class="badge"><span class="subject-dot" [style.background]="store.subject(card.subjectId)?.color"></span> {{ store.subjectName(card.subjectId) }}</span>
            <span class="badge" [class.brand]="card.state === 'new'">{{ 'flashcards.states.' + card.state | t }}</span>
          </div>
          <div class="face front"><rw-markdown [content]="card.front" /></div>
          @if (revealed()) {
            <hr class="divider" />
            <div class="face back"><rw-markdown [content]="card.back" /></div>
            @if (card.sourcePage && card.bookId) {
              <a class="small source" [routerLink]="['/read', card.bookId]" [queryParams]="{ page: card.sourcePage }" (click)="$event.stopPropagation()">
                <rw-icon name="book-open" [size]="14" /> {{ 'common.pageN' | t: { n: card.sourcePage } }}</a>
            }
          } @else {
            <p class="hint subtle small">{{ 'review.tapToReveal' | t }}</p>
          }
        </article>

        <div class="controls">
          @if (!revealed()) {
            <button class="btn primary lg block" type="button" (click)="reveal()">{{ 'review.showAnswer' | t }} <kbd>Space</kbd></button>
          } @else {
            <div class="ratings" role="group" [attr.aria-label]="'review.howWell' | t">
              @for (r of ratings; track r; let i = $index) {
                <button type="button" class="rating" [class]="'rating ' + r" (click)="rate(r)" [disabled]="busy()">
                  <span class="r-label">{{ 'review.ratings.' + r | t }}</span>
                  <span class="r-interval num">{{ preview() ? interval(r) : '…' }}</span>
                  <kbd>{{ i + 1 }}</kbd>
                </button>
              }
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .review { max-width: 760px; }
    .top { display: flex; align-items: center; gap: 12px; margin-bottom: 20px; }
    .flashcard { min-height: 320px; padding: 28px 32px; display: flex; flex-direction: column; gap: 14px; border-radius: var(--radius-xl); cursor: pointer; background: var(--reading); box-shadow: var(--shadow-2); }
    .flashcard.flipped { cursor: default; }
    .face { font-size: 1.1rem; }
    .front :host ::ng-deep .prose { font-size: 1.2rem; }
    .hint { margin-top: auto; text-align: center; }
    .source { display: inline-flex; gap: 6px; align-items: center; margin-top: auto; }
    .controls { margin-top: 18px; }
    .ratings { display: grid; grid-template-columns: repeat(4, 1fr); gap: 8px; }
    .rating { display: flex; flex-direction: column; align-items: center; gap: 2px; padding: 12px 6px; border-radius: var(--radius); border: 1px solid var(--line); background: var(--surface); cursor: pointer; position: relative; }
    .rating:hover { transform: translateY(-1px); box-shadow: var(--shadow-1); }
    .r-label { font-weight: 600; font-size: var(--text-sm); }
    .r-interval { font-size: var(--text-xs); color: var(--ink-3); }
    .again .r-label { color: var(--danger); } .hard .r-label { color: var(--warning); } .good .r-label { color: var(--success); } .easy .r-label { color: var(--info); }
    kbd { font-family: var(--font-latin); font-size: 0.65rem; padding: 1px 5px; border-radius: 4px; border: 1px solid var(--line-strong); color: var(--ink-3); background: var(--surface-2); }
    .rating kbd { position: absolute; inset-block-start: 6px; inset-inline-end: 6px; }
    .done { text-align: center; display: flex; flex-direction: column; align-items: center; gap: 14px; padding: 36px; }
    .done-icon { width: 60px; height: 60px; border-radius: 50%; display: grid; place-items: center; background: var(--gold-soft); color: var(--gold); }
    .summary { display: grid; grid-template-columns: repeat(3, 1fr); gap: 24px; }
    @media (max-width: 560px) { .ratings { grid-template-columns: repeat(2, 1fr); } .flashcard { padding: 20px; } }
  `]
})
export class ReviewComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly store = inject(StoreService);
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly toast = inject(ToastService);

  protected readonly ratings = RATINGS;
  protected readonly queue = signal<Flashcard[]>([]);
  protected readonly index = signal(0);
  protected readonly revealed = signal(false);
  protected readonly preview = signal<IntervalPreview | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly reviewed = signal(0);
  protected readonly forgotten = signal(0);
  private startedAt = Date.now();
  private cardShownAt = Date.now();

  protected readonly current = computed(() => this.queue()[this.index()] ?? null);
  protected readonly finished = computed(() => this.queue().length > 0 && this.index() >= this.queue().length);
  protected readonly minutes = computed(() => Math.max(1, Math.round((Date.now() - this.startedAt) / 60000 + this.reviewed() * 0)));

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    const params = this.route.snapshot.queryParamMap;
    try {
      const result = await this.api.get<{ cards: Flashcard[] }>('/api/flashcards/due', { subjectId: params.get('subjectId'), bookId: params.get('bookId'), limit: 60 });
      this.queue.set(result.cards);
      this.index.set(0);
      this.startedAt = Date.now();
      this.cardShownAt = Date.now();
    } catch (e) {
      this.toast.error(e);
    } finally {
      this.loading.set(false);
    }
  }

  protected restart(): void {
    this.reviewed.set(0);
    this.forgotten.set(0);
    void this.load();
  }

  protected async reveal(): Promise<void> {
    const card = this.current();
    if (!card) return;
    this.revealed.set(true);
    this.preview.set(null);
    try {
      this.preview.set(await this.api.get<IntervalPreview>(`/api/flashcards/${card.id}/preview`));
    } catch { /* intervals are informational */ }
  }

  protected async rate(rating: Rating): Promise<void> {
    const card = this.current();
    if (!card || this.busy()) return;
    this.busy.set(true);
    try {
      const result = await this.api.post<{ card: Flashcard }>(`/api/flashcards/${card.id}/review`, { rating, durationMs: Date.now() - this.cardShownAt });
      this.reviewed.update(n => n + 1);
      if (rating === 'again') {
        this.forgotten.update(n => n + 1);
        // Relearning cards come back later in the same session.
        this.queue.update(list => [...list, result.card]);
      }
      this.index.update(i => i + 1);
      this.revealed.set(false);
      this.cardShownAt = Date.now();
    } catch (e) {
      this.toast.error(e);
    } finally {
      this.busy.set(false);
    }
  }

  protected interval(rating: Rating): string {
    const days = this.preview()?.[rating] ?? 0;
    if (days < 1 / 24) return this.i18n.t('review.minutes', { n: Math.max(1, Math.round(days * 1440)) });
    if (days < 1) return this.i18n.t('review.hours', { n: Math.round(days * 24) });
    if (days < 31) return this.i18n.t('review.days', { n: Math.round(days) });
    if (days < 365) return this.i18n.t('review.months', { n: Math.round(days / 30) });
    return this.i18n.t('review.years', { n: Math.round((days / 365) * 10) / 10 });
  }

  @HostListener('document:keydown', ['$event'])
  protected onKey(event: KeyboardEvent): void {
    if ((event.target as HTMLElement).closest('input, textarea')) return;
    if (!this.current() || this.finished()) return;
    if (event.code === 'Space' && !this.revealed()) { event.preventDefault(); void this.reveal(); }
    const n = Number(event.key);
    if (this.revealed() && n >= 1 && n <= 4) { event.preventDefault(); void this.rate(RATINGS[n - 1]); }
  }
}
