import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { Book } from '../../core/models';
import { StoreService } from '../../core/store.service';
import { EmptyStateComponent } from '../../shared/ui';
import { TutorPanelComponent } from './tutor-panel.component';

/** Standalone tutor: chat about one book, or about every book of a subject (explicit cross-book scope). */
@Component({
  selector: 'rw-tutor-page',
  imports: [FormsModule, RouterLink, TranslatePipe, TutorPanelComponent, EmptyStateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page tutor-page">
      <header class="page-header">
        <div>
          <div class="eyebrow">{{ 'nav.tutor' | t }}</div>
          <h1>{{ 'tutor.pageTitle' | t }}</h1>
          <p>{{ 'tutor.pageSubtitle' | t }}</p>
        </div>
      </header>

      @if (books().length === 0 && loaded()) {
        <div class="card"><rw-empty icon="message-circle" [title]="'tutor.noBooksTitle' | t" [text]="'tutor.noBooksText' | t">
          <a class="btn primary" routerLink="/library">{{ 'nav.library' | t }}</a></rw-empty></div>
      } @else {
        <div class="layout">
          <aside class="card scope">
            <h3 class="small">{{ 'tutor.scope' | t }}</h3>
            <div class="field">
              <label for="subject">{{ 'common.subject' | t }}</label>
              <select id="subject" class="select" [ngModel]="subjectId()" (ngModelChange)="pickSubject($event)">
                @for (s of subjectsWithBooks(); track s.id) { <option [ngValue]="s.id">{{ i18n.subjectName(s) }}</option> }
              </select>
            </div>
            <div class="options">
              <label class="option" [class.active]="bookId() === null">
                <input type="radio" name="scope" [checked]="bookId() === null" (change)="bookId.set(null)" />
                <span><strong>{{ 'tutor.allBooks' | t }}</strong><br><span class="xsmall subtle">{{ 'tutor.allBooksHint' | t: { n: booksInSubject().length } }}</span></span>
              </label>
              @for (b of booksInSubject(); track b.id) {
                <label class="option" [class.active]="bookId() === b.id">
                  <input type="radio" name="scope" [checked]="bookId() === b.id" (change)="bookId.set(b.id)" />
                  <span dir="auto">{{ b.title }}</span>
                </label>
              }
            </div>
            <p class="xsmall subtle">{{ 'tutor.scopeNote' | t }}</p>
          </aside>
          <section class="card chat">
            @if (bookId(); as id) {
              <rw-tutor-panel [bookId]="id" />
            } @else if (subjectId(); as sid) {
              <rw-tutor-panel [subjectId]="sid" />
            }
          </section>
        </div>
      }
    </div>
  `,
  styles: [`
    .layout { display: grid; grid-template-columns: 280px minmax(0, 1fr); gap: 16px; height: calc(100vh - 230px); min-height: 520px; }
    .scope { display: flex; flex-direction: column; gap: 12px; overflow-y: auto; }
    .options { display: flex; flex-direction: column; gap: 6px; }
    .option { display: flex; gap: 10px; align-items: flex-start; padding: 10px; border-radius: var(--radius-sm); border: 1px solid var(--line); cursor: pointer; font-size: var(--text-sm); }
    .option.active { border-color: var(--brand); background: var(--brand-soft); }
    .option input { margin-top: 3px; accent-color: var(--brand); }
    .chat { padding: 0; overflow: hidden; display: flex; flex-direction: column; }
    .chat > * { flex: 1; min-height: 0; }
    @media (max-width: 900px) { .layout { grid-template-columns: 1fr; height: auto; } .chat { height: 70vh; } }
  `]
})
export class TutorPageComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly store = inject(StoreService);
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);

  protected readonly books = signal<Book[]>([]);
  protected readonly loaded = signal(false);
  protected readonly subjectId = signal<number | null>(null);
  protected readonly bookId = signal<string | null>(null);

  protected readonly subjectsWithBooks = computed(() => this.store.subjects().filter(s => this.books().some(b => b.subjectId === s.id)));
  protected readonly booksInSubject = computed(() => this.books().filter(b => b.subjectId === this.subjectId()));

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    const books = (await this.api.get<Book[]>('/api/books')).filter(b => b.status === 'ready' || b.status === 'readyWithWarnings');
    this.books.set(books);
    this.loaded.set(true);
    const requestedBook = this.route.snapshot.queryParamMap.get('bookId');
    const book = books.find(b => b.id === requestedBook) ?? books[0];
    if (book) {
      this.subjectId.set(book.subjectId);
      this.bookId.set(requestedBook ? book.id : null);
    }
  }

  protected pickSubject(id: number): void {
    this.subjectId.set(id);
    this.bookId.set(null);
  }
}
