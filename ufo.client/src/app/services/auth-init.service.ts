import { Injectable } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from './auth.service';

@Injectable({
  providedIn: 'root'
})
export class AuthInitService {
  constructor(
    private authService: AuthService,
    private router: Router
  ) { }

  initializeAuth(): Promise<void> {
    return new Promise((resolve) => {
      this.authService.checkIfUserExists().subscribe({
        next: (userExists) => {
          if (userExists) {
            // User exists, check if already logged in
            if (this.authService.currentUserValue) {
              this.router.navigate(['/dashboard']); // TODO LA - change to main app route
            } else {
              this.router.navigate(['/login']);
            }
          } else {
            // No user exists, redirect to registration
            this.router.navigate(['/register']);
          }
          resolve();
        },
        error: (error) => {
          console.error('Error checking user existence:', error);
          // Default to login on error
          this.router.navigate(['/login']);
          resolve();
        }
      });
    });
  }
}
