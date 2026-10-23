import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { Book, SearchHit } from '../../core/models';
import { StoreService } from '../../core/store.service';
import { ToastService } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { EmptyStateComponent } from '../../shared/ui';

const HISTORY_KEY = 'riwaq.searchHistory';

@Component({
  selector: 'rw-search',
  imports: [FormsModule, RouterLink, TranslatePipe, IconComponent, EmptyStateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page narrow">
      <header class="page-header"><div><div class="eyebrow">{{ 'common.search' | t }}</div><h1>{{ 'search.title' | t }}</h1><p>{{ 'search.subtitle' | t }}</p></div></header>

      <form class="card search-box" (ngSubmit)="run()">
        <div class="row">
          <rw-icon name="search" />
          <input class="input big" [(ngModel)]="query" name="q" [placeholder]="'search.placeholder' | t" dir="auto" autofocus [attr.aria-label]="'search.placeholder' | t" />
          <button class="btn primary" type="submit" [disabled]="query.trim().length < 2">{{ 'common.search' | t }}</button>
        </div>
        <div class="scope">
          <span class="xsmall subtle">{{ 'search.scope' | t }}</span>
          <select class="select" [(ngModel)]="subjectId" name="subject" [attr.aria-label]="'common.subject' | t">
            <option [ngValue]="null">{{ 'search.allBooks' | t }}</option>
            @for (s of store.subjects(); track s.id) { <option [ngValue]="s.id">{{ i18n.subjectName(s) }}</option> }
          </select>
          <span class="chips">
            @for (b of scopedBooks(); track b.id) {
              <button type="button" class="chip" [class.active]="selectedBooks().includes(b.id)" (click)="toggleBook(b.id)" dir="auto">{{ b.title }}</button>
            }
          </span>
        </div>
      </form>

      @if (!searched() && history().length > 0) {
        <div class="history">
          <span class="xsmall subtle">{{ 'search.recent' | t }}</span>
          @for (h of history(); track h) { <button type="button" class="chip" (click)="query = h; run()">{{ h }}</button> }
          <button type="button" class="btn ghost sm" (click)="clearHistory()">{{ 'search.clear' | t }}</button>
        </div>
      }

      @if (loading()) { <div class="skeleton" style="height: 200px"></div> }
      @else if (searched()) {
        <p class="small muted">{{ 'search.resultsIn' | t: { n: hits().length, books: searchedCount() } }}</p>
        @for (hit of hits(); track hit.chunkId) {
          <a class="card hit" [routerLink]="['/read', hit.bookId]" [queryParams]="{ page: hit.pageStart }">
            <div class="row between">
              <span class="small"><strong dir="auto">{{ hit.bookTitle }}</strong>@if (hit.chapterTitle) { <span class="subtle" dir="auto"> · {{ hit.chapterTitle }}</span> }</span>
              <span class="badge brand num">{{ 'common.pageN' | t: { n: hit.pageStart } }}</span>
            </div>
            <p class="snippet" dir="auto">{{ hit.snippet }}</p>
            <span class="xsmall subtle">{{ 'search.match.' + hit.matchType | t }}</span>
          </a>
        } @empty {
          <div class="card"><rw-empty icon="search" [title]="'search.noResults' | t" [text]="'search.noResultsText' | t" /></div>
        }
      }
    </div>
  `,
  styles: [`
    .narrow { max-width: 880px; }
    .search-box { display: flex; flex-direction: column; gap: 12px; margin-bottom: 16px; }
    .big { font-size: 1.05rem; min-height: 46px; }
    .scope { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .scope .select { width: auto; min-height: 32px; font-size: var(--text-xs); }
    .chips { display: inline-flex; gap: 4px; flex-wrap: wrap; }
    .history { display: flex; gap: 6px; align-items: center; flex-wrap: wrap; margin-bottom: 16px; }
    .hit { display: flex; flex-direction: column; gap: 6px; margin-bottom: 10px; color: var(--ink); text-decoration: none !important; }
    .hit:hover { border-color: var(--brand); }
    .snippet { margin: 0; font-family: var(--font-read); line-height: 1.85; color: var(--ink-2); }
  `]
})
export class SearchComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly store = inject(StoreService);
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  protected query = '';
  protected subjectId: number | null = null;
  protected readonly books = signal<Book[]>([]);
  protected readonly selectedBooks = signal<string[]>([]);
  protected readonly hits = signal<SearchHit[]>([]);
  protected readonly searchedCount = signal(0);
  protected readonly searched = signal(false);
  protected readonly loading = signal(false);
  protected readonly history = signal<string[]>(readHistory());
  protected readonly scopedBooks = computed(() => this.books().filter(b => b.status === 'ready' || b.status === 'readyWithWarnings'));

  constructor() {
    void this.api.get<Book[]>('/api/books').then(b => this.books.set(b));
  }

  protected toggleBook(id: string): void {
    this.selectedBooks.update(list => (list.includes(id) ? list.filter(x => x !== id) : [...list, id]));
  }

  protected async run(): Promise<void> {
    const q = this.query.trim();
    if (q.length < 2) return;
    this.loading.set(true);
    try {
      const result = await this.api.post<{ hits: SearchHit[]; searchedBookIds: string[] }>('/api/search', {
        query: q, bookIds: this.selectedBooks().length ? this.selectedBooks() : null, subjectId: this.subjectId, limit: 30
      });
      this.hits.set(result.hits);
      this.searchedCount.set(result.searchedBookIds.length);
      this.searched.set(true);
      const next = [q, ...this.history().filter(h => h !== q)].slice(0, 8);
      this.history.set(next);
      try { localStorage.setItem(HISTORY_KEY, JSON.stringify(next)); } catch { /* storage unavailable */ }
    } catch (e) {
      this.toast.error(e);
    } finally {
      this.loading.set(false);
    }
  }

  protected clearHistory(): void {
    this.history.set([]);
    try { localStorage.removeItem(HISTORY_KEY); } catch { /* storage unavailable */ }
  }
}

function readHistory(): string[] {
  try {
    const raw = localStorage.getItem(HISTORY_KEY);
    return raw ? (JSON.parse(raw) as string[]).slice(0, 8) : [];
  } catch {
    return [];
  }
}
