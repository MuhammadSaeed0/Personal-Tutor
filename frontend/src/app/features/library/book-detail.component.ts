import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { Book, BookDetail, Chapter, ChapterStatus, ProcessingStatus } from '../../core/models';
import { formatClock } from '../../core/source-times.service';
import { shrinkPhoto } from './upload-dialog.component';
import { RealtimeService } from '../../core/realtime.service';
import { StoreService } from '../../core/store.service';
import { ToastService } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { ConfirmService } from '../../shared/ui';
import { ProcessingTimelineComponent } from './processing-timeline.component';

@Component({
  selector: 'rw-book-detail',
  imports: [FormsModule, RouterLink, TranslatePipe, IconComponent, ProcessingTimelineComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './book-detail.component.html',
  styleUrl: './book-detail.component.scss'
})
export class BookDetailComponent {
  readonly bookId = input.required<string>();
  protected readonly i18n = inject(I18nService);
  protected readonly store = inject(StoreService);
  private readonly api = inject(ApiService);
  private readonly realtime = inject(RealtimeService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  protected readonly detail = signal<BookDetail | null>(null);
  protected readonly editing = signal(false);
  protected readonly editingChapter = signal<string | null>(null);
  protected chapterTitle = '';
  protected form = { title: '', subjectId: 0, academicYear: '', semester: '', collection: '', tags: '' };

  protected readonly status = computed<ProcessingStatus | null>(() => {
    const d = this.detail();
    if (!d) return null;
    return this.realtime.processing()[d.book.id] ?? d.book.processing;
  });
  protected readonly processing = computed(() => ['uploaded', 'processing'].includes(this.status()?.bookStatus ?? this.detail()?.book.status ?? ''));
  protected readonly ready = computed(() => ['ready', 'readyWithWarnings'].includes(this.status()?.bookStatus ?? this.detail()?.book.status ?? ''));
  protected readonly scannedPages = computed(() => this.detail()?.pages.filter(p => p.isScanned).length ?? 0);
  protected readonly ocrPages = computed(() => this.detail()?.pages.filter(p => p.extractionMethod === 'ocr').length ?? 0);

  constructor() {
    effect(() => void this.load(this.bookId()));
    effect(() => {
      const s = this.status();
      const d = this.detail();
      if (s && d && s.bookStatus !== d.book.status && ['ready', 'readyWithWarnings', 'failed'].includes(s.bookStatus)) void this.load(d.book.id);
    });
  }

  protected async load(id: string): Promise<void> {
    try {
      const detail = await this.api.get<BookDetail>(`/api/books/${id}`);
      this.detail.set(detail);
      const b = detail.book;
      this.form = { title: b.title, subjectId: b.subjectId, academicYear: b.academicYear ?? '', semester: b.semester ?? '', collection: b.collection ?? '', tags: b.tags.join(', ') };
    } catch (e) {
      this.toast.error(e);
      void this.router.navigateByUrl('/library');
    }
  }

  protected async save(): Promise<void> {
    const d = this.detail();
    if (!d) return;
    try {
      const book = await this.api.patch<Book>(`/api/books/${d.book.id}`, {
        title: this.form.title, subjectId: this.form.subjectId, academicYear: this.form.academicYear, semester: this.form.semester,
        collection: this.form.collection, tags: this.form.tags.split(',').map(t => t.trim()).filter(Boolean)
      });
      this.detail.set({ ...d, book: { ...d.book, ...book, processing: d.book.processing } });
      this.editing.set(false);
      this.toast.success('common.saved');
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected startChapterEdit(chapter: Chapter): void {
    this.editingChapter.set(chapter.id);
    this.chapterTitle = chapter.title;
  }

  protected async saveChapter(chapter: Chapter): Promise<void> {
    const d = this.detail();
    if (!d || !this.chapterTitle.trim()) return;
    try {
      const updated = await this.api.patch<Chapter>(`/api/books/${d.book.id}/chapters/${chapter.id}`, { title: this.chapterTitle.trim() });
      this.detail.set({ ...d, chapters: d.chapters.map(c => (c.id === chapter.id ? { ...c, title: updated.title, source: updated.source } : c)) });
      this.editingChapter.set(null);
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected async setStatus(chapter: Chapter, status: ChapterStatus): Promise<void> {
    const d = this.detail();
    if (!d) return;
    try {
      await this.api.put(`/api/books/${d.book.id}/chapters/${chapter.id}/status`, { status });
      this.detail.set({ ...d, chapters: d.chapters.map(c => (c.id === chapter.id ? { ...c, status } : c)) });
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected readonly addingPhotos = signal(false);

  protected clock(seconds: number): string {
    return formatClock(seconds);
  }

  /** "p. 12" for documents, "photo 3" for photos, "▶ 4:10" for videos. */
  protected pageLabel(page: number): string {
    const d = this.detail();
    if (d?.book.kind === 'youTube') {
      const start = d.pages.find(p => p.pageNumber === page)?.startSeconds;
      if (start !== null && start !== undefined) return `▶ ${formatClock(start)}`;
    }
    return this.i18n.t(d?.book.kind === 'images' ? 'book.photoN' : 'common.pageN', { n: page });
  }

  protected rangeLabel(chapter: Chapter): string {
    const d = this.detail();
    if (d?.book.kind === 'youTube') {
      const from = d.pages.find(p => p.pageNumber === chapter.startPage)?.startSeconds ?? 0;
      const to = d.pages.find(p => p.pageNumber === chapter.endPage)?.endSeconds ?? from;
      return `${formatClock(from)} – ${formatClock(to)}`;
    }
    return this.i18n.t(d?.book.kind === 'images' ? 'book.photoRange' : 'book.pages', { from: chapter.startPage, to: chapter.endPage });
  }

  protected async addPhotos(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []).sort((a, b) => a.name.localeCompare(b.name, undefined, { numeric: true }));
    input.value = '';
    const d = this.detail();
    if (!d || files.length === 0) return;
    this.addingPhotos.set(true);
    try {
      const form = new FormData();
      const blobs = await Promise.all(files.map(f => shrinkPhoto(f)));
      blobs.forEach((blob, i) => form.append('files', blob, files[i].name));
      await this.api.post<Book>(`/api/books/${d.book.id}/images`, form);
      await this.load(d.book.id);
    } catch (e) {
      this.toast.error(e);
    } finally {
      this.addingPhotos.set(false);
    }
  }

  protected async reprocess(): Promise<void> {
    const d = this.detail();
    if (!d) return;
    const ok = await this.confirm.ask({ title: this.i18n.t('book.reprocessTitle'), message: this.i18n.t('book.reprocessMessage'), confirm: this.i18n.t('book.reprocess') });
    if (!ok) return;
    try {
      const status = await this.api.post<ProcessingStatus>(`/api/books/${d.book.id}/reprocess`);
      this.detail.set({ ...d, book: { ...d.book, status: status.bookStatus, processing: status } });
      this.realtime.processing.update(m => ({ ...m, [d.book.id]: status }));
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected async remove(): Promise<void> {
    const d = this.detail();
    if (!d) return;
    const ok = await this.confirm.ask({
      title: this.i18n.t('library.deleteTitle'), message: this.i18n.t('library.deleteMessage', { title: d.book.title }), confirm: this.i18n.t('common.delete'), danger: true
    });
    if (!ok) return;
    try {
      await this.api.delete(`/api/books/${d.book.id}`);
      this.toast.success('library.deleted');
      void this.router.navigateByUrl('/library');
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected warningText(code: string, message: string): string {
    const key = `warnings.${code}`;
    return this.i18n.has(key) ? this.i18n.t(key) : message;
  }
}
