import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { Attempt, Mistake, MistakesOverview } from '../../core/models';
import { StoreService } from '../../core/store.service';
import { ToastService } from '../../core/toast.service';
import { DiagramComponent } from '../../shared/diagram.component';
import { IconComponent } from '../../shared/icon.component';
import { MarkdownComponent } from '../../shared/markdown.component';
import { EmptyStateComponent } from '../../shared/ui';

interface SubjectGroup { subjectId: number; due: number; chapters: { title: string; items: Mistake[] }[]; }

/** The mistakes notebook: wrong answers grouped by subject and chapter, with a one-click review quiz of the due ones. */
@Component({
  selector: 'rw-mistakes',
  imports: [RouterLink, TranslatePipe, IconComponent, EmptyStateComponent, MarkdownComponent, DiagramComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <header class="page-header">
        <div><div class="eyebrow">{{ 'nav.mistakes' | t }}</div><h1>{{ 'mistakes.title' | t }}</h1><p>{{ 'mistakes.subtitle' | t }}</p></div>
        <label class="row small"><input type="checkbox" [checked]="showMastered()" (change)="toggleMastered()" /> {{ 'mistakes.showMastered' | t }}</label>
      </header>

      @if (data(); as d) {
        <div class="stats">
          <div class="card stat"><strong class="num">{{ i18n.number(d.dueCount) }}</strong><span class="small muted">{{ 'mistakes.due' | t }}</span></div>
          <div class="card stat"><strong class="num">{{ i18n.number(d.openCount) }}</strong><span class="small muted">{{ 'mistakes.open' | t }}</span></div>
          <div class="card stat"><strong class="num">{{ i18n.number(d.masteredCount) }}</strong><span class="small muted">{{ 'mistakes.mastered' | t }}</span></div>
        </div>

        @if (d.items.length === 0) {
          <div class="card"><rw-empty icon="check-circle" [title]="'mistakes.emptyTitle' | t" [text]="'mistakes.emptyText' | t">
            <a class="btn primary" routerLink="/quizzes/new">{{ 'quizzes.newQuiz' | t }}</a></rw-empty></div>
        }

        @for (g of groups(); track g.subjectId) {
          <section class="subject">
            <div class="row between subject-head">
              <h2><span class="subject-dot" [style.background]="store.subject(g.subjectId)?.color"></span> {{ store.subjectName(g.subjectId) }}</h2>
              @if (g.due > 0) {
                <button class="btn primary" type="button" (click)="retest(g.subjectId)" [disabled]="busy()">
                  <rw-icon name="rotate-ccw" [size]="16" /> {{ 'mistakes.retest' | t: { n: g.due } }}</button>
              } @else {
                <span class="small muted">{{ 'mistakes.nothingDue' | t }}</span>
              }
            </div>
            @for (c of g.chapters; track c.title) {
              <h3 class="chapter small muted">{{ c.title || ('mistakes.noChapter' | t) }}</h3>
              <div class="items">
                @for (m of c.items; track m.id) {
                  <article class="card item" [class.mastered]="m.masteredAt">
                    <div class="row between meta xsmall">
                      <span>
                        @if (m.masteredAt) { <span class="badge brand">{{ 'mistakes.masteredBadge' | t }}</span> }
                        @else if (m.due) { <span class="badge gold">{{ 'mistakes.dueBadge' | t }}</span> }
                        @else { <span class="subtle">{{ 'mistakes.nextReview' | t: { date: i18n.date(m.nextReviewAt) } }}</span> }
                        <span class="subtle"> · {{ 'mistakes.timesWrong' | t: { n: m.timesWrong } }}</span>
                      </span>
                      <button class="btn ghost icon sm" type="button" (click)="remove(m)" [attr.aria-label]="'common.delete' | t"><rw-icon name="trash" [size]="14" /></button>
                    </div>
                    <rw-markdown [content]="m.prompt" />
                    @if (m.diagramSvg) { <rw-diagram [svg]="m.diagramSvg" [alt]="m.diagramAlt" /> }
                    <div class="answer"><span class="xsmall muted">{{ 'mistakes.correctAnswer' | t }}</span><rw-markdown [content]="m.correctAnswer" /></div>
                    @if (m.explanation) {
                      <details><summary class="small">{{ 'mistakes.explanation' | t }}</summary><rw-markdown [content]="m.explanation" /></details>
                    }
                    @if (m.sourcePage && m.bookId) {
                      <a class="xsmall" [routerLink]="['/read', m.bookId]" [queryParams]="{ page: m.sourcePage }">
                        <rw-icon name="book-open" [size]="12" /> {{ m.bookTitle }} · {{ 'common.pageN' | t: { n: m.sourcePage } }}</a>
                    }
                  </article>
                }
              </div>
            }
          </section>
        }
      } @else {
        <div class="card"><span class="spinner"></span></div>
      }
    </div>
  `,
  styles: [`
    .stats { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; margin-bottom: 20px; }
    .stat { display: flex; flex-direction: column; gap: 2px; padding: 14px 16px; }
    .stat strong { font-size: var(--text-xl); color: var(--brand); }
    .subject { margin-bottom: 28px; }
    .subject-head { margin-bottom: 8px; flex-wrap: wrap; gap: 8px; }
    .subject-head h2 { display: flex; align-items: center; gap: 8px; font-size: var(--text-lg); margin: 0; }
    .chapter { margin: 14px 0 8px; font-weight: 600; }
    .items { display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); gap: 12px; }
    .item { display: flex; flex-direction: column; gap: 8px; }
    .item.mastered { opacity: .7; }
    .meta { align-items: center; }
    .answer { background: var(--brand-soft); border-radius: 10px; padding: 8px 12px; }
    details summary { cursor: pointer; color: var(--muted); }
    @media (max-width: 560px) { .items { grid-template-columns: 1fr; } .stats { gap: 8px; } }
  `]
})
export class MistakesComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  protected readonly i18n = inject(I18nService);
  protected readonly store = inject(StoreService);

  protected readonly data = signal<MistakesOverview | null>(null);
  protected readonly showMastered = signal(false);
  protected readonly busy = signal(false);

  protected readonly groups = computed<SubjectGroup[]>(() => {
    const items = this.data()?.items ?? [];
    const bySubject = new Map<number, Mistake[]>();
    for (const m of items) bySubject.set(m.subjectId, [...(bySubject.get(m.subjectId) ?? []), m]);
    return [...bySubject.entries()].map(([subjectId, list]) => {
      const byChapter = new Map<string, Mistake[]>();
      for (const m of list) byChapter.set(m.chapterTitle ?? '', [...(byChapter.get(m.chapterTitle ?? '') ?? []), m]);
      return { subjectId, due: list.filter(m => m.due).length, chapters: [...byChapter.entries()].map(([title, items]) => ({ title, items })) };
    }).sort((a, b) => b.due - a.due);
  });

  constructor() { void this.load(); }

  private async load(): Promise<void> {
    try {
      this.data.set(await this.api.get<MistakesOverview>(`/api/mistakes?includeMastered=${this.showMastered()}`));
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected toggleMastered(): void {
    this.showMastered.update(v => !v);
    void this.load();
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

  protected async remove(m: Mistake): Promise<void> {
    try {
      await this.api.delete(`/api/mistakes/${m.id}`);
      await this.load();
    } catch (e) {
      this.toast.error(e);
    }
  }
}
