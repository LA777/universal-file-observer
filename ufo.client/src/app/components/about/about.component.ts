import { ChangeDetectionStrategy, Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { Subscription } from 'rxjs';
import { VersionService } from '../../services/version.service';

/**
 * The About tab: what this application is, and which build is answering.
 *
 * The version is fetched rather than bundled - a number compiled into the front
 * end would keep claiming the release it was built for after the server behind
 * it was updated, which is exactly the moment somebody reads this tab.
 */
@Component({
  selector: 'app-about',
  standalone: true,
  imports: [CommonModule, MatIconModule],
  templateUrl: './about.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './about.component.css'
})
export class AboutComponent implements OnInit, OnDestroy {
  /** Empty until the server answers, so the template can show it is still asking. */
  version = '';
  isLoading = true;
  error = '';

  private versionSubscription?: Subscription;

  constructor(private versionService: VersionService) {}

  ngOnInit() {
    this.versionSubscription = this.versionService.getVersion().subscribe({
      next: applicationVersion => {
        this.version = applicationVersion.version;
        this.isLoading = false;
        this.error = '';
      },
      error: () => {
        // Nothing else on this tab depends on the call, so a failure costs the
        // reader the number and nothing more - say so instead of leaving a blank.
        this.version = '';
        this.isLoading = false;
        this.error = 'The server did not report its version.';
      }
    });
  }

  ngOnDestroy() {
    this.versionSubscription?.unsubscribe();
  }
}
