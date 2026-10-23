import { ChangeDetectionStrategy, Component, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { Note, Paged } from '../../core/models';
import { ToastService } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { MarkdownComponent } from '../../shared/markdown.component';
import { ReaderStore } from './reader-store';

@Component({
  selector: 'rw-book-notes-panel',
  imports: [FormsModule, RouterLink, TranslatePipe, IconComponent, MarkdownComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="panel">
      <div class="compose">
        @if (store.selection(); as sel) {
          <blockquote dir="auto">{{ sel.text.length > 200 ? sel.text.slice(0, 200) + '…' : sel.text }}</blockquote>
        }
        <input class="input" [(ngModel)]="title" [placeholder]="'notes.titlePlaceholder' | t" dir="auto" [attr.aria-label]="'notes.title' | t" />
        <textarea class="textarea" [(ngModel)]="content" [placeholder]="'notes.contentPlaceholder' | t" dir="auto" [attr.aria-label]="'notes.content' | t"></textarea>
        <div class="row between">
          <span class="xsmall subtle">{{ 'notes.linkedToPage' | t: { page: store.selection()?.page ?? store.currentPage() } }}</span>
          <button class="btn primary sm" type="button" (click)="save()" [disabled]="busy() || (!content.trim() && !title.trim() && !store.selection())">{{ 'notes.add' | t }}</button>
        </div>
      </div>
      <div class="list">
        @for (n of notes(); track n.id) {
          <article class="note">
            <header class="row between">
              <button type="button" class="note-title" (click)="n.pageNumber && store.navigate(n.pageNumber)">
                @if (n.isPinned) { <rw-icon name="pin" [size]="13" /> }
                <strong dir="auto">{{ n.title }}</strong>
              </button>
              @if (n.pageNumber) { <span class="badge num">{{ 'common.pageN' | t: { n: n.pageNumber } }}</span> }
            </header>
            <rw-markdown [content]="n.content.length > 400 ? n.content.slice(0, 400) + '…' : n.content" [citations]="n.sources" (citationClick)="store.navigate($event.page)" />
          </article>
        } @empty {
          <p class="muted small">{{ 'notes.emptyBook' | t }}</p>
        }
        <a class="small" routerLink="/notes" [queryParams]="{ bookId: store.book()?.id }">{{ 'notes.openNotebook' | t }}</a>
      </div>
    </div>
  `,
  styles: [`
    :host { display: flex; flex-direction: column; min-height: 0; }
    .panel { display: flex; flex-direction: column; height: 100%; min-height: 0; }
    .compose { display: flex; flex-direction: column; gap: 8px; padding: 12px; border-bottom: 1px solid var(--line); }
    .compose .textarea { min-height: 80px; }
    blockquote { margin: 0; padding: 6px 10px; border-inline-start: 3px solid var(--gold); background: var(--gold-soft); font-size: var(--text-xs); border-radius: 4px; }
    .list { flex: 1; overflow-y: auto; padding: 12px; display: flex; flex-direction: column; gap: 10px; }
    .note { padding: 10px 12px; border: 1px solid var(--line); border-radius: var(--radius); background: var(--surface-2); }
    .note-title { display: flex; gap: 6px; align-items: center; border: 0; background: none; padding: 0; cursor: pointer; text-align: start; color: var(--ink); font-size: var(--text-sm); }
    .note :host ::ng-deep .prose { font-size: 0.9rem; }
  `]
})
export class BookNotesPanelComponent {
  protected readonly store = inject(ReaderStore);
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  protected readonly i18n = inject(I18nService);

  protected readonly notes = signal<Note[]>([]);
  protected readonly busy = signal(false);
  protected title = '';
  protected content = '';

  constructor() {
    effect(() => {
      const book = this.store.book();
      if (book) untracked(() => void this.load(book.id));
    });
  }

  private async load(bookId: string): Promise<void> {
    const page = await this.api.get<Paged<Note>>('/api/notes', { bookId, pageSize: 100 });
    this.notes.set(page.items);
  }

  protected async save(): Promise<void> {
    const book = this.store.book();
    if (!book) return;
    const sel = this.store.selection();
    const quote = sel ? `> ${sel.text.replace(/\n/g, ' ')}\n\n` : '';
    this.busy.set(true);
    try {
      const note = await this.api.post<Note>('/api/notes', {
        bookId: book.id, chapterId: this.store.currentChapter()?.id ?? null, pageNumber: sel?.page ?? this.store.currentPage(),
        title: this.title.trim() || null, content: quote + this.content, sourceType: sel ? 'selection' : 'manual', tags: []
      });
      this.notes.update(list => [note, ...list]);
      this.title = '';
      this.content = '';
      this.store.selection.set(null);
      this.toast.success('notes.saved');
    } catch (e) {
      this.toast.error(e);
    } finally {
      this.busy.set(false);
    }
  }
}
