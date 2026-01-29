import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AuthService } from './services/auth.service';

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
  styleUrl: './app.component.css'
})
export class AppComponent {
  title = 'ufo.client';
  currentUser$ = this.authService.currentUser$;

  constructor(
    private authService: AuthService,
    private router: Router
  ) { }

  logout() {
    console.log('Logout button clicked');
    this.authService.logout();
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

