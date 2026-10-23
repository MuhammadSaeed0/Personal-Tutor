import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';

export type ThemeChoice = 'light' | 'dark' | 'system';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);
  private readonly media = window.matchMedia('(prefers-color-scheme: dark)');
  readonly choice = signal<ThemeChoice>(readStored());
  readonly resolved = signal<'light' | 'dark'>('light');

  constructor() {
    this.media.addEventListener('change', () => this.apply());
    this.apply();
  }

  /** Applies a theme. The local copy only avoids a flash on the next load; the account preference is the source of truth. */
  set(choice: ThemeChoice): void {
    this.choice.set(choice);
    try { localStorage.setItem('riwaq.theme', choice); } catch { /* storage unavailable */ }
    this.apply();
  }

  private apply(): void {
    const choice = this.choice();
    const dark = choice === 'dark' || (choice === 'system' && this.media.matches);
    this.resolved.set(dark ? 'dark' : 'light');
    this.document.documentElement.dataset['theme'] = dark ? 'dark' : 'light';
  }
}

function readStored(): ThemeChoice {
  try {
    const v = localStorage.getItem('riwaq.theme');
    return v === 'light' || v === 'dark' ? v : 'system';
  } catch {
    return 'system';
  }
}
