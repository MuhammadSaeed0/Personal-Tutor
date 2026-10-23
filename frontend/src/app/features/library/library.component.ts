import { Dialog } from '@angular/cdk/dialog';
import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { Book, ProcessingStatus } from '../../core/models';
import { RealtimeService } from '../../core/realtime.service';
import { StoreService } from '../../core/store.service';
import { formatClock } from '../../core/source-times.service';
import { ToastService } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { ConfirmService, EmptyStateComponent } from '../../shared/ui';
import { UploadDialogComponent } from './upload-dialog.component';

@Component({
  selector: 'rw-library',
  imports: [FormsModule, RouterLink, TranslatePipe, IconComponent, EmptyStateComponent, CdkMenuTrigger, CdkMenu, CdkMenuItem],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './library.component.html',
  styleUrl: './library.component.scss'
})
export class LibraryComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly store = inject(StoreService);
  private readonly api = inject(ApiService);
  private readonly dialog = inject(Dialog);
  private readonly realtime = inject(RealtimeService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  protected readonly books = signal<Book[]>([]);
  protected readonly loading = signal(true);
  protected readonly subjectFilter = signal<number | null>(null);
  protected readonly yearFilter = signal<string | null>(null);
  protected readonly query = signal('');

  protected readonly years = computed(() => [...new Set(this.books().map(b => b.academicYear).filter((y): y is string => !!y))].sort());
  protected readonly filtered = computed(() => {
    const q = this.query().trim().toLowerCase();
    return this.books().filter(b =>
      (this.subjectFilter() === null || b.subjectId === this.subjectFilter()) &&
      (this.yearFilter() === null || b.academicYear === this.yearFilter()) &&
      (!q || b.title.toLowerCase().includes(q) || b.tags.some(t => t.toLowerCase().includes(q)) || (b.collection ?? '').toLowerCase().includes(q)));
  });

  constructor() {
    void this.load();
    // Refresh the list when a book finishes processing (pushed over SignalR).
    effect(() => {
      const updates = this.realtime.processing();
      const finished = Object.values(updates).some(s => ['ready', 'readyWithWarnings', 'failed'].includes(s.bookStatus)
        && this.books().some(b => b.id === s.bookId && b.status !== s.bookStatus));
      if (finished) void this.load(false);
    });
  }

  protected async load(showSpinner = true): Promise<void> {
    if (showSpinner) this.loading.set(true);
    try {
      this.books.set(await this.api.get<Book[]>('/api/books'));
    } catch (e) {
      this.toast.error(e);
    } finally {
      this.loading.set(false);
    }
  }

  protected status(book: Book): ProcessingStatus | null {
    return this.realtime.processing()[book.id] ?? book.processing;
  }

  protected isProcessing(book: Book): boolean {
    const s = this.status(book)?.bookStatus ?? book.status;
    return s === 'uploaded' || s === 'processing';
  }

  protected upload(): void {
    const ref = this.dialog.open(UploadDialogComponent, { autoFocus: 'first-tabbable', restoreFocus: true });
    ref.closed.subscribe(() => void this.load(false));
  }

  protected open(book: Book): void {
    if (book.status === 'ready' || book.status === 'readyWithWarnings') void this.router.navigate(['/read', book.id]);
    else void this.router.navigate(['/library', book.id]);
  }

  protected async remove(book: Book): Promise<void> {
    const ok = await this.confirm.ask({
      title: this.i18n.t('library.deleteTitle'), message: this.i18n.t('library.deleteMessage', { title: book.title }),
      confirm: this.i18n.t('common.delete'), danger: true
    });
    if (!ok) return;
    try {
      await this.api.delete(`/api/books/${book.id}`);
      this.books.update(list => list.filter(b => b.id !== book.id));
      this.toast.success('library.deleted');
    } catch (e) {
      this.toast.error(e);
    }
  }

  protected clock(seconds: number): string {
    return formatClock(seconds);
  }
}
