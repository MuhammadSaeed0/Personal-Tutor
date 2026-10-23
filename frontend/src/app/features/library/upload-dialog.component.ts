import { DialogRef } from '@angular/cdk/dialog';
import { HttpClient, HttpEventType } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnDestroy, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiService, toApiError } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { Book, ProcessingStatus } from '../../core/models';
import { RealtimeService } from '../../core/realtime.service';
import { StoreService } from '../../core/store.service';
import { errorMessage } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { ProcessingTimelineComponent } from './processing-timeline.component';

const MAX_MB = 150;

@Component({
  selector: 'rw-upload-dialog',
  imports: [FormsModule, TranslatePipe, IconComponent, ProcessingTimelineComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './upload-dialog.component.html',
  styleUrl: './upload-dialog.component.scss'
})
export class UploadDialogComponent implements OnDestroy {
  protected readonly i18n = inject(I18nService);
  protected readonly store = inject(StoreService);
  private readonly http = inject(HttpClient);
  private readonly api = inject(ApiService);
  private readonly realtime = inject(RealtimeService);
  private readonly router = inject(Router);
  protected readonly ref = inject(DialogRef);

  protected readonly file = signal<File | null>(null);
  protected readonly dragging = signal(false);
  protected readonly fileError = signal<string | null>(null);
  protected readonly phase = signal<'select' | 'uploading' | 'processing' | 'error'>('select');
  protected readonly uploadProgress = signal(0);
  protected readonly error = signal<string | null>(null);
  protected readonly duplicateId = signal<string | null>(null);
  protected readonly book = signal<Book | null>(null);
  private readonly polled = signal<ProcessingStatus | null>(null);

  protected subjectId: number | null = null;
  protected title = '';
  protected academicYear = '';
  protected tags = '';

  protected readonly status = computed<ProcessingStatus | null>(() => {
    const book = this.book();
    if (!book) return null;
    return this.realtime.processing()[book.id] ?? this.polled() ?? book.processing;
  });
  protected readonly done = computed(() => ['ready', 'readyWithWarnings'].includes(this.status()?.bookStatus ?? ''));
  protected readonly failed = computed(() => this.status()?.bookStatus === 'failed');

  private upload?: Subscription;
  private pollTimer?: ReturnType<typeof setInterval>;

  constructor() {
    effect(() => {
      if (this.done() || this.failed()) this.stopPolling();
    });
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    const file = event.dataTransfer?.files?.[0];
    if (file) this.pick(file);
  }

  protected onFileInput(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (file) this.pick(file);
  }

  private pick(file: File): void {
    this.fileError.set(null);
    this.duplicateId.set(null);
    if (!file.name.toLowerCase().endsWith('.pdf') && file.type !== 'application/pdf') {
      this.fileError.set(this.i18n.t('upload.notPdf'));
      return;
    }
    if (file.size > MAX_MB * 1024 * 1024) {
      this.fileError.set(this.i18n.t('upload.tooLarge', { mb: MAX_MB }));
      return;
    }
    this.file.set(file);
    if (!this.title) this.title = file.name.replace(/\.pdf$/i, '').replace(/[_-]+/g, ' ').trim();
  }

  protected start(): void {
    const file = this.file();
    if (!file || !this.subjectId) return;
    const form = new FormData();
    form.append('file', file, file.name);
    form.append('subjectId', String(this.subjectId));
    if (this.title.trim()) form.append('title', this.title.trim());
    if (this.academicYear.trim()) form.append('academicYear', this.academicYear.trim());
    if (this.tags.trim()) form.append('tags', this.tags);

    this.phase.set('uploading');
    this.error.set(null);
    this.uploadProgress.set(0);
    this.upload = this.http.post<Book>('/api/books/upload', form, { reportProgress: true, observe: 'events' }).subscribe({
      next: event => {
        if (event.type === HttpEventType.UploadProgress && event.total) this.uploadProgress.set(Math.round((100 * event.loaded) / event.total));
        if (event.type === HttpEventType.Response && event.body) {
          this.book.set(event.body);
          this.phase.set('processing');
          this.startPolling(event.body.id);
        }
      },
      error: err => {
        const apiError = toApiError(err);
        this.phase.set('error');
        this.error.set(errorMessage(this.i18n, apiError));
        if (apiError.code === 'duplicate_book') this.duplicateId.set(String(apiError.problem['bookId']));
      }
    });
  }

  protected cancel(): void {
    this.upload?.unsubscribe();
    this.phase.set('select');
    this.uploadProgress.set(0);
  }

  protected async retryProcessing(): Promise<void> {
    const book = this.book();
    if (!book) return;
    this.polled.set(await this.api.post<ProcessingStatus>(`/api/books/${book.id}/reprocess`));
    this.realtime.processing.update(map => { const copy = { ...map }; delete copy[book.id]; return copy; });
    this.startPolling(book.id);
  }

  protected openBook(): void {
    const book = this.book();
    if (!book) return;
    this.ref.close();
    void this.router.navigate(['/read', book.id]);
  }

  protected viewDetails(id: string): void {
    this.ref.close();
    void this.router.navigate(['/library', id]);
  }

  protected reset(): void {
    this.phase.set('select');
    this.error.set(null);
    this.duplicateId.set(null);
  }

  private startPolling(bookId: string): void {
    this.stopPolling();
    this.pollTimer = setInterval(async () => {
      try {
        this.polled.set(await this.api.get<ProcessingStatus>(`/api/books/${bookId}/processing-status`));
      } catch { /* keep polling */ }
    }, 2500);
  }

  private stopPolling(): void {
    if (this.pollTimer) clearInterval(this.pollTimer);
    this.pollTimer = undefined;
  }

  ngOnDestroy(): void {
    this.stopPolling();
  }

  protected formatSize(bytes: number): string {
    return bytes > 1024 * 1024 ? `${this.i18n.number(bytes / 1024 / 1024, 1)} MB` : `${this.i18n.number(bytes / 1024, 0)} KB`;
  }
}
