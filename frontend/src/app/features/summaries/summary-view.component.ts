import { ChangeDetectionStrategy, Component, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService, saveBlob } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { GeneratedFlashcards, Summary, SummaryStyle } from '../../core/models';
import { ToastService } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { CitationClick, MarkdownComponent } from '../../shared/markdown.component';
import { ConfirmService } from '../../shared/ui';
import { SUMMARY_STYLES } from '../reader/study-tools-panel.component';

@Component({
  selector: 'rw-summary-view',
  imports: [FormsModule, RouterLink, TranslatePipe, IconComponent, MarkdownComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page narrow">
      <a routerLink="/summaries" class="back small"><rw-icon name="arrow-left" [size]="14" /> {{ 'nav.summaries' | t }}</a>
      @if (summary(); as s) {
        <header class="page-header">
          <div class="grow">
            <div class="row"><span class="badge brand">{{ 'summaryStyles.' + s.style | t }}</span>
              <span class="xsmall subtle">{{ i18n.date(s.createdAt) }}</span></div>
            @if (editing()) { <input class="input title-input" [(ngModel)]="title" dir="auto" [attr.aria-label]="'notes.title' | t" /> }
            @else { <h1 dir="auto">{{ s.title }}</h1> }
          </div>
          <div class="row wrap">
            @if (editing()) {
              <button class="btn ghost" type="button" (click)="editing.set(false)">{{ 'common.cancel' | t }}</button>
              <button class="btn primary" type="button" (click)="save()">{{ 'common.save' | t }}</button>
            } @else {
              <button class="btn" type="button" (click)="startEdit()"><rw-icon name="pencil" [size]="16" /> {{ 'common.edit' | t }}</button>
              <button class="btn" type="button" (click)="export()"><rw-icon name="download" [size]="16" /> {{ 'common.export' | t }}</button>
              <button class="btn" type="button" (click)="print()"><rw-icon name="file-text" [size]="16" /> {{ 'summaries.print' | t }}</button>
              <button class="btn ghost icon" type="button" (click)="remove()" [attr.aria-label]="'common.delete' | t"><rw-icon name="trash" [size]="16" /></button>
            }
          </div>
        </header>

        @for (w of s.warnings; track w) { <div class="alert warning small" style="margin-bottom: 10px"><rw-icon name="alert-triangle" [size]="16" /> {{ warningText(w) }}</div> }

        <article class="card doc">
          @if (editing()) {
            <textarea class="textarea editor" [(ngModel)]="content" dir="auto" [attr.aria-label]="'summaries.content' | t"></textarea>
            <p class="xsmall subtle">{{ 'summaries.editHint' | t }}</p>
          } @else {
            <rw-markdown [content]="s.content" [citations]="s.citations" [bookId]="s.bookId" (citationClick)="open($event)" />
          }
        </article>

        <p class="xsmall subtle disclaimer">{{ 'tools.summaryDisclaimer' | t }}</p>

        <section class="card actions-card">
          <h3 class="small">{{ 'summaries.next' | t }}</h3>
          <div class="row wrap">
            <button class="btn" type="button" (click)="makeCards()" [disabled]="busy()"><rw-icon name="layers" [size]="16" /> {{ 'summaries.toCards' | t }}</button>
            <a class="btn" routerLink="/quizzes/new" [queryParams]="{ bookId: s.bookId, chapterId: s.chapterId ?? undefined, pageStart: s.chapterId ? undefined : s.pageStart, pageEnd: s.chapterId ? undefined : s.pageEnd }"><rw-icon name="list-checks" [size]="16" /> {{ 'summaries.toQuiz' | t }}</a>
            <span class="regen">
              <select class="select" [(ngModel)]="regenStyle" [attr.aria-label]="'tools.style' | t">
                @for (st of styles; track st) { <option [ngValue]="st">{{ 'summaryStyles.' + st | t }}</option> }
              </select>
              <select class="select" [(ngModel)]="regenLanguage" [attr.aria-label]="'tutor.answerIn' | t">
                <option value="ar">العربية</option><option value="en">English</option><option value="fr">Français</option>
              </select>
              <button class="btn soft" type="button" (click)="regenerate()" [disabled]="busy()">@if (busy()) { <span class="spinner"></span> } {{ 'summaries.regenerate' | t }}</button>
            </span>
          </div>
        </section>
      } @else {
        <div class="skeleton" style="height: 400px"></div>
      }
    </div>
  `,
  styles: [`
    .narrow { max-width: 900px; }
    .back { display: inline-flex; align-items: center; gap: 6px; margin-bottom: 12px; color: var(--ink-2); }
    .title-input { font-size: 1.3rem; font-weight: 600; margin-top: 6px; }
    .doc { padding: 28px 32px; background: var(--reading); }
    .editor { min-height: 60vh; font-family: ui-monospace, Consolas, monospace; font-size: 0.9rem; }
    .disclaimer { margin: 10px 4px 20px; }
    .actions-card { display: flex; flex-direction: column; gap: 10px; }
    .regen { display: inline-flex; gap: 6px; align-items: center; flex-wrap: wrap; }
    .regen .select { width: auto; min-height: 38px; }
    @media print { .back, .page-header .row, .actions-card, .disclaimer { display: none !important; } .doc { border: 0; box-shadow: none; padding: 0; } }
  `]
})
export class SummaryViewComponent {
  readonly id = input.required<string>();
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  protected readonly summary = signal<Summary | null>(null);
  protected readonly editing = signal(false);
  protected readonly busy = signal(false);
  protected readonly styles = SUMMARY_STYLES;
  protected title = '';
  protected content = '';
  protected regenStyle: SummaryStyle = 'quick';
  protected regenLanguage = this.i18n.lang() as string;

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => void this.api.get<Summary>(`/api/summaries/${id}`).then(s => {
        this.summary.set(s);
        this.regenStyle = s.style;
        this.regenLanguage = s.language;
      }).catch(e => { this.toast.error(e); void this.router.navigateByUrl('/summaries'); }));
    });
  }

  protected startEdit(): void {
    const s = this.summary();
    if (!s) return;
    this.title = s.title;
    this.content = s.content;
    this.editing.set(true);
  }

  protected async save(): Promise<void> {
    const s = this.summary();
    if (!s) return;
    try {
      this.summary.set(await this.api.patch<Summary>(`/api/summaries/${s.id}`, { title: this.title, content: this.content }));
      this.editing.set(false);
      this.toast.success('common.saved');
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected async export(): Promise<void> {
    const s = this.summary();
    if (!s) return;
    saveBlob(await this.api.blob(`/api/summaries/${s.id}/export`), `${s.title}.md`);
  }

  protected print(): void {
    window.print();
  }

  protected async remove(): Promise<void> {
    const s = this.summary();
    if (!s) return;
    if (!(await this.confirm.ask({ title: this.i18n.t('summaries.deleteTitle'), message: this.i18n.t('summaries.deleteMessage'), confirm: this.i18n.t('common.delete'), danger: true }))) return;
    await this.api.delete(`/api/summaries/${s.id}`);
    void this.router.navigateByUrl('/summaries');
  }

  protected async regenerate(): Promise<void> {
    const s = this.summary();
    if (!s) return;
    this.busy.set(true);
    try {
      const fresh = await this.api.post<Summary>('/api/summaries', {
        bookId: s.bookId, scope: s.scope === 'selection' ? 'page' : s.scope, style: this.regenStyle, language: this.regenLanguage,
        chapterId: s.chapterId, page: s.pageStart, pageStart: s.pageStart, pageEnd: s.pageEnd
      });
      void this.router.navigate(['/summaries', fresh.id]);
    } catch (e) {
      this.toast.error(e);
    } finally {
      this.busy.set(false);
    }
  }

  protected async makeCards(): Promise<void> {
    const s = this.summary();
    if (!s) return;
    this.busy.set(true);
    try {
      const result = await this.api.post<GeneratedFlashcards>('/api/flashcards/generate', { bookId: s.bookId, scope: 'selection', summaryId: s.id, count: 12 });
      if (result.drafts.length === 0) { this.toast.show(this.i18n.t('tools.noDrafts')); return; }
      await this.api.post('/api/flashcards/batch', {
        cards: result.drafts.map(d => ({ bookId: s.bookId, chapterId: s.chapterId, front: d.front, back: d.back, tags: d.tags, sourceType: 'summary', sourcePage: d.page }))
      });
      this.toast.success('tools.cardsSaved', { n: result.drafts.length });
    } catch (e) {
      this.toast.error(e);
    } finally {
      this.busy.set(false);
    }
  }

  protected open(click: CitationClick): void {
    const s = this.summary();
    if (s) void this.router.navigate(['/read', s.bookId], { queryParams: { page: click.page } });
  }

  protected warningText(w: string): string {
    const [code, param] = w.split('|');
    return this.i18n.t('summaryWarnings.' + code, { pages: param, page: param });
  }
}
