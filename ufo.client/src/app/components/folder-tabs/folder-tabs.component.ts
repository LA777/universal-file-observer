import { Component, Input, Output, EventEmitter, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { FolderTab } from '../../models/models';

/**
 * The strip of folder tabs above a panel's listing.
 *
 * Presentational: it renders what it is given and reports what was clicked. The
 * panel owns the tabs, because it is the panel that navigates - and a tab strip
 * holding its own copy of where each tab points would be a second answer to that
 * question, disagreeing with the first as soon as anybody opened a folder.
 */
@Component({
  selector: 'app-folder-tabs',
  standalone: true,
  imports: [CommonModule, MatIconModule],
  templateUrl: './folder-tabs.component.html',
  styleUrl: './folder-tabs.component.css',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class FolderTabsComponent {
  @Input() tabs: FolderTab[] = [];
  @Input() activeTabId = '';

  @Output() tabSelected = new EventEmitter<string>();
  @Output() tabClosed = new EventEmitter<string>();
  @Output() lockToggled = new EventEmitter<string>();
  /** The + button: a new tab on the folder the panel is showing now. */
  @Output() tabAdded = new EventEmitter<void>();

  /**
   * Whether a tab can be closed.
   *
   * A locked tab cannot: locking is how the user said this one is worth keeping,
   * and a close button beside that invites throwing it away with one mis-click.
   * Unlocking first is the way out, and it is one click.
   */
  canClose(tab: FolderTab): boolean {
    return !tab.isLocked && this.tabs.length > 1;
  }

  lockTitle(tab: FolderTab): string {
    return tab.isLocked
      ? `Unlock "${tab.name}" - stops keeping it and lets it follow you again`
      : `Lock "${tab.name}" - keeps it for next time and pins it to this folder`;
  }
}
