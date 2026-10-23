import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ApiService, saveBlob } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { Exam, Lang, Preferences, Profile, SessionInfo } from '../../core/models';
import { StoreService } from '../../core/store.service';
import { ThemeChoice } from '../../core/theme.service';
import { ToastService } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { ConfirmService } from '../../shared/ui';

type Tab = 'profile' | 'preferences' | 'academic' | 'exams' | 'sessions' | 'privacy';

@Component({
  selector: 'rw-settings',
  imports: [FormsModule, TranslatePipe, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './settings.component.html',
  styleUrl: './settings.component.scss'
})
export class SettingsComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly store = inject(StoreService);
  protected readonly auth = inject(AuthService);
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly router = inject(Router);

  protected readonly tabs: Tab[] = ['profile', 'preferences', 'academic', 'exams', 'sessions', 'privacy'];
  protected readonly tab = signal<Tab>((inject(ActivatedRoute).snapshot.queryParamMap.get('tab') as Tab) ?? 'profile');
  protected readonly exams = signal<Exam[]>([]);
  protected readonly sessions = signal<SessionInfo[]>([]);

  private readonly profile = this.auth.profile()!;
  protected displayName = this.profile.displayName;
  protected currentPassword = '';
  protected newPassword = '';
  protected prefs: Preferences = { ...this.profile.preferences };
  protected academic = { ...this.profile.academic, difficultSubjectIds: [...this.profile.academic.difficultSubjectIds] };
  protected newExam = { title: '', date: '', subjectId: null as number | null };
  protected deletePassword = '';

  constructor() {
    void this.loadExams();
    void this.loadSessions();
  }

  protected async saveProfile(): Promise<void> {
    try {
      this.auth.setProfile(await this.api.patch<Profile>('/api/me', { displayName: this.displayName }));
      this.toast.success('common.saved');
    } catch (e) { this.toast.error(e); }
  }

  protected async changePassword(): Promise<void> {
    try {
      await this.api.post('/api/auth/change-password', { currentPassword: this.currentPassword, newPassword: this.newPassword });
      this.currentPassword = '';
      this.newPassword = '';
      this.toast.success('settings.passwordChanged');
    } catch (e) { this.toast.error(e); }
  }

  protected async savePreferences(): Promise<void> {
    try {
      await this.store.updatePreferences(this.prefs);
      if (this.prefs.uiLanguage !== this.i18n.lang()) this.i18n.setLang(this.prefs.uiLanguage as Lang);
      await this.store.setTheme(this.prefs.theme as ThemeChoice);
      this.toast.success('common.saved');
    } catch (e) { this.toast.error(e); }
  }

  protected toggleDifficult(id: number): void {
    const list = this.academic.difficultSubjectIds;
    this.academic.difficultSubjectIds = list.includes(id) ? list.filter(x => x !== id) : [...list, id];
  }

  protected async saveAcademic(): Promise<void> {
    try {
      this.auth.setProfile(await this.api.patch<Profile>('/api/me/academic', this.academic));
      this.toast.success('common.saved');
    } catch (e) { this.toast.error(e); }
  }

  private async loadExams(): Promise<void> {
    this.exams.set(await this.api.get<Exam[]>('/api/exams'));
  }

  protected async addExam(): Promise<void> {
    if (!this.newExam.title.trim() || !this.newExam.date) return;
    try {
      await this.api.post('/api/exams', { ...this.newExam, title: this.newExam.title.trim(), notes: null });
      this.newExam = { title: '', date: '', subjectId: null };
      await this.loadExams();
    } catch (e) { this.toast.error(e); }
  }

  protected async removeExam(exam: Exam): Promise<void> {
    await this.api.delete(`/api/exams/${exam.id}`);
    await this.loadExams();
  }

  private async loadSessions(): Promise<void> {
    try { this.sessions.set(await this.api.get<SessionInfo[]>('/api/auth/sessions')); } catch { /* optional */ }
  }

  protected async revoke(session: SessionInfo): Promise<void> {
    await this.api.delete(`/api/auth/sessions/${session.id}`);
    await this.loadSessions();
  }

  protected async exportData(): Promise<void> {
    saveBlob(await this.api.blob('/api/account/export'), 'riwaq-export.json');
  }

  protected async deleteAccount(): Promise<void> {
    const ok = await this.confirm.ask({ title: this.i18n.t('settings.deleteTitle'), message: this.i18n.t('settings.deleteMessage'), confirm: this.i18n.t('settings.deleteConfirm'), danger: true });
    if (!ok) return;
    try {
      await this.api.post('/api/account/delete', { password: this.deletePassword });
      this.auth.forget();
      void this.router.navigateByUrl('/auth/register');
    } catch (e) { this.toast.error(e); }
  }

  protected device(ua: string | null): string {
    if (!ua) return this.i18n.t('settings.unknownDevice');
    const browser = /Edg\//.test(ua) ? 'Edge' : /Chrome\//.test(ua) ? 'Chrome' : /Firefox\//.test(ua) ? 'Firefox' : /Safari\//.test(ua) ? 'Safari' : 'Browser';
    const os = /Windows/.test(ua) ? 'Windows' : /Android/.test(ua) ? 'Android' : /iPhone|iPad/.test(ua) ? 'iOS' : /Mac OS/.test(ua) ? 'macOS' : /Linux/.test(ua) ? 'Linux' : '';
    return `${browser}${os ? ' · ' + os : ''}`;
  }
}
