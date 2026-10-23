import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { errorMessage } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';

@Component({
  selector: 'rw-forgot-password',
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './auth-forms.scss',
  template: `
    <div class="auth-card">
      <div>
        <h2>{{ 'auth.forgotTitle' | t }}</h2>
        <p class="lead">{{ 'auth.forgotLead' | t }}</p>
      </div>
      @if (sent()) {
        <div class="alert success" role="status"><rw-icon name="check-circle" /> {{ 'auth.forgotSent' | t }}</div>
      } @else {
        @if (error()) { <div class="alert danger" role="alert">{{ error() }}</div> }
        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <div class="field">
            <label for="email">{{ 'auth.email' | t }}</label>
            <input id="email" class="input ltr" type="email" formControlName="email" autocomplete="email" />
          </div>
          <button class="btn primary lg" type="submit" [disabled]="busy() || form.invalid">
            @if (busy()) { <span class="spinner"></span> } {{ 'auth.sendResetLink' | t }}
          </button>
        </form>
      }
      <p class="switch"><a routerLink="/auth/login">{{ 'auth.backToLogin' | t }}</a></p>
    </div>
  `
})
export class ForgotPasswordComponent {
  private readonly api = inject(ApiService);
  private readonly i18n = inject(I18nService);
  protected readonly form = inject(FormBuilder).nonNullable.group({ email: ['', [Validators.required, Validators.email]] });
  protected readonly busy = signal(false);
  protected readonly sent = signal(false);
  protected readonly error = signal<string | null>(null);

  protected async submit(): Promise<void> {
    this.busy.set(true);
    try {
      await this.api.post('/api/auth/forgot-password', this.form.getRawValue());
      this.sent.set(true);
    } catch (e) {
      this.error.set(errorMessage(this.i18n, e));
    } finally {
      this.busy.set(false);
    }
  }
}
