import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/** Adds the bearer token to API calls and transparently refreshes it once on a 401. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const isApi = req.url.startsWith('/api/');
  const isAuthEndpoint = req.url.startsWith('/api/auth/') && !req.url.startsWith('/api/auth/sessions') && !req.url.startsWith('/api/auth/change-password');

  const withToken = (request: HttpRequest<unknown>) =>
    auth.token && isApi ? request.clone({ setHeaders: { Authorization: `Bearer ${auth.token}` } }) : request;

  return next(withToken(req)).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401 || !isApi || isAuthEndpoint) return throwError(() => error);
      return from(auth.refresh()).pipe(
        switchMap(ok => {
          if (!ok) {
            void router.navigateByUrl('/auth/login');
            return throwError(() => error);
          }
          return next(withToken(req));
        })
      );
    })
  );
};
