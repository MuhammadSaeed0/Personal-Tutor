import { Dialog } from '@angular/cdk/dialog';
import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { I18nService, TranslatePipe } from '../core/i18n.service';
import { AppNotification } from '../core/models';
import { StoreService } from '../core/store.service';
import { ThemeService } from '../core/theme.service';
import { UploadDialogComponent } from '../features/library/upload-dialog.component';
import { IconComponent } from '../shared/icon.component';
import { BrandComponent } from './brand.component';

interface NavItem { path: string; icon: string; label: string; exact?: boolean; }

@Component({
  selector: 'rw-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, IconComponent, TranslatePipe, BrandComponent, CdkMenuTrigger, CdkMenu, CdkMenuItem],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss'
})
export class ShellComponent {
  protected readonly auth = inject(AuthService);
  protected readonly i18n = inject(I18nService);
  protected readonly store = inject(StoreService);
  protected readonly theme = inject(ThemeService);
  private readonly dialog = inject(Dialog);
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);

  protected readonly drawerOpen = signal(false);
  protected readonly subjectsOpen = signal(true);
  protected readonly notifications = signal<AppNotification[]>([]);
  protected readonly unread = computed(() => this.notifications().filter(n => !n.isRead).length);
  protected readonly initials = computed(() => (this.auth.profile()?.displayName ?? '?').trim().slice(0, 1).toUpperCase());

  protected readonly primary: NavItem[] = [
    { path: '/', icon: 'home', label: 'nav.home', exact: true },
    { path: '/library', icon: 'book-marked', label: 'nav.library' },
    { path: '/tutor', icon: 'message-circle', label: 'nav.tutor' },
    { path: '/plan', icon: 'calendar', label: 'nav.plan' }
  ];

  protected readonly practice: NavItem[] = [
    { path: '/flashcards', icon: 'layers', label: 'nav.flashcards' },
    { path: '/quizzes', icon: 'list-checks', label: 'nav.quizzes' },
    { path: '/summaries', icon: 'file-text', label: 'nav.summaries' },
    { path: '/notes', icon: 'notebook-pen', label: 'nav.notes' },
    { path: '/analytics', icon: 'bar-chart', label: 'nav.analytics' }
  ];

  protected readonly mobile: NavItem[] = [
    { path: '/', icon: 'home', label: 'nav.home', exact: true },
    { path: '/library', icon: 'book-marked', label: 'nav.library' },
    { path: '/flashcards', icon: 'layers', label: 'nav.flashcards' },
    { path: '/plan', icon: 'calendar', label: 'nav.plan' }
  ];

  constructor() {
    this.router.events.pipe(filter(e => e instanceof NavigationEnd)).subscribe(() => this.drawerOpen.set(false));
    void this.loadNotifications();
  }

  protected openUpload(): void {
    this.dialog.open(UploadDialogComponent, { autoFocus: 'first-tabbable', restoreFocus: true, maxWidth: '100vw' });
  }

  protected toggleLanguage(): void {
    void this.store.setLanguage(this.i18n.lang() === 'ar' ? 'en' : 'ar');
  }

  protected toggleTheme(): void {
    void this.store.setTheme(this.theme.resolved() === 'dark' ? 'light' : 'dark');
  }

  protected async loadNotifications(): Promise<void> {
    try {
      this.notifications.set(await this.api.get<AppNotification[]>('/api/notifications'));
    } catch { /* non-critical */ }
  }

  protected async markRead(): Promise<void> {
    if (this.unread() === 0) return;
    await this.api.post('/api/notifications/read');
    this.notifications.update(list => list.map(n => ({ ...n, isRead: true })));
  }

  protected openNotification(n: AppNotification): void {
    if (n.type === 'book_ready' && n.entityId) void this.router.navigate(['/library', n.entityId]);
  }

  protected logout(): void {
    void this.auth.logout();
  }
}
