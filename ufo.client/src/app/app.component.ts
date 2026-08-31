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
        this.themeService.loadFromServer().subscribe({
          // A failed load leaves the already-applied theme in place; the
          // Settings page is where a failure is worth reporting, not here.
          error: () => { }
        });
      }
    });
  }

  openSettings() {
    this.router.navigate(['/settings']);
  }

  logout() {
    console.log('Logout button clicked');
    this.authService.logout();
    // The theme cache is one key for the whole browser, so it has to go with
    // the token — otherwise the next user to sign in here starts in this
    // user's theme.
    this.themeService.resetToDefault();
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

