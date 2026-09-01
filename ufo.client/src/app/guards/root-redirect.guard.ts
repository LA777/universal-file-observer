import { Injectable } from '@angular/core';
import { CanActivate, Router, UrlTree } from '@angular/router';
import { Observable, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { AuthService } from '../services/auth.service';

/**
 * Decides where "/" lands.
 *
 * The route used to redirect to /login for everyone, signed in or not. That is
 * what made the app look like it had let someone past the login page: they were
 * signed in the whole time, the root URL simply never asked, and typing
 * /dashboard afterwards went where it always should have.
 *
 * The three-way decision is the one AuthInitService was written to make and that
 * nothing ever called; it lives here now, on the route it belongs to.
 */
@Injectable({
  providedIn: 'root'
})
export class RootRedirectGuard implements CanActivate {
  constructor(private authService: AuthService, private router: Router) { }

  canActivate(): Observable<UrlTree> {
    if (this.authService.isAuthenticated) {
      return of(this.router.createUrlTree(['/dashboard']));
    }

    // Opening the app after the access token has aged out is the common case for
    // anyone who was here yesterday, so the refresh cookie is tried before
    // concluding that nobody is signed in.
    return this.authService.refreshSession().pipe(
      map(() => this.router.createUrlTree(['/dashboard'])),
      catchError(() => this.landingForSignedOutVisitor())
    );
  }

  private landingForSignedOutVisitor(): Observable<UrlTree> {
    // A fresh installation has no account to sign in to, so its first visitor is
    // sent to register rather than to a form that nothing they type can satisfy.
    return this.authService.checkIfUserExists().pipe(
      map(userExists => this.router.createUrlTree([userExists ? '/login' : '/register'])),
      // Unreachable server, or an answer we cannot read: login is the safe
      // landing, since it is the one page that recovers on its own once the
      // server is back.
      catchError(() => of(this.router.createUrlTree(['/login'])))
    );
  }
}
