import { DialogRef } from '@angular/cdk/dialog';
import { HttpClient, HttpEventType } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnDestroy, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiService, toApiError } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { Book, ProcessingStatus, SourceKind } from '../../core/models';
import { RealtimeService } from '../../core/realtime.service';
import { StoreService } from '../../core/store.service';
import { errorMessage } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { ProcessingTimelineComponent } from './processing-timeline.component';

const MAX_MB = 150;
const MAX_PHOTOS = 100;
const MAX_PHOTO_MB = 25;
/** Photos are scaled to this long side in the browser before upload (the server does the same). */
const PHOTO_SIDE = 2400;

interface Photo { file: File; url: string; }

/** Mirrors the server's YouTube link parsing for an instant preview. */
export function youTubeId(input: string): string | null {
  const text = input.trim();
  if (/^[A-Za-z0-9_-]{11}$/.test(text)) return text;
  try {
    const url = new URL(text.includes('://') ? text : `https://${text}`);
    const host = url.hostname.replace(/^(www|m)\./, '');
    const parts = url.pathname.split('/').filter(Boolean);
    let id: string | null = null;
    if (host === 'youtu.be') id = parts[0] ?? null;
    else if (['youtube.com', 'music.youtube.com', 'youtube-nocookie.com'].includes(host)) {
      id = parts.length >= 2 && ['shorts', 'embed', 'live', 'v'].includes(parts[0]) ? parts[1] : parts[0] === 'watch' ? url.searchParams.get('v') : null;
    }
    return id && /^[A-Za-z0-9_-]{11}$/.test(id) ? id : null;
  } catch {
    return null;
  }
}

