import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';
import { I18nService } from './i18n.service';
import { Lang, Preferences, Profile, Subject, SystemStatus } from './models';
import { ThemeService, ThemeChoice } from './theme.service';

/** Small shared cache for reference data and preference updates. */
@Injectable({ providedIn: 'root' })
export class StoreService {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly i18n = inject(I18nService);
  private readonly theme = inject(ThemeService);

  readonly subjects = signal<Subject[]>([]);
  readonly system = signal<SystemStatus | null>(null);
  readonly subjectById = computed(() => new Map(this.subjects().map(s => [s.id, s])));

  async load(attempt = 0): Promise<void> {
    try {
      const [subjects, system] = await Promise.all([
        this.api.get<Subject[]>('/api/subjects'),
        this.api.get<SystemStatus>('/api/system/status').catch(() => null)
      ]);
      this.subjects.set(subjects);
      this.system.set(system);
    } catch {
      // The API may still be starting or restarting; retry so the subject list never stays empty.
      setTimeout(() => void this.load(attempt + 1), Math.min(30_000, 1000 * 2 ** attempt));
    }
  }

  subject(id: number | null | undefined): Subject | undefined {
    return id == null ? undefined : this.subjectById().get(id);
  }

  subjectName(id: number | null | undefined): string {
    return this.i18n.subjectName(this.subject(id));
  }

  /** Switches the interface language immediately and persists it to the account. */
  async setLanguage(lang: Lang): Promise<void> {
    this.i18n.setLang(lang);
    await this.updatePreferences({ uiLanguage: lang });
  }

  async setTheme(theme: ThemeChoice): Promise<void> {
    this.theme.set(theme);
    await this.updatePreferences({ theme });
  }

  async updatePreferences(changes: Partial<Preferences>): Promise<void> {
    const profile = this.auth.profile();
    if (!profile) return;
    const preferences = await this.api.patch<Preferences>('/api/me/preferences', changes);
    this.auth.profile.set({ ...profile, preferences } satisfies Profile);
  }
}
