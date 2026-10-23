import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { errorMessage } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';

@Component({
  selector: 'rw-login',
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './auth-forms.scss',
  template: `
    <div class="auth-card">
      <div>
        <h2>{{ 'auth.loginTitle' | t }}</h2>
        <p class="lead">{{ 'auth.loginLead' | t }}</p>
      </div>
      @if (error()) { <div class="alert danger" role="alert"><rw-icon name="alert-circle" /> {{ error() }}</div> }
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <div class="field">
          <label for="email">{{ 'auth.email' | t }}</label>
          <input id="email" class="input ltr" type="email" formControlName="email" autocomplete="email" [class.invalid]="invalid('email')" />
          @if (invalid('email')) { <span class="error">{{ 'auth.emailInvalid' | t }}</span> }
        </div>
        <div class="field">
          <label for="password">{{ 'auth.password' | t }}</label>
          <div class="password">
            <input id="password" class="input ltr" [type]="show() ? 'text' : 'password'" formControlName="password" autocomplete="current-password" [class.invalid]="invalid('password')" />
            <button class="btn ghost icon sm toggle" type="button" (click)="show.set(!show())" [attr.aria-label]="(show() ? 'auth.hidePassword' : 'auth.showPassword') | t">
              <rw-icon [name]="show() ? 'eye-off' : 'eye'" [size]="16" />
            </button>
          </div>
        </div>
        <a class="forgot" routerLink="/auth/forgot-password">{{ 'auth.forgot' | t }}</a>
        <button class="btn primary lg" type="submit" [disabled]="busy()">
          @if (busy()) { <span class="spinner"></span> } {{ 'auth.login' | t }}
        </button>
      </form>
      <p class="switch">{{ 'auth.noAccount' | t }} <a routerLink="/auth/register">{{ 'auth.createAccount' | t }}</a></p>
    </div>
  `
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly i18n = inject(I18nService);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required]
  });
  protected readonly busy = signal(false);
  protected readonly show = signal(false);
  protected readonly error = signal<string | null>(null);
  private submitted = false;

  protected invalid(name: 'email' | 'password'): boolean {
    const c = this.form.controls[name];
    return c.invalid && (c.touched || this.submitted);
  }

  protected async submit(): Promise<void> {
    this.submitted = true;
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      const { email, password } = this.form.getRawValue();
      await this.auth.login(email, password);
      await this.router.navigateByUrl(this.route.snapshot.queryParamMap.get('returnUrl') ?? '/');
    } catch (e) {
      this.error.set(errorMessage(this.i18n, e));
    } finally {
      this.busy.set(false);
    }
  }
}
