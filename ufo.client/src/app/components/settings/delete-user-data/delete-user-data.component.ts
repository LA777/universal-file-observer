import { Component, ChangeDetectionStrategy, ChangeDetectorRef, EventEmitter, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Observable } from 'rxjs';
import { openConfirmDialog } from '../../dialog/dialog.component';
import { describeHttpError } from '../../../shared/http-error';
import { UserDataDeletionResult, UserDataService } from '../../../services/user-data.service';
import { FsItemFlagsService } from '../../../services/fs-item-flags.service';
import { FsItemRatingsService } from '../../../services/fs-item-ratings.service';
import { TagsService } from '../../../services/tags.service';
import { FolderTabsService } from '../../../services/folder-tabs.service';
import { KeyBindingsService } from '../../../services/key-bindings.service';
import { ThemeService } from '../../../services/theme.service';

/** The three kinds of data a user can delete, each behind its own button. */
export type UserDataKind = 'snapshots' | 'filesystem' | 'settings';

/** One button of the danger zone: what it deletes and how it asks. */
export interface UserDataDeletion {
  kind: UserDataKind;
  label: string;
  /** What goes, in the user's terms, shown beside the button. */
  description: string;
  confirmTitle: string;
  confirmMessage: string;
  confirmHint: string;
  /** Shown once the server has answered; the count comes from the server. */
  successMessage: string;
}

/**
 * The danger zone at the foot of the Settings page: three buttons, each
 * deleting one kind of the user's data for good.
 *
 * Its own component so the destructive part of the page is one block with one
 * style, and so SettingsComponent does not have to know which caches go stale
 * when what is deleted. Each button asks first, through the same confirm dialog
 * the Files tab uses for deleting files - a click here is not undoable either.
 */
@Component({
  selector: 'app-delete-user-data',
  standalone: true,
  imports: [CommonModule, MatIconModule, MatTooltipModule],
  templateUrl: './delete-user-data.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './delete-user-data.component.css'
})
export class DeleteUserDataComponent {
  readonly deletions: UserDataDeletion[] = [
    {
      kind: 'snapshots',
      label: 'Delete snapshots',
      description: 'Every snapshot you have taken, with its folder tree and file hashes, and every label.',
      confirmTitle: 'Delete all snapshots?',
      confirmMessage: 'Every snapshot and every label will be deleted. This cannot be undone.',
      confirmHint: 'The files and folders on disk are not touched.',
      successMessage: 'Snapshots deleted.'
    },
    {
      kind: 'filesystem',
      label: 'Delete file system data',
      description: 'Every flag, rating and tag you have put on files and folders. The files themselves stay.',
      confirmTitle: 'Delete all file system data?',
      confirmMessage: 'Every flag, rating and tag will be deleted, including the copies kept in snapshots. This cannot be undone.',
      confirmHint: 'The files and folders on disk are not touched.',
      successMessage: 'File system data deleted.'
    },
    {
      kind: 'settings',
      label: 'Delete settings',
      description: 'Your theme, keyboard shortcuts and locked folder tabs, back to how UFO came.',
      confirmTitle: 'Delete all settings?',
      confirmMessage: 'Your theme, keyboard shortcuts and locked folder tabs will be put back to the defaults. This cannot be undone.',
      confirmHint: 'Snapshots and file system data are not affected.',
      successMessage: 'Settings deleted. Everything is back to the defaults.'
    }
  ];

  /**
   * Raised after the settings are deleted, once the theme and shortcuts held
   * on the client have been reset - so the Settings page can redraw what it
   * shows of them.
   */
  @Output() settingsDeleted = new EventEmitter<void>();

  /** The deletion in flight, or null when none is. One at a time. */
  workingKind: UserDataKind | null = null;
  message = '';
  errorMessage = '';

  constructor(
    private userDataService: UserDataService,
    private fsItemFlagsService: FsItemFlagsService,
    private fsItemRatingsService: FsItemRatingsService,
    private tagsService: TagsService,
    private folderTabsService: FolderTabsService,
    private keyBindingsService: KeyBindingsService,
    private themeService: ThemeService,
    private dialog: MatDialog,
    private changeDetectorRef: ChangeDetectorRef
  ) {}

  get isWorking(): boolean {
    return this.workingKind !== null;
  }

  /** Asks first; the answer for every way of declining is to do nothing. */
  requestDeletion(deletion: UserDataDeletion): void {
    if (this.isWorking) {
      return;
    }

    openConfirmDialog(this.dialog, {
      title: deletion.confirmTitle,
      message: deletion.confirmMessage,
      hint: deletion.confirmHint,
      severity: 'error',
      confirmLabel: deletion.label,
      isDestructive: true
    }).subscribe(confirmed => {
      if (confirmed) {
        this.delete(deletion);
      }
    });
  }

  private delete(deletion: UserDataDeletion): void {
    this.workingKind = deletion.kind;
    this.message = '';
    this.errorMessage = '';
    this.changeDetectorRef.markForCheck();

    this.requestFor(deletion.kind).subscribe({
      next: result => {
        this.forgetDeleted(deletion.kind);
        this.workingKind = null;
        this.message = result?.message ? `${deletion.successMessage} ${result.message}` : deletion.successMessage;
        this.changeDetectorRef.markForCheck();
      },
      error: (error: unknown) => {
        this.workingKind = null;
        this.errorMessage = describeHttpError(error, { action: deletion.label.toLowerCase() }).message;
        this.changeDetectorRef.markForCheck();
      }
    });
  }

  private requestFor(kind: UserDataKind): Observable<UserDataDeletionResult> {
    switch (kind) {
      case 'snapshots':
        return this.userDataService.deleteSnapshots();
      case 'filesystem':
        return this.userDataService.deleteFileSystemData();
      case 'settings':
        return this.userDataService.deleteSettings();
    }
  }

  /**
   * Drops whatever the client still holds of what was just deleted.
   *
   * The marks and the settings are cached for the session - the panes consult
   * them on every render - and a cache that outlives the rows behind it would
   * keep drawing flags the server no longer knows about until a reload.
   * Snapshots are read fresh on each visit to their tab, so nothing is held.
   */
  private forgetDeleted(kind: UserDataKind): void {
    switch (kind) {
      case 'filesystem':
        this.fsItemFlagsService.reset();
        this.fsItemRatingsService.reset();
        this.tagsService.reset();
        break;
      case 'settings':
        this.folderTabsService.reset();
        this.keyBindingsService.reload();
        this.themeService.resetToDefault();
        this.settingsDeleted.emit();
        break;
    }
  }
}
