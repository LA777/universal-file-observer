import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

/**
 * Marks where the per-user settings end and the server-wide, administrator-only
 * ones begin.
 *
 * Presentational and deliberately without inputs: it is a divider, not a guard.
 * Whether a given control is offered is decided by the section below it, and the
 * server refuses the write regardless - this only tells the reader which side of
 * the line they are looking at. Every future administrator section goes beneath
 * it rather than growing its own variant of the same banner.
 */
@Component({
  selector: 'app-admin-settings-divider',
  standalone: true,
  imports: [MatIconModule],
  templateUrl: './admin-settings-divider.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './admin-settings-divider.component.css'
})
export class AdminSettingsDividerComponent {}
