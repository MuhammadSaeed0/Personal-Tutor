import { Injectable, inject, signal } from '@angular/core';
import { ApiService } from './api.service';
import { BookDetail } from './models';

/** "1:05" / "1:02:05" for a number of seconds. */
export function formatClock(seconds: number): string {
  const s = Math.max(0, Math.floor(seconds));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const sec = String(s % 60).padStart(2, '0');
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${sec}` : `${m}:${sec}`;
}

/**
 * Start time of each "page" (transcript segment) of video sources, so page references from the tutor and
 * summaries can be shown and opened as video times. Books that are not videos map to an empty record.
 */
@Injectable({ providedIn: 'root' })
export class SourceTimesService {
  private readonly api = inject(ApiService);
  private readonly requested = new Set<string>();
  readonly times = signal<Record<string, Record<number, number>>>({});

  /** Registers times already loaded (e.g. by the reader). */
  remember(detail: BookDetail): void {
    this.requested.add(detail.book.id);
    const map: Record<number, number> = {};
    if (detail.book.kind === 'youTube') for (const p of detail.pages) if (p.startSeconds !== null) map[p.pageNumber] = p.startSeconds;
    this.times.update(all => ({ ...all, [detail.book.id]: map }));
  }

  ensure(bookId: string | null | undefined): void {
    if (!bookId || this.requested.has(bookId)) return;
    this.requested.add(bookId);
    this.api.get<BookDetail>(`/api/books/${bookId}`).then(d => this.remember(d)).catch(() => undefined);
  }

  /** Video start time of a page, or null for non-video sources (or while loading). */
  time(bookId: string | null | undefined, page: number): number | null {
    if (!bookId) return null;
    return this.times()[bookId]?.[page] ?? null;
  }
}
