import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { I18nService, TranslatePipe } from '../../core/i18n.service';
import { errorMessage } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';

@Component({
  selector: 'rw-register',
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './auth-forms.scss',
  template: `
    <div class="auth-card">
      <div>
        <h2>{{ 'auth.registerTitle' | t }}</h2>
        <p class="lead">{{ 'auth.registerLead' | t }}</p>
      </div>
      @if (error()) { <div class="alert danger" role="alert"><rw-icon name="alert-circle" /> {{ error() }}</div> }
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <div class="field">
          <label for="name">{{ 'auth.displayName' | t }}</label>
          <input id="name" class="input" formControlName="displayName" autocomplete="given-name" maxlength="100" [class.invalid]="invalid('displayName')" />
        </div>
        <div class="field">
          <label for="email">{{ 'auth.email' | t }}</label>
          <input id="email" class="input ltr" type="email" formControlName="email" autocomplete="email" [class.invalid]="invalid('email')" />
          @if (invalid('email')) { <span class="error">{{ 'auth.emailInvalid' | t }}</span> }
        </div>
        <div class="field">
          <label for="password">{{ 'auth.password' | t }}</label>
          <div class="password">
            <input id="password" class="input ltr" [type]="show() ? 'text' : 'password'" formControlName="password" autocomplete="new-password" [class.invalid]="invalid('password')" aria-describedby="pw-hint" />
            <button class="btn ghost icon sm toggle" type="button" (click)="show.set(!show())" [attr.aria-label]="(show() ? 'auth.hidePassword' : 'auth.showPassword') | t">
              <rw-icon [name]="show() ? 'eye-off' : 'eye'" [size]="16" />
            </button>
          </div>
          <div class="bar" aria-hidden="true"><span [style.width.%]="strength() * 25" [style.background]="strength() < 2 ? 'var(--danger)' : strength() < 3 ? 'var(--warning)' : 'var(--success)'"></span></div>
          <span id="pw-hint" class="hint">{{ 'auth.passwordHint' | t }}</span>
        </div>
        <button class="btn primary lg" type="submit" [disabled]="busy()">
          @if (busy()) { <span class="spinner"></span> } {{ 'auth.createAccount' | t }}
        </button>
      </form>
      <p class="switch">{{ 'auth.haveAccount' | t }} <a routerLink="/auth/login">{{ 'auth.login' | t }}</a></p>
    </div>
  `
})
export class RegisterComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly i18n = inject(I18nService);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    displayName: ['', [Validators.required, Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(8), Validators.pattern(/\d/)]]
  });
  private readonly password = toSignal(this.form.controls.password.valueChanges, { initialValue: '' });
  protected readonly strength = computed(() => {
    const p = this.password();
    return (p.length >= 8 ? 1 : 0) + (/\d/.test(p) ? 1 : 0) + (/[A-Z]/.test(p) && /[a-z]/.test(p) ? 1 : 0) + (p.length >= 12 || /[^A-Za-z0-9]/.test(p) ? 1 : 0);
  });
  protected readonly busy = signal(false);
  protected readonly show = signal(false);
  protected readonly error = signal<string | null>(null);
  private submitted = false;

  protected invalid(name: 'displayName' | 'email' | 'password'): boolean {
    const c = this.form.controls[name];
    return c.invalid && (c.touched || this.submitted);
  }

  protected async submit(): Promise<void> {
    this.submitted = true;
    if (this.form.invalid) {
      if (this.form.controls.password.invalid) this.error.set(this.i18n.t('auth.passwordHint'));
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      const { email, password, displayName } = this.form.getRawValue();
      await this.auth.register(email, password, displayName);
      await this.router.navigateByUrl('/onboarding');
    } catch (e) {
      this.error.set(errorMessage(this.i18n, e));
    } finally {
      this.busy.set(false);
    }
  }
}
