import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { FlashcardDraft, GeneratedFlashcards, PageTextDto, Summary, SummaryScope, SummaryStyle } from '../../core/models';
import { ToastService } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { MarkdownComponent } from '../../shared/markdown.component';
import { ReaderStore } from './reader-store';

export const SUMMARY_STYLES: SummaryStyle[] = ['quick', 'simple', 'detailed', 'keyPoints', 'definitions', 'formulas', 'comparisons', 'causesEffects', 'examQuestions', 'revisionSheet'];

@Component({
  selector: 'rw-study-tools-panel',
  imports: [FormsModule, RouterLink, TranslatePipe, IconComponent, MarkdownComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './study-tools-panel.component.html',
  styleUrl: './study-tools-panel.component.scss'
})
export class StudyToolsPanelComponent {
  protected readonly store = inject(ReaderStore);
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  protected readonly styles = SUMMARY_STYLES;
  protected readonly section = signal<'summary' | 'cards' | 'quiz' | 'text'>('summary');

  // Summary
  protected scope: SummaryScope = 'chapter';
  protected style: SummaryStyle = 'keyPoints';
  protected readonly summarizing = signal(false);
  protected readonly summary = signal<Summary | null>(null);

  // Flashcards
  protected front = '';
  protected back = '';
  protected cardScope: 'page' | 'chapter' | 'selection' = 'page';
  protected focus = 'general';
  protected readonly generating = signal(false);
  protected readonly drafts = signal<(FlashcardDraft & { keep: boolean })[]>([]);
  protected readonly draftWarnings = signal<string[]>([]);

  // Page text
  protected readonly pageText = signal<PageTextDto | null>(null);

  protected readonly chapter = computed(() => this.store.currentChapter());

  constructor() {
    // A selection made in the reader pre-fills the manual flashcard.
    effect(() => {
      const sel = this.store.selection();
      if (sel) untracked(() => {
        if (!this.front) this.front = sel.text.slice(0, 300);
        this.cardScope = 'selection';
        this.scope = 'selection';
      });
    });
    effect(() => {
      if (this.section() === 'text') {
        const page = this.store.currentPage();
        untracked(() => void this.loadPageText(page));
      }
    });
  }

  protected async generateSummary(): Promise<void> {
    const book = this.store.book();
    if (!book) return;
    const sel = this.store.selection();
    const body: Record<string, unknown> = { bookId: book.id, scope: this.scope, style: this.style };
    if (this.scope === 'page') body['page'] = this.store.currentPage();
    if (this.scope === 'chapter') {
      if (!this.chapter()) { this.toast.show(this.i18n.t('tools.noChapter'), 'error'); return; }
      body['chapterId'] = this.chapter()!.id;
    }
    if (this.scope === 'selection') {
      if (!sel) { this.toast.show(this.i18n.t('tools.selectFirst'), 'error'); return; }
      body['selectedText'] = sel.text;
      body['page'] = sel.page;
    }
    this.summarizing.set(true);
    this.summary.set(null);
    try {
      this.summary.set(await this.api.post<Summary>('/api/summaries', body));
    } catch (e) {
      this.toast.error(e);
    } finally {
      this.summarizing.set(false);
    }
  }

  protected async saveManualCard(): Promise<void> {
    const book = this.store.book();
    if (!book || !this.front.trim() || !this.back.trim()) return;
    try {
      await this.api.post('/api/flashcards', {
        bookId: book.id, chapterId: this.chapter()?.id ?? null, front: this.front.trim(), back: this.back.trim(),
        sourceType: this.store.selection() ? 'selection' : 'manual', sourcePage: this.store.selection()?.page ?? this.store.currentPage(), tags: []
      });
      this.front = '';
      this.back = '';
      this.toast.success('tools.cardSaved');
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected async generateCards(): Promise<void> {
    const book = this.store.book();
    if (!book) return;
    const sel = this.store.selection();
    const body: Record<string, unknown> = { bookId: book.id, count: 10, focus: this.focus };
    if (this.cardScope === 'selection' && sel) Object.assign(body, { scope: 'selection', selectedText: sel.text, page: sel.page });
    else if (this.cardScope === 'chapter' && this.chapter()) Object.assign(body, { scope: 'chapter', chapterId: this.chapter()!.id });
    else Object.assign(body, { scope: 'page', page: this.store.currentPage() });
    this.generating.set(true);
    try {
      const result = await this.api.post<GeneratedFlashcards>('/api/flashcards/generate', body);
      this.drafts.set(result.drafts.map(d => ({ ...d, keep: true })));
      this.draftWarnings.set(result.warnings);
      if (result.drafts.length === 0) this.toast.show(this.i18n.t('tools.noDrafts'));
    } catch (e) {
      this.toast.error(e);
    } finally {
      this.generating.set(false);
    }
  }

  protected toggleDraft(index: number): void {
    this.drafts.update(list => list.map((d, i) => (i === index ? { ...d, keep: !d.keep } : d)));
  }

  protected async saveDrafts(): Promise<void> {
    const book = this.store.book();
    const keep = this.drafts().filter(d => d.keep);
    if (!book || keep.length === 0) return;
    try {
      await this.api.post('/api/flashcards/batch', {
        cards: keep.map(d => ({ bookId: book.id, chapterId: this.chapter()?.id ?? null, front: d.front, back: d.back, tags: d.tags, sourceType: 'aiGenerated', sourcePage: d.page }))
      });
      this.drafts.set([]);
      this.toast.success('tools.cardsSaved', { n: keep.length });
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected startQuiz(mode: 'chapter' | 'page'): void {
    const book = this.store.book();
    if (!book) return;
    const params: Record<string, unknown> = { bookId: book.id };
    if (mode === 'chapter' && this.chapter()) params['chapterId'] = this.chapter()!.id;
    else { params['pageStart'] = Math.max(1, this.store.currentPage() - 2); params['pageEnd'] = this.store.currentPage() + 2; }
    void this.router.navigate(['/quizzes/new'], { queryParams: params });
  }

  private async loadPageText(page: number): Promise<void> {
    const book = this.store.book();
    if (!book) return;
    try {
      this.pageText.set(await this.api.get<PageTextDto>(`/api/books/${book.id}/pages/${page}/text`));
    } catch {
      this.pageText.set(null);
    }
  }

  protected warningText(w: string): string {
    const [code, param] = w.split('|');
    return this.i18n.t('summaryWarnings.' + code, { pages: param, page: param });
  }
}
