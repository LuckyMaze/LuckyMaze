import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { environment } from '../environments/environment';
import { AuthService } from './auth.service';

const AUTH_ENDPOINT_SEGMENTS = ['/auth/login', '/auth/register', '/auth/refresh'];

/**
 * Attaches the access token to API requests and, on a 401, refreshes once and retries. The
 * refresh/login/register endpoints themselves are skipped - they either don't need a token or are
 * the ones that would issue it in the first place.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  const isApiRequest = req.url.startsWith(environment.apiBaseUrl);
  const isAuthEndpoint = AUTH_ENDPOINT_SEGMENTS.some((segment) => req.url.includes(segment));
  const attachToken = isApiRequest && !isAuthEndpoint;

  const token = authService.getAccessToken();
  const authorized = attachToken && token ? withBearer(req, token) : req;

  return next(authorized).pipe(
    catchError((error: unknown) => {
      if (!(attachToken && error instanceof HttpErrorResponse && error.status === 401)) {
        return throwError(() => error);
      }

      return from(authService.refresh()).pipe(
        switchMap((newToken) => {
          if (!newToken) {
            void router.navigateByUrl('/login');
            return throwError(() => error);
          }

          return next(withBearer(req, newToken));
        }),
      );
    }),
  );
};

function withBearer<T>(req: HttpRequest<T>, token: string) {
  return req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
}