/** Scales a photo down and re-encodes it as JPEG (upright, using EXIF orientation); returns the original if the browser can't decode it. */
export async function shrinkPhoto(file: File): Promise<Blob> {
  try {
    const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
    const scale = Math.min(1, PHOTO_SIDE / Math.max(bitmap.width, bitmap.height));
    if (scale === 1 && file.type === 'image/jpeg' && file.size < 1.5 * 1024 * 1024) { bitmap.close(); return file; }
    const canvas = document.createElement('canvas');
    canvas.width = Math.round(bitmap.width * scale);
    canvas.height = Math.round(bitmap.height * scale);
    const ctx = canvas.getContext('2d')!;
    ctx.fillStyle = '#fff';
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    ctx.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
    bitmap.close();
    return await new Promise<Blob>((resolve, reject) => canvas.toBlob(b => (b ? resolve(b) : reject()), 'image/jpeg', 0.88));
  } catch {
    return file;
  }
}

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

  protected readonly mode = signal<SourceKind>('pdf');
  protected readonly file = signal<File | null>(null);
  protected readonly photos = signal<Photo[]>([]);
  protected readonly videoUrl = signal('');
  protected readonly videoId = computed(() => youTubeId(this.videoUrl()));
  protected readonly preparing = signal(false);
  protected readonly dragging = signal(false);
  protected readonly fileError = signal<string | null>(null);
  protected readonly phase = signal<'select' | 'uploading' | 'processing' | 'error'>('select');
  protected readonly uploadProgress = signal(0);
  protected readonly error = signal<string | null>(null);
  protected readonly duplicateId = signal<string | null>(null);
  protected readonly book = signal<Book | null>(null);
  private readonly polled = signal<ProcessingStatus | null>(null);

  protected readonly subjectId = signal<number | null>(null);
  protected title = '';
  protected academicYear = '';
  protected tags = '';

  protected readonly status = computed<ProcessingStatus | null>(() => {
    const book = this.book();
    if (!book) return null;
    return this.realtime.processing()[book.id] ?? this.polled() ?? book.processing;
  });
  protected readonly canStart = computed(() => {
    if (!this.subjectId()) return false;
    switch (this.mode()) {
      case 'images': return this.photos().length > 0;
      case 'youTube': return this.videoId() !== null;
      default: return this.file() !== null;
    }
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

  protected setMode(mode: SourceKind): void {
    this.mode.set(mode);
    this.fileError.set(null);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    const files = Array.from(event.dataTransfer?.files ?? []);
    if (this.mode() === 'images') this.addPhotos(files);
    else if (files[0]) this.pick(files[0]);
  }

  protected onFileInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    if (this.mode() === 'images') this.addPhotos(files);
    else if (files[0]) this.pick(files[0]);
    input.value = '';
  }

  private addPhotos(files: File[]): void {
    this.fileError.set(null);
    this.duplicateId.set(null);
    const images = files.filter(f => f.type.startsWith('image/') || /\.(jpe?g|png|webp|gif|bmp|heic|heif)$/i.test(f.name));
    if (images.length < files.length) this.fileError.set(this.i18n.t('upload.notImage'));
    const tooBig = images.filter(f => f.size > MAX_PHOTO_MB * 1024 * 1024);
    if (tooBig.length) this.fileError.set(this.i18n.t('upload.photoTooLarge', { name: tooBig[0].name, mb: MAX_PHOTO_MB }));
    const accepted = images
      .filter(f => f.size <= MAX_PHOTO_MB * 1024 * 1024)
      .sort((a, b) => a.name.localeCompare(b.name, undefined, { numeric: true }))
      .map(file => ({ file, url: URL.createObjectURL(file) }));
    const all = [...this.photos(), ...accepted];
    if (all.length > MAX_PHOTOS) {
      this.fileError.set(this.i18n.t('upload.tooManyPhotos', { n: MAX_PHOTOS }));
      all.splice(MAX_PHOTOS).forEach(p => URL.revokeObjectURL(p.url));
    }
    this.photos.set(all);
  }

  protected movePhoto(index: number, delta: number): void {
    const list = [...this.photos()];
    const target = index + delta;
    if (target < 0 || target >= list.length) return;
    [list[index], list[target]] = [list[target], list[index]];
    this.photos.set(list);
  }

  protected removePhoto(index: number): void {
    const list = [...this.photos()];
    const [removed] = list.splice(index, 1);
    URL.revokeObjectURL(removed.url);
    this.photos.set(list);
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

  protected async start(): Promise<void> {
    if (!this.canStart()) return;
    this.error.set(null);
    this.uploadProgress.set(0);

    if (this.mode() === 'youTube') {
      this.phase.set('uploading');
      try {
        const book = await this.api.post<Book>('/api/books/youtube', {
          url: this.videoUrl().trim(), subjectId: this.subjectId(), title: this.title.trim() || null,
          academicYear: this.academicYear.trim() || null, tags: this.tags.trim() ? this.tags.split(',').map(t => t.trim()).filter(Boolean) : null
        });
        this.book.set(book);
        this.phase.set('processing');
        this.startPolling(book.id);
      } catch (e) {
        this.fail(toApiError(e));
      }
      return;
    }

    const form = new FormData();
    if (this.mode() === 'images') {
      this.preparing.set(true);
      this.phase.set('uploading');
      const blobs = await Promise.all(this.photos().map(p => shrinkPhoto(p.file)));
      this.preparing.set(false);
      blobs.forEach((blob, i) => form.append('files', blob, this.photos()[i].file.name.replace(/\.[^.]+$/, '') + (blob.type === 'image/jpeg' ? '.jpg' : '')));
    } else {
      const file = this.file()!;
      form.append('file', file, file.name);
    }
    form.append('subjectId', String(this.subjectId()));
    if (this.title.trim()) form.append('title', this.title.trim());
    if (this.academicYear.trim()) form.append('academicYear', this.academicYear.trim());
    if (this.tags.trim()) form.append('tags', this.tags);

    this.phase.set('uploading');
    const endpoint = this.mode() === 'images' ? '/api/books/images' : '/api/books/upload';
    this.upload = this.http.post<Book>(endpoint, form, { reportProgress: true, observe: 'events' }).subscribe({
      next: event => {
        if (event.type === HttpEventType.UploadProgress && event.total) this.uploadProgress.set(Math.round((100 * event.loaded) / event.total));
        if (event.type === HttpEventType.Response && event.body) {
          this.book.set(event.body);
          this.phase.set('processing');
          this.startPolling(event.body.id);
        }
      },
      error: err => this.fail(toApiError(err))
    });
  }

  private fail(apiError: ReturnType<typeof toApiError>): void {
    this.phase.set('error');
    this.error.set(errorMessage(this.i18n, apiError));
    if (apiError.code === 'duplicate_book') this.duplicateId.set(String(apiError.problem['bookId']));
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
    this.photos().forEach(p => URL.revokeObjectURL(p.url));
  }

  protected formatSize(bytes: number): string {
    return bytes > 1024 * 1024 ? `${this.i18n.number(bytes / 1024 / 1024, 1)} MB` : `${this.i18n.number(bytes / 1024, 0)} KB`;
  }
}
