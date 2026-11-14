import { ChangeDetectionStrategy, Component, OnDestroy, effect, inject, input, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError, ApiService } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { StudyPack } from '../../core/models';
import { ToastService, errorMessage } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';

/** The study pack of a source: a button to prepare it, its progress while it is made, and links when it is done. */
@Component({
  selector: 'rw-study-pack',
  imports: [RouterLink, TranslatePipe, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (loaded()) {
      <section class="card pack" aria-live="polite">
        <rw-icon name="sparkles" [size]="20" class="icon" />
        <div class="grow">
          <h3>{{ 'studyPack.title' | t }}</h3>
          @if (pack(); as p) {
            @switch (p.status) {
              @case ('pending') { <p class="small muted">{{ (ready() ? 'studyPack.starting' : 'studyPack.waiting') | t }}</p> }
              @case ('running') {
                <p class="small muted">{{ 'studyPack.running' | t: { done: p.done, total: p.total } }}</p>
                <div class="bar"><span [style.width.%]="p.total ? (100 * p.done) / p.total : 5"></span></div>
              }
              @case ('completed') {
                <p class="small muted">{{ 'studyPack.done' | t: { summaries: p.summaries, cards: p.cards, quizzes: p.quizzes } }}</p>
                <div class="row wrap links">
                  <a class="btn sm" routerLink="/summaries"><rw-icon name="file-text" [size]="14" /> {{ 'nav.summaries' | t }}</a>
                  <a class="btn sm" routerLink="/flashcards/review"><rw-icon name="layers" [size]="14" /> {{ 'nav.flashcards' | t }}</a>
                  <a class="btn sm" routerLink="/quizzes"><rw-icon name="list-checks" [size]="14" /> {{ 'nav.quizzes' | t }}</a>
                </div>
              }
              @case ('failed') {
                <p class="small muted">{{ 'studyPack.stopped' | t: { summaries: p.summaries, cards: p.cards, quizzes: p.quizzes } }} {{ reason(p.errorCode) }}</p>
                <button class="btn sm" type="button" (click)="request()" [disabled]="busy()">{{ 'studyPack.retry' | t }}</button>
              }
            }
          } @else {
            <p class="small muted">{{ 'studyPack.lead' | t: { n: 10 } }}</p>
            <button class="btn primary sm" type="button" (click)="request()" [disabled]="busy() || !ready()">{{ 'studyPack.prepare' | t }}</button>
          }
        </div>
      </section>
    }
  `,
  styles: [`
    .pack { display: flex; gap: 12px; align-items: flex-start; margin-bottom: 16px; }
    .pack h3 { margin: 0 0 4px; font-size: var(--text-md); }
    .pack p { margin: 0 0 8px; }
    .icon { color: var(--brand); flex: none; margin-top: 2px; }
    .links { gap: 8px; }
  `]
})
export class StudyPackComponent implements OnDestroy {
  readonly bookId = input.required<string>();
  readonly ready = input(false);

  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(I18nService);
  protected readonly pack = signal<StudyPack | null>(null);
  protected readonly loaded = signal(false);
  protected readonly busy = signal(false);
  private timer: ReturnType<typeof setInterval> | null = null;

  constructor() {
    effect(() => {
      const id = this.bookId();
      untracked(() => void this.load(id));
    });
  }

  private async load(id: string): Promise<void> {
    try {
      this.pack.set((await this.api.get<StudyPack | null>(`/api/books/${id}/study-pack`)) ?? null);
    } catch { /* the card simply offers to prepare a pack */ }
    this.loaded.set(true);
    this.follow();
  }

  /** Polls while the pack is being made. */
  private follow(): void {
    const active = this.pack()?.status === 'pending' || this.pack()?.status === 'running';
    if (active && !this.timer) this.timer = setInterval(() => void this.load(this.bookId()), 4000);
    if (!active && this.timer) { clearInterval(this.timer); this.timer = null; }
  }

  protected async request(): Promise<void> {
    this.busy.set(true);
    try {
      this.pack.set(await this.api.post<StudyPack>(`/api/books/${this.bookId()}/study-pack`));
      this.follow();
    } catch (e) {
      this.toast.error(e);
    } finally {
      this.busy.set(false);
    }
  }

  protected reason(code: string | null): string {
    return code ? errorMessage(this.i18n, new ApiError({ status: 503, code })) : '';
  }

  ngOnDestroy(): void {
    if (this.timer) clearInterval(this.timer);
  }
}
