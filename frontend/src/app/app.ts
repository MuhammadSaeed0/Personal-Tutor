import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth.service';
import { RealtimeService } from './core/realtime.service';
import { StoreService } from './core/store.service';
import { ToastService } from './core/toast.service';
import { IconComponent } from './shared/icon.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <router-outlet />
    <div class="toasts" role="status" aria-live="polite">
      @for (toast of toasts.toasts(); track toast.id) {
        <div class="toast" [class.error]="toast.kind === 'error'" [class.success]="toast.kind === 'success'">
          <rw-icon [name]="toast.kind === 'error' ? 'alert-circle' : toast.kind === 'success' ? 'check-circle' : 'info'" />
          <span class="grow">{{ toast.message }}</span>
          <button class="btn ghost icon sm" style="color:inherit" type="button" (click)="toasts.dismiss(toast.id)" aria-label="×"><rw-icon name="x" [size]="14" /></button>
        </div>
      }
    </div>
  `
})
export class App {
  protected readonly toasts = inject(ToastService);
  private readonly store = inject(StoreService);
  private readonly realtime = inject(RealtimeService);
  private readonly auth = inject(AuthService);

  constructor() {
    void this.store.load();
    effect(() => {
      if (this.auth.isAuthenticated()) void this.realtime.connect();
      else void this.realtime.disconnect();
    });
  }
}
