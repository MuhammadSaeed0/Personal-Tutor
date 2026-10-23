import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { Book, Summary } from '../../core/models';
import { StoreService } from '../../core/store.service';
import { EmptyStateComponent } from '../../shared/ui';

@Component({
  selector: 'rw-summaries',
  imports: [RouterLink, TranslatePipe, EmptyStateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <header class="page-header">
        <div><div class="eyebrow">{{ 'nav.summaries' | t }}</div><h1>{{ 'summaries.title' | t }}</h1><p>{{ 'summaries.subtitle' | t }}</p></div>
      </header>
      @if (summaries().length > 0) {
        <div class="chips">
          <button type="button" class="chip" [class.active]="bookFilter() === null" (click)="bookFilter.set(null)">{{ 'common.all' | t }}</button>
          @for (b of booksWithSummaries(); track b.id) {
            <button type="button" class="chip" [class.active]="bookFilter() === b.id" (click)="bookFilter.set(b.id)" dir="auto">{{ b.title }}</button>
          }
        </div>
      }
      @if (loaded() && summaries().length === 0) {
        <div class="card"><rw-empty icon="file-text" [title]="'summaries.emptyTitle' | t" [text]="'summaries.emptyText' | t">
          <a class="btn primary" routerLink="/library">{{ 'nav.library' | t }}</a></rw-empty></div>
      }
      <div class="grid-cards">
        @for (s of filtered(); track s.id) {
          <a class="card interactive summary" [routerLink]="['/summaries', s.id]">
            <div class="row between">
              <span class="badge brand">{{ 'summaryStyles.' + s.style | t }}</span>
              @if (s.isEdited) { <span class="badge">{{ 'summaries.edited' | t }}</span> }
            </div>
            <h3 dir="auto">{{ s.title }}</h3>
            <p class="excerpt small muted" dir="auto">{{ excerpt(s.content) }}</p>
            <span class="xsmall subtle">{{ bookTitle(s.bookId) }} · {{ i18n.relative(s.updatedAt) }}</span>
          </a>
        }
      </div>
    </div>
  `,
  styles: [`
    .chips { display: flex; gap: 6px; flex-wrap: wrap; margin-bottom: 16px; }
    .grid-cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 14px; }
    .summary { display: flex; flex-direction: column; gap: 8px; color: var(--ink); text-decoration: none !important; }
    .summary h3 { font-size: var(--text-md); }
    .excerpt { margin: 0; display: -webkit-box; -webkit-line-clamp: 3; -webkit-box-orient: vertical; overflow: hidden; }
  `]
})
export class SummariesComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly store = inject(StoreService);
  private readonly api = inject(ApiService);
  protected readonly summaries = signal<Summary[]>([]);
  protected readonly books = signal<Book[]>([]);
  protected readonly loaded = signal(false);
  protected readonly bookFilter = signal<string | null>(null);
  protected readonly filtered = computed(() => this.summaries().filter(s => !this.bookFilter() || s.bookId === this.bookFilter()));
  protected readonly booksWithSummaries = computed(() => this.books().filter(b => this.summaries().some(s => s.bookId === b.id)));

  constructor() {
    void Promise.all([this.api.get<Summary[]>('/api/summaries'), this.api.get<Book[]>('/api/books')]).then(([s, b]) => {
      this.summaries.set(s);
      this.books.set(b);
      this.loaded.set(true);
    });
  }

  protected bookTitle(id: string): string {
    return this.books().find(b => b.id === id)?.title ?? '';
  }

  protected excerpt(content: string): string {
    return content.replace(/\[p:\d+(-\d+)?\]/g, '').replace(/[#*>`$\\_|-]/g, ' ').replace(/\s+/g, ' ').trim().slice(0, 220);
  }
}
