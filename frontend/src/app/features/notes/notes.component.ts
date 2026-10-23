import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiService, saveBlob } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { Book, GeneratedFlashcards, Note, Paged } from '../../core/models';
import { StoreService } from '../../core/store.service';
import { ToastService } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { CitationClick, MarkdownComponent } from '../../shared/markdown.component';
import { ConfirmService, EmptyStateComponent } from '../../shared/ui';

interface Draft { id: string | null; title: string; content: string; tags: string; subjectId: number | null; bookId: string | null; pageNumber: number | null; isPinned: boolean; }

@Component({
  selector: 'rw-notes',
  imports: [FormsModule, RouterLink, TranslatePipe, IconComponent, MarkdownComponent, EmptyStateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './notes.component.html',
  styleUrl: './notes.component.scss'
})
export class NotesComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly store = inject(StoreService);
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  protected readonly notes = signal<Note[]>([]);
  protected readonly books = signal<Book[]>([]);
  protected readonly subjectId = signal<number | null>(null);
  protected readonly bookId = signal<string | null>(this.route.snapshot.queryParamMap.get('bookId'));
  protected readonly search = signal('');
  protected readonly tag = signal<string | null>(null);
  protected readonly selected = signal<Note | null>(null);
  protected readonly editing = signal(false);
  protected readonly preview = signal(false);
  protected readonly busy = signal(false);
  protected draft: Draft = this.emptyDraft();

  protected readonly tags = computed(() => [...new Set(this.notes().flatMap(n => n.tags))].sort());
  protected readonly bookTitle = (id: string | null) => this.books().find(b => b.id === id)?.title ?? '';

  constructor() {
    void this.api.get<Book[]>('/api/books').then(b => this.books.set(b));
    effect(() => {
      const params = { subjectId: this.subjectId(), bookId: this.bookId(), search: this.search(), tag: this.tag() };
      untracked(() => void this.load(params));
    });
  }

  private async load(params: { subjectId: number | null; bookId: string | null; search: string; tag: string | null }): Promise<void> {
    const page = await this.api.get<Paged<Note>>('/api/notes', { ...params, pageSize: 100 });
    this.notes.set(page.items);
    const current = this.selected();
    if (current) this.selected.set(page.items.find(n => n.id === current.id) ?? null);
  }

  private reload(): Promise<void> {
    return this.load({ subjectId: this.subjectId(), bookId: this.bookId(), search: this.search(), tag: this.tag() });
  }

  protected select(note: Note): void {
    this.selected.set(note);
    this.editing.set(false);
  }

  protected create(): void {
    this.draft = this.emptyDraft();
    this.draft.subjectId = this.subjectId();
    this.draft.bookId = this.bookId();
    this.selected.set(null);
    this.preview.set(false);
    this.editing.set(true);
  }

  protected edit(note: Note): void {
    this.draft = { id: note.id, title: note.title, content: note.content, tags: note.tags.join(', '), subjectId: note.subjectId, bookId: note.bookId, pageNumber: note.pageNumber, isPinned: note.isPinned };
    this.preview.set(false);
    this.editing.set(true);
  }

  protected async save(): Promise<void> {
    const body = {
      title: this.draft.title, content: this.draft.content, tags: this.draft.tags.split(',').map(t => t.trim()).filter(Boolean),
      subjectId: this.draft.subjectId, bookId: this.draft.bookId, pageNumber: this.draft.pageNumber, isPinned: this.draft.isPinned, sourceType: 'manual'
    };
    try {
      const note = this.draft.id ? await this.api.put<Note>(`/api/notes/${this.draft.id}`, body) : await this.api.post<Note>('/api/notes', body);
      this.editing.set(false);
      await this.reload();
      this.selected.set(note);
      this.toast.success('notes.saved');
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected async togglePin(note: Note): Promise<void> {
    await this.api.put<Note>(`/api/notes/${note.id}`, { ...note, isPinned: !note.isPinned });
    await this.reload();
  }

  protected async remove(note: Note): Promise<void> {
    if (!(await this.confirm.ask({ title: this.i18n.t('notes.deleteTitle'), message: this.i18n.t('notes.deleteMessage'), confirm: this.i18n.t('common.delete'), danger: true }))) return;
    await this.api.delete(`/api/notes/${note.id}`);
    this.selected.set(null);
    await this.reload();
  }

  protected async toFlashcard(note: Note): Promise<void> {
    try {
      if (note.bookId) {
        this.busy.set(true);
        const result = await this.api.post<GeneratedFlashcards>('/api/flashcards/generate', { bookId: note.bookId, scope: 'selection', selectedText: note.content.slice(0, 15000), page: note.pageNumber, count: 8 });
        if (result.drafts.length > 0) {
          await this.api.post('/api/flashcards/batch', { cards: result.drafts.map(d => ({ bookId: note.bookId, front: d.front, back: d.back, tags: d.tags, sourceType: 'note', sourcePage: d.page ?? note.pageNumber })) });
          this.toast.success('tools.cardsSaved', { n: result.drafts.length });
          return;
        }
      }
      await this.api.post('/api/flashcards', { subjectId: note.subjectId ?? this.store.subjects()[0]?.id, bookId: note.bookId, front: note.title, back: note.content.slice(0, 4000), sourceType: 'note', sourcePage: note.pageNumber, tags: note.tags });
      this.toast.success('tools.cardSaved');
    } catch (e) {
      this.toast.error(e);
    } finally {
      this.busy.set(false);
    }
  }

  protected toQuiz(note: Note): void {
    if (!note.bookId) return;
    const page = note.pageNumber ?? 1;
    void this.router.navigate(['/quizzes/new'], { queryParams: { bookId: note.bookId, pageStart: Math.max(1, page - 1), pageEnd: page + 1 } });
  }

  protected async export(): Promise<void> {
    saveBlob(await this.api.blob('/api/notes/export', { subjectId: this.subjectId(), bookId: this.bookId() }), 'riwaq-notes.md');
  }

  protected openSource(note: Note, click: CitationClick): void {
    const bookId = click.citation?.bookId ?? note.bookId;
    if (bookId) void this.router.navigate(['/read', bookId], { queryParams: { page: click.page } });
  }

  private emptyDraft(): Draft {
    return { id: null, title: '', content: '', tags: '', subjectId: null, bookId: null, pageNumber: null, isPinned: false };
  }
}
