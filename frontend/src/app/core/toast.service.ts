import { Injectable, inject, signal } from '@angular/core';
import { ApiError } from './api.service';
import { I18nService } from './i18n.service';

export interface Toast {
  id: number;
  kind: 'info' | 'success' | 'error';
  message: string;
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly i18n = inject(I18nService);
  private nextId = 1;
  readonly toasts = signal<Toast[]>([]);

  show(message: string, kind: Toast['kind'] = 'info', ms = 4500): void {
    const toast = { id: this.nextId++, kind, message };
    this.toasts.update(list => [...list.slice(-3), toast]);
    setTimeout(() => this.dismiss(toast.id), ms);
  }

  success(key: string, params?: Record<string, unknown>): void {
    this.show(this.i18n.t(key, params), 'success');
  }

  /** Shows a localised message for an API error code. */
  error(error: unknown): void {
    this.show(errorMessage(this.i18n, error), 'error', 6500);
  }

  dismiss(id: number): void {
    this.toasts.update(list => list.filter(t => t.id !== id));
  }
}

export function errorMessage(i18n: I18nService, error: unknown): string {
  const code = error instanceof ApiError ? error.code : 'unknown_error';
  const key = `errors.${code}`;
  return i18n.has(key) ? i18n.t(key) : i18n.t('errors.generic');
}
