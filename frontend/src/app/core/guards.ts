import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.isAuthenticated()) return true;
  return router.createUrlTree(['/auth/login'], { queryParams: state.url !== '/' ? { returnUrl: state.url } : {} });
};

export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.isAuthenticated() ? inject(Router).createUrlTree(['/']) : true;
};

/** New students go through the (skippable) onboarding questions first. */
export const onboardingGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const profile = auth.profile();
  if (profile && !profile.academic.onboardingCompleted) return inject(Router).createUrlTree(['/onboarding']);
  return true;
};
