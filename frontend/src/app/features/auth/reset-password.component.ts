import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { errorMessage } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';

@Component({
  selector: 'rw-reset-password',
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './auth-forms.scss',
  template: `
    <div class="auth-card">
      <div><h2>{{ 'auth.resetTitle' | t }}</h2></div>
      @if (done()) {
        <div class="alert success" role="status"><rw-icon name="check-circle" /> {{ 'auth.resetDone' | t }}</div>
        <a class="btn primary lg" routerLink="/auth/login">{{ 'auth.login' | t }}</a>
      } @else {
        @if (error()) { <div class="alert danger" role="alert">{{ error() }}</div> }
        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <div class="field">
            <label for="password">{{ 'auth.newPassword' | t }}</label>
            <input id="password" class="input ltr" type="password" formControlName="password" autocomplete="new-password" />
            <span class="hint">{{ 'auth.passwordHint' | t }}</span>
          </div>
          <button class="btn primary lg" type="submit" [disabled]="busy() || form.invalid">{{ 'auth.resetPassword' | t }}</button>
        </form>
      }
    </div>
  `
})
export class ResetPasswordComponent {
  private readonly api = inject(ApiService);
  private readonly i18n = inject(I18nService);
  private readonly route = inject(ActivatedRoute);
  protected readonly form = inject(FormBuilder).nonNullable.group({ password: ['', [Validators.required, Validators.minLength(8), Validators.pattern(/\d/)]] });
  protected readonly busy = signal(false);
  protected readonly done = signal(false);
  protected readonly error = signal<string | null>(null);

  protected async submit(): Promise<void> {
    const params = this.route.snapshot.queryParamMap;
    this.busy.set(true);
    try {
      await this.api.post('/api/auth/reset-password', { email: params.get('email'), token: params.get('token'), newPassword: this.form.getRawValue().password });
      this.done.set(true);
    } catch (e) {
      this.error.set(errorMessage(this.i18n, e));
    } finally {
      this.busy.set(false);
    }
  }
}
