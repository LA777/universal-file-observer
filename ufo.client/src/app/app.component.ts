import { Component, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AuthService } from './services/auth.service';
import { ThemeService } from './services/theme.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    MatButtonModule,
    MatIconModule,
    MatTooltipModule
  ],
  templateUrl: './app.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './app.component.css'
})
export class AppComponent implements OnInit {
  title = 'ufo.client';
  currentUser$ = this.authService.currentUser$;

  /**
   * Whether the last thing this subscription saw was a signed-in user, so the
   * sign-out work runs on the transition and not on the null that a page opened
   * signed out starts with - there is no session to clear there, and clearing
   * the theme cache would repaint the login page for nobody's benefit.
   */
  private wasSignedIn = false;

  constructor(
    private authService: AuthService,
    private themeService: ThemeService,
    private router: Router
  ) { }

  ngOnInit() {
    // ThemeService has already applied the cached theme; this replaces it with
    // whatever the database holds. currentUser$ is a BehaviorSubject, so this
    // fires straight away for an already-signed-in user and again on a later
    // sign-in, bringing that user's own theme. Only asked with a token in hand,
    // since /api/settings is authenticated.
    this.authService.currentUser$.subscribe(user => {
      if (user && this.authService.isAuthenticated) {
        this.wasSignedIn = true;

        this.themeService.loadFromServer().subscribe({
          // A failed load leaves the already-applied theme in place; the
          // Settings page is where a failure is worth reporting, not here.
          error: () => { }
        });

        return;
      }

      // The session has ended - by the button, by AuthGuard finding an expired
      // token, or by a 401 reaching JwtInterceptor. The theme cache is one key
      // for the whole browser, so it goes with the token: without this the next
      // user to sign in here paints in the previous one's theme. Every way out
      // of a session passes through this subject, which is why the reset lives
      // here rather than being repeated at each of them.
      if (this.wasSignedIn) {
        this.wasSignedIn = false;
        this.themeService.resetToDefault();
      }
    });
  }

  openSettings() {
    this.router.navigate(['/settings']);
  }

  logout() {
    console.log('Logout button clicked');
    // Revokes the refresh token server-side as well, so the session cannot be
    // resumed from another copy of the cookie. Local state is dropped inside
    // signOut() before the request goes out, so navigation below does not wait
    // on it - and resetting the theme is not repeated here either: ending the
    // session pushes null through currentUser$, and the subscription in ngOnInit
    // does it for every way a session can end.
    this.authService.signOut().subscribe();
    console.log('Auth service logout called');
    
    // Navigate to login with explicit handling
    this.router.navigate(['/login']).then(success => {
      console.log('Navigation to login:', success ? 'successful' : 'failed');
      if (!success) {
        console.error('Navigation failed, attempting reload');
        window.location.href = '/login';
      }
    }).catch(error => {
      console.error('Navigation error:', error);
      window.location.href = '/login';
    });
  }
}

