import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ServerCertificateComponent } from './server-certificate/server-certificate.component';
import { Theme } from '../../models/models';
import { ThemeService } from '../../services/theme.service';

interface ThemeChoice {
  value: Theme;
  label: string;
  icon: string;
  description: string;
}

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatTooltipModule, ServerCertificateComponent],
  templateUrl: './settings.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './settings.component.css'
})
export class SettingsComponent implements OnInit {
  readonly themeChoices: ThemeChoice[] = [
    { value: 'light', label: 'Light', icon: 'light_mode', description: 'Bright surfaces, dark text.' },
    { value: 'dark', label: 'Dark', icon: 'dark_mode', description: 'Dark surfaces, light text.' }
  ];

  selectedTheme: Theme;
  isSaving = false;
  savedMessage = '';
  errorMessage = '';

  constructor(
    private themeService: ThemeService,
    private router: Router
  ) {
    this.selectedTheme = this.themeService.currentTheme;
  }

  ngOnInit(): void {
    // Re-read from the database rather than trusting the cached theme, in case
    // it was changed from another browser.
    this.themeService.loadFromServer().subscribe({
      next: theme => (this.selectedTheme = theme),
      error: () => (this.errorMessage = 'Could not load your settings.')
    });
  }

  selectTheme(theme: Theme): void {
    if (theme === this.selectedTheme && !this.errorMessage) {
      return;
    }

    // Applied by the service before the request goes out, so the page switches
    // under the click; a failed save is reported and the choice rolled back.
    const previousTheme = this.selectedTheme;
    this.selectedTheme = theme;
    this.isSaving = true;
    this.savedMessage = '';
    this.errorMessage = '';

    this.themeService.saveTheme(theme).subscribe({
      next: () => {
        this.isSaving = false;
        this.savedMessage = 'Saved.';
      },
      error: () => {
        this.isSaving = false;
        this.selectedTheme = previousTheme;
        this.themeService.applyTheme(previousTheme);
        this.errorMessage = 'Could not save your settings. Please try again.';
      }
    });
  }

  goBack(): void {
    this.router.navigate(['/dashboard']);
  }
}
