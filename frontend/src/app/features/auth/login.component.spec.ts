import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { ApiError } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { I18nService } from '../../core/i18n.service';
import { LoginComponent } from './login.component';

describe('LoginComponent', () => {
  const auth = { login: vi.fn() };

  beforeEach(async () => {
    localStorage.clear();
    auth.login.mockReset();
    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [provideRouter([]), { provide: AuthService, useValue: auth }]
    }).compileComponents();
  });

  function setup() {
    const fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    const type = (selector: string, value: string) => {
      const input = el.querySelector<HTMLInputElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    return { fixture, el, type };
  }

  it('renders in Arabic by default', () => {
    const { el } = setup();
    expect(el.querySelector('h2')?.textContent).toContain('أهلًا بعودتك');
  });

  it('validates the email before calling the API', async () => {
    const { fixture, el, type } = setup();
    type('#email', 'not-an-email');
    type('#password', 'secret123');
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(auth.login).not.toHaveBeenCalled();
    expect(el.querySelector('.error')).not.toBeNull();
  });

  it('signs in and navigates home', async () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    auth.login.mockResolvedValue(undefined);
    const { fixture, el, type } = setup();
    type('#email', 'student@example.com');
    type('#password', 'Passw0rd-123');
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    expect(auth.login).toHaveBeenCalledWith('student@example.com', 'Passw0rd-123');
    expect(navigate).toHaveBeenCalledWith('/');
  });

  it('shows a localised message for invalid credentials', async () => {
    TestBed.inject(I18nService).setLang('en');
    auth.login.mockRejectedValue(new ApiError({ status: 401, code: 'invalid_credentials' }));
    const { fixture, el, type } = setup();
    type('#email', 'student@example.com');
    type('#password', 'wrong-pass1');
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Email or password is incorrect.');
  });
});
