import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toApiError } from './api.service';
import { I18nService } from './i18n.service';
import { AuthResponse, Profile } from './models';
import { ThemeService } from './theme.service';

export const CLIENT_HEADER = 'X-Riwaq-Client';

/**
 * Session management. The access token lives only in memory; the refresh token is an HttpOnly cookie the
 * browser sends to /api/auth. Nothing sensitive is written to browser storage.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly i18n = inject(I18nService);
  private readonly theme = inject(ThemeService);

  private accessToken: string | null = null;
  private refreshTimer: ReturnType<typeof setTimeout> | null = null;
  private refreshing: Promise<boolean> | null = null;

  readonly profile = signal<Profile | null>(null);
  readonly isAuthenticated = computed(() => this.profile() !== null);
  readonly ready = signal(false);

  get token(): string | null {
    return this.accessToken;
  }

  /** Restores the session from the refresh cookie on app start. */
  async init(): Promise<void> {
    try {
      await this.refresh();
    } finally {
      this.ready.set(true);
    }
  }

  async login(email: string, password: string): Promise<void> {
    await this.handle(this.http.post<AuthResponse>('/api/auth/login', { email, password }));
  }

  async register(email: string, password: string, displayName: string): Promise<void> {
    await this.handle(this.http.post<AuthResponse>('/api/auth/register', { email, password, displayName, uiLanguage: this.i18n.lang() }));
  }

  /** Exchanges the refresh cookie for a new access token. Concurrent callers share one request. */
  refresh(): Promise<boolean> {
    this.refreshing ??= (async () => {
      try {
        const response = await firstValueFrom(this.http.post<AuthResponse>('/api/auth/refresh', {}, { headers: clientHeaders() }));
        this.apply(response);
        return true;
      } catch {
        this.clear();
        return false;
      } finally {
        this.refreshing = null;
      }
    })();
    return this.refreshing;
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post('/api/auth/logout', {}, { headers: clientHeaders() }));
    } catch { /* ignore network errors on logout */ }
    this.clear();
    await this.router.navigateByUrl('/auth/login');
  }

  setProfile(profile: Profile): void {
    this.profile.set(profile);
    this.applyPreferences(profile);
  }

  /** Called after the account is deleted server-side. */
  forget(): void {
    this.clear();
  }

  private async handle(request: import('rxjs').Observable<AuthResponse>): Promise<void> {
    try {
      this.apply(await firstValueFrom(request));
    } catch (error) {
      throw toApiError(error);
    }
  }

  private apply(response: AuthResponse): void {
    this.accessToken = response.accessToken;
    this.setProfile(response.profile);
    this.scheduleRefresh(response.expiresAt);
  }

  private applyPreferences(profile: Profile): void {
    if (profile.preferences.uiLanguage !== this.i18n.lang()) this.i18n.setLang(profile.preferences.uiLanguage);
    this.theme.set(profile.preferences.theme);
  }

  private scheduleRefresh(expiresAt: string): void {
    if (this.refreshTimer) clearTimeout(this.refreshTimer);
    const ms = Math.max(10_000, new Date(expiresAt).getTime() - Date.now() - 60_000);
    this.refreshTimer = setTimeout(() => void this.refresh(), ms);
  }

  private clear(): void {
    this.accessToken = null;
    this.profile.set(null);
    if (this.refreshTimer) clearTimeout(this.refreshTimer);
    this.refreshTimer = null;
  }
}

export function clientHeaders(): HttpHeaders {
  return new HttpHeaders({ [CLIENT_HEADER]: 'web' });
}
