import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { Flashcard, FlashcardStats, Paged } from '../../core/models';
import { StoreService } from '../../core/store.service';
import { ToastService } from '../../core/toast.service';
import { ChartComponent, SimpleSeries } from '../../shared/chart.component';
import { IconComponent } from '../../shared/icon.component';
import { MarkdownComponent } from '../../shared/markdown.component';
import { ConfirmService, EmptyStateComponent } from '../../shared/ui';

@Component({
  selector: 'rw-flashcards',
  imports: [FormsModule, RouterLink, TranslatePipe, IconComponent, MarkdownComponent, ChartComponent, EmptyStateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './flashcards.component.html',
  styleUrl: './flashcards.component.scss'
})
export class FlashcardsComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly store = inject(StoreService);
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  protected readonly stats = signal<FlashcardStats | null>(null);
  protected readonly cards = signal<Paged<Flashcard> | null>(null);
  protected readonly subjectId = signal<number | null>(null);
  protected readonly state = signal<string>('');
  protected readonly search = signal('');
  protected readonly page = signal(1);
  protected readonly creating = signal(false);
  protected readonly editingId = signal<string | null>(null);
  protected draft = { subjectId: null as number | null, front: '', back: '', tags: '' };

  protected readonly forecast = computed<SimpleSeries>(() => ({
    labels: (this.stats()?.forecast ?? []).map(f => this.i18n.date(f.date, 'short')),
    values: (this.stats()?.forecast ?? []).map(f => f.count)
  }));

  constructor() {
    effect(() => {
      const subjectId = this.subjectId();
      const state = this.state();
      const search = this.search();
      const page = this.page();
      untracked(() => void this.load(subjectId, state, search, page));
    });
  }

  private async load(subjectId: number | null, state: string, search: string, page: number): Promise<void> {
    try {
      const [stats, cards] = await Promise.all([
        this.api.get<FlashcardStats>('/api/flashcards/stats', { subjectId }),
        this.api.get<Paged<Flashcard>>('/api/flashcards', { subjectId, state, search, page, pageSize: 24 })
      ]);
      this.stats.set(stats);
      this.cards.set(cards);
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected reload(): void {
    void this.load(this.subjectId(), this.state(), this.search(), this.page());
  }

  protected startCreate(): void {
    this.draft = { subjectId: this.subjectId() ?? this.store.subjects()[0]?.id ?? null, front: '', back: '', tags: '' };
    this.editingId.set(null);
    this.creating.set(true);
  }

  protected startEdit(card: Flashcard): void {
    this.draft = { subjectId: card.subjectId, front: card.front, back: card.back, tags: card.tags.join(', ') };
    this.editingId.set(card.id);
    this.creating.set(true);
  }

  protected async saveDraft(): Promise<void> {
    const tags = this.draft.tags.split(',').map(t => t.trim()).filter(Boolean);
    try {
      if (this.editingId()) await this.api.put(`/api/flashcards/${this.editingId()}`, { front: this.draft.front, back: this.draft.back, tags });
      else await this.api.post('/api/flashcards', { subjectId: this.draft.subjectId, front: this.draft.front, back: this.draft.back, tags, sourceType: 'manual' });
      this.creating.set(false);
      this.toast.success('common.saved');
      this.reload();
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected async toggleSuspend(card: Flashcard): Promise<void> {
    await this.api.put(`/api/flashcards/${card.id}`, { isSuspended: !card.isSuspended });
    this.reload();
  }

  protected async remove(card: Flashcard): Promise<void> {
    if (!(await this.confirm.ask({ title: this.i18n.t('flashcards.deleteTitle'), message: this.i18n.t('flashcards.deleteMessage'), confirm: this.i18n.t('common.delete'), danger: true }))) return;
    await this.api.delete(`/api/flashcards/${card.id}`);
    this.reload();
  }

  protected dueLabel(card: Flashcard): string {
    if (card.state === 'new') return this.i18n.t('flashcards.states.new');
    return new Date(card.due) <= new Date() ? this.i18n.t('flashcards.dueNow') : this.i18n.relative(card.due);
  }
}
