import { Injectable } from '@angular/core';
import { Router, CanActivate, ActivatedRouteSnapshot, RouterStateSnapshot, UrlTree } from '@angular/router';
import { Observable, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { AuthService } from '../services/auth.service';

@Injectable({
  providedIn: 'root'
})
export class AuthGuard implements CanActivate {
  constructor(private authService: AuthService, private router: Router) { }

  canActivate(route: ActivatedRouteSnapshot, state: RouterStateSnapshot): Observable<boolean | UrlTree> {
    // The token decides, not the stored username. Those two part company the
    // moment a token expires, and gating on the username admitted the leftover:
    // the page then rendered and every request behind it answered 401.
    if (this.authService.isAuthenticated) {
      return of(true);
    }

    // An expired access token is not the end of a session - the refresh cookie
    // usually outlives it by weeks. Since that cookie is HttpOnly, asking is the
    // only way to find out whether one is there, so navigation waits on a
    // renewal rather than assuming the worst and showing a login form to someone
    // who is still signed in.
    return this.authService.refreshSession().pipe(
      map(() => true),
      catchError(() => {
        this.authService.logout();

        return of(this.router.createUrlTree(['/login']));
      })
    );
  }
}
