import { Injectable } from '@angular/core';
import { HttpRequest, HttpHandler, HttpEvent, HttpInterceptor, HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, throwError } from 'rxjs';
import { catchError, switchMap } from 'rxjs/operators';
import { AuthService } from '../services/auth.service';

@Injectable()
export class JwtInterceptor implements HttpInterceptor {
  constructor(private authService: AuthService, private router: Router) { }

  intercept(request: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    return next.handle(this.withToken(request)).pipe(
      catchError((error: HttpErrorResponse) => {
        if (error.status !== 401 || this.isAuthenticationRequest(request)) {
          return throwError(() => error);
        }

        // An access token lives about half an hour, so this is the ordinary way a
        // long session continues rather than an exception: renew, then replay the
        // request the user was actually making. AuthService shares one renewal
        // between every request that lapses at the same moment.
        return this.authService.refreshSession().pipe(
          catchError((refreshError: HttpErrorResponse) => {
            // The renewal itself was refused: the refresh token is gone, expired
            // or revoked. That is the end of the session.
            this.endSession();

            return throwError(() => refreshError);
          }),
          // The clone is rebuilt rather than reused: the original carries the
          // token that was just refused.
          switchMap(() => next.handle(this.withToken(request)).pipe(
            catchError((replayError: HttpErrorResponse) => {
              // Only a second 401 says anything about the session - a token minted
              // seconds ago being refused means it is not going to work. Anything
              // else (a 500, a 403, a dropped connection) belongs to the request,
              // and signing the user out over it would throw away a working
              // session because one call failed. The replay goes straight to
              // `next`, so this cannot loop back into another refresh.
              if (replayError.status === 401) {
                this.endSession();
              }

              return throwError(() => replayError);
            })
          ))
        );
      })
    );
  }

  private withToken(request: HttpRequest<any>): HttpRequest<any> {
    const token = this.authService.getToken();

    if (!token) {
      return request;
    }

    return request.clone({
      setHeaders: {
        Authorization: `Bearer ${token}`
      }
    });
  }

  private endSession(): void {
    this.authService.logout();
    this.router.navigate(['/login']);
  }

  /**
   * Signing in with the wrong password is answered with 401 as well, and so is a
   * refresh with no cookie behind it. Those are the endpoints that decide whether
   * a session exists; sending them through the renewal path would mean answering
   * a refused refresh with another refresh.
   */
  private isAuthenticationRequest(request: HttpRequest<unknown>): boolean {
    return request.url.startsWith('/api/auth/');
  }
}
