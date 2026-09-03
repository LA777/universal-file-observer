import { Component, OnInit, OnDestroy, Input, Output, EventEmitter, ViewChild, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatDialog } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import {
  Folder,
  FileSystemRoot,
  DialogData,
  FileNameRules,
  FsBatchResult,
  FsItemFailure,
  FsItemUi,
  SnapshotSummary,
} from '../../models/models';
import { openMessageDialog, openConfirmDialog } from '../dialog/dialog.component';
import { describeHttpError, describeInfo } from '../../shared/http-error';
import { FileService } from '../../services/file.service';
import { SnapshotService } from '../../services/snapshot.service';
import { KeyBindingsService } from '../../services/key-bindings.service';
import { KeyBindingActions } from '../../shared/key-binding-actions';
import { Subscription } from 'rxjs';
import {
  DraftCommit,
  FolderDetailsComponent,
  RenameRequest,
} from '../folder-details/folder-details.component';
import { STRICT_FILE_NAME_RULES } from '../../shared/file-name-validation';
import { fullNameOf, isParentRow, FOLDER_EXTENSION_LABEL } from '../../shared/fs-item';

@Component({
  selector: 'app-file-panel',
  standalone: true,
  templateUrl: './file-panel.component.html',
  styleUrl: './file-panel.component.css',
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatProgressBarModule, FolderDetailsComponent]
})
export class FilePanelComponent implements OnInit, OnDestroy {
  @Input() panelId: string = 'panel';
  @Input() isActive: boolean = false;
  /**
   * Where the other panel is standing, which is where Copy and Move send things.
   * Null until that panel has loaded, and then those two buttons stay disabled -
   * there is nowhere to send anything to yet.
   */
  @Input() otherPanelPath: string | null = null;

  @Output() activate = new EventEmitter<void>();
  /** This panel's folder changed, so the other one can be told where to aim. */
  @Output() pathChanged = new EventEmitter<string>();
  /**
   * Something was written outside this panel - a copy or move that landed in the
   * other one, which is now showing a listing that is one entry short.
   */
  @Output() otherPanelChanged = new EventEmitter<void>();

  @ViewChild(FolderDetailsComponent) private folderDetails?: FolderDetailsComponent;

  fileSystemRoot: FileSystemRoot;
  selectedFolder: Folder;
  folderData: FsItemUi[] = [];
  isFilesView: boolean = true;
  isVideosView: boolean = false;
  isImagesView: boolean = false;

  /** Replaced by the host's own rules as soon as the root listing arrives. */
  nameRules: FileNameRules = STRICT_FILE_NAME_RULES;

  /** The blank row waiting for a name, or null when there is not one. */
  draftItem: FsItemUi | null = null;

  /** What Copy, Move, Delete and Rename act on. Never holds the '..' row. */
  selectedItems: FsItemUi[] = [];

  private subscriptionRoot: Subscription;
  private subscriptionFolder: Subscription;
  private subscriptionCreateSnapshot: Subscription;
  private subscriptionWrite: Subscription;

  /** Browser-style navigation history of visited folder paths. */
  private history: string[] = [];
  private historyIndex = -1;

  constructor(
    public dialog: MatDialog,
    private fileService: FileService,
    private snapshotService: SnapshotService,
    private keyBindingsService: KeyBindingsService
  ) {}

  ngOnInit() {
    this.getRoot();
    // Shared and replayed, so the second panel asking costs nothing.
    this.keyBindingsService.load().subscribe();
  }

  ngOnDestroy() {
    this.subscriptionRoot?.unsubscribe();
    this.subscriptionFolder?.unsubscribe();
    this.subscriptionCreateSnapshot?.unsubscribe();
    this.subscriptionWrite?.unsubscribe();
  }

  /**
   * Short button label for a root. A Windows drive keeps its letter; a POSIX
   * mount is named after its last segment, since every one of them starts with
   * '/' and taking the first character would label them all identically.
   */
  rootLabel(root: string): string {
    if (/^[a-zA-Z]:/.test(root)) {
      return root.substring(0, 1).toUpperCase();
    }

    const segments = root.split(/[\\/]+/).filter(segment => segment.length > 0);

    return segments.length === 0 ? '/' : segments[segments.length - 1];
  }

  onPanelClick() {
    if (!this.isActive) {
      this.activate.emit();
    }
  }

  getRoot() {
    this.subscriptionRoot = this.fileService.getRoot().subscribe({
      next: (result) => {
        if (!result?.folder) {
          this.showEmptyResponseDialog('open the starting folder');
          return;
        }

        this.fileSystemRoot = result;
        this.selectedFolder = result.folder;
        // Sent with the root because it is the one call every panel makes before
        // it can show anything, so the name box is never opened without them.
        this.nameRules = result.nameRules ?? STRICT_FILE_NAME_RULES;
        this.initiateFolder(result.folder);
        this.recordHistory(result.folder.fullPath);
      },
      error: (error) => {
        this.showErrorDialog(error, 'open the starting folder');
      }
    });
  }

  initiateFolder(folder: Folder) {
    this.selectedFolder = folder;
    this.folderData = [];
    // Both belong to the listing being replaced: a selection of paths that are no
    // longer on screen would still be what Delete acted on, and a draft would
    // create its file in whichever folder happened to be open when it closed.
    this.selectedItems = [];
    this.draftItem = null;
    this.pathChanged.emit(folder.fullPath);

    if (folder.hasParent) {
      this.folderData.push({
        name: '..',
        size: undefined,
        fileExtension: '<DIR>',
        sha256Hash: '',
        id: '',
        fullPath: folder.parentFolderPath,
        isHidden: false,
        hasParent: false,
        parentFolderPath: '',
        createdAt: '',
        updatedAt: '',
        isFile: false
      });
    }

    folder.childFolders.forEach(element => {
      this.folderData.push({
        name: element.name,
        size: element.size,
        fileExtension: '<DIR>',
        sha256Hash: element.sha256Hash,
        id: element.id,
        fullPath: element.fullPath,
        isHidden: element.isHidden,
        hasParent: element.hasParent,
        parentFolderPath: element.parentFolderPath,
        createdAt: element.createdAt,
        updatedAt: element.updatedAt,
        isFile: false
      });
    });

    folder.files.forEach(element => {
      this.folderData.push({
        name: element.name,
        size: element?.size,
        fileExtension: element.fileExtension,
        sha256Hash: element?.sha256Hash,
        id: element?.id,
        fullPath: element.fullPath,
        isHidden: element?.isHidden,
        hasParent: element.hasParent,
        parentFolderPath: element.parentFolderPath,
        createdAt: element.createdAt,
        updatedAt: element.updatedAt,
        isFile: true
      });
    });
  }

  navigateToPath(path?: string) {
    this.loadFolder(path, true);
  }

  /** Path typed into the location bar; Enter applies it. */
  onPathSubmit(rawPath: string) {
    const path = rawPath.trim();
    if (path) {
      this.navigateToPath(path);
    }
  }

  private loadFolder(path: string | undefined, recordHistory: boolean) {
    this.subscriptionFolder?.unsubscribe();
    this.subscriptionFolder = this.fileService.getFolder(path).subscribe({
      next: (result) => {
        // A 204 arrives here as a null body - the listing was cancelled server-side,
        // and rendering it would blank the pane with no word of why.
        if (!result) {
          this.showEmptyResponseDialog('open the folder', path);
          return;
        }

        this.initiateFolder(result);
        if (recordHistory) {
          this.recordHistory(result.fullPath);
        }
      },
      error: (error) => {
        this.showErrorDialog(error, 'open the folder', path);
      }
    });
  }

  /** Drop any forward entries and append the new location, skipping consecutive duplicates. */
  private recordHistory(path: string) {
    if (this.history[this.historyIndex] === path) {
      return;
    }
    this.history = this.history.slice(0, this.historyIndex + 1);
    this.history.push(path);
    this.historyIndex = this.history.length - 1;
  }

  get canNavigateBackward(): boolean {
    return this.historyIndex > 0;
  }

  get canNavigateForward(): boolean {
    return this.historyIndex < this.history.length - 1;
  }

  createSnapshot() {
    this.subscriptionCreateSnapshot = this.snapshotService.createSnapshot(this.selectedFolder.fullPath).subscribe({
      next: (result) => {
        this.showSnapshotCreatedDialog(result);
      },
      error: (error) => {
        this.showErrorDialog(error, 'create a snapshot of', this.selectedFolder?.fullPath);
      }
    });
  }

  enableFilesView() {
    this.isFilesView = true;
    this.isVideosView = false;
    this.isImagesView = false;
  }

  enableVideosView() {
    this.isFilesView = false;
    this.isVideosView = true;
    this.isImagesView = false;
  }

  enableImagesView() {
    this.isFilesView = false;
    this.isVideosView = false;
    this.isImagesView = true;
  }

  onRowDoubleClick(fsItem: FsItemUi) {
    if (fsItem.isFile) return;
    // Folder rows carry their target in fullPath — for '..' that is the parent folder.
    this.navigateToPath(fsItem.fullPath);
  }

  navigateBackward() {
    if (!this.canNavigateBackward) {
      return;
    }
    this.historyIndex--;
    this.loadFolder(this.history[this.historyIndex], false);
  }

  navigateForward() {
    if (!this.canNavigateForward) {
      return;
    }
    this.historyIndex++;
    this.loadFolder(this.history[this.historyIndex], false);
  }

  // ---------------------------------------------------------------------------
  // File and folder operations
  // ---------------------------------------------------------------------------

  /** Re-reads the current folder. The other panel calls this after writing into it. */
  refresh() {
    if (this.selectedFolder?.fullPath) {
      this.loadFolder(this.selectedFolder.fullPath, false);
    }
  }

  get hasSelection(): boolean {
    return this.selectedItems.length > 0;
  }

  /** A new entry needs a folder to go in, and only one blank row at a time. */
  get canCreate(): boolean {
    return !!this.selectedFolder && !this.draftItem;
  }

  /** There is no renaming two entries to one name, so this wants exactly one. */
  get canRename(): boolean {
    return this.selectedItems.length === 1 && this.isFilesView && !this.draftItem;
  }

  get canTransfer(): boolean {
    return this.hasSelection && !!this.otherPanelPath;
  }

  /** Names the destination in the tooltip, since the button itself cannot show it. */
  transferTitle(verb: string, shortcutKey: string): string {
    return this.otherPanelPath
      ? `${verb} to ${this.otherPanelPath} (${shortcutKey})`
      : `${verb} to the other panel (${shortcutKey}) - the other panel has not loaded yet`;
  }

  /**
   * The '..' row is filtered out here rather than left to every caller: it is not
   * an entry on disk, and the grid only excludes it from selection by mouse.
   */
  onSelectionChanged(items: FsItemUi[]) {
    this.selectedItems = items.filter(item => !isParentRow(item) && !item.isDraft);
  }

  /**
   * The shortcuts a file browser is expected to answer to.
   *
   * Which key does what is the user's, from the Settings page, so nothing is
   * matched by name here - the service is asked what a keypress means and this
   * only decides whether the answer can be carried out right now.
   *
   * Ignored while the caret is in a text box: the path bar and the name box are
   * both inputs, and Delete there means delete a character.
   */
  onPanelKeyDown(event: KeyboardEvent) {
    if (event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement) {
      return;
    }

    const actionId = this.keyBindingsService.actionFor(event);

    if (!actionId) {
      return;
    }

    const action = this.runnableActions[actionId];

    // A shortcut for something that cannot be done right now - Copy with nothing
    // selected - is left alone rather than swallowed, so the key keeps whatever
    // meaning the browser gives it.
    if (!action?.canRun()) {
      return;
    }

    // Only once the shortcut is actually being honoured: F5 would otherwise
    // reload the page and take the session's navigation history with it.
    event.preventDefault();
    action.run();
  }

  /**
   * What each bindable action does here, and when it is available.
   *
   * Keyed by the ids the server publishes, so a new action is a row in the
   * catalogue plus an entry here - and one this panel does not implement is
   * simply not found, rather than throwing.
   */
  private get runnableActions(): Record<string, { canRun: () => boolean; run: () => void }> {
    return {
      [KeyBindingActions.rename]: { canRun: () => this.canRename, run: () => this.startRename() },
      [KeyBindingActions.createFile]: { canRun: () => this.canCreate, run: () => this.startNewFile() },
      [KeyBindingActions.createFolder]: { canRun: () => this.canCreate, run: () => this.startNewFolder() },
      [KeyBindingActions.copy]: { canRun: () => this.canTransfer, run: () => this.copySelection() },
      [KeyBindingActions.move]: { canRun: () => this.canTransfer, run: () => this.moveSelection() },
      [KeyBindingActions.delete]: { canRun: () => this.hasSelection, run: () => this.deleteSelection() },
      [KeyBindingActions.navigateBackward]: {
        canRun: () => this.canNavigateBackward,
        run: () => this.navigateBackward(),
      },
      [KeyBindingActions.navigateForward]: {
        canRun: () => this.canNavigateForward,
        run: () => this.navigateForward(),
      },
      [KeyBindingActions.navigateUpward]: {
        canRun: () => !!this.selectedFolder?.hasParent,
        run: () => this.navigateToPath(this.selectedFolder.parentFolderPath),
      },
    };
  }

  // --- Create ---

  startNewFile() {
    this.beginDraft(true);
  }

  startNewFolder() {
    this.beginDraft(false);
  }

  /**
   * Puts a blank row at the top of the listing with the cursor in its name.
   *
   * Nothing is created yet, and nothing will be unless a name is typed: closing
   * the row empty leaves the folder exactly as it was. That is what makes this
   * safe to reach for by accident.
   */
  private beginDraft(isFile: boolean) {
    if (!this.selectedFolder) {
      return;
    }

    this.draftItem = {
      name: '',
      size: undefined,
      // A folder shows the listing's own label rather than an extension; a new
      // file has none until the user types one as part of the name.
      fileExtension: isFile ? '' : FOLDER_EXTENSION_LABEL,
      sha256Hash: '',
      id: '',
      fullPath: '',
      isHidden: false,
      hasParent: false,
      parentFolderPath: this.selectedFolder.fullPath,
      createdAt: '',
      updatedAt: '',
      isFile,
      isDraft: true,
    };
  }

  /** The blank row was closed with nothing in it. */
  onDraftCancelled() {
    this.draftItem = null;
  }

  onDraftCommitted(commit: DraftCommit) {
    const parentPath = this.selectedFolder?.fullPath;
    this.draftItem = null;

    if (!parentPath) {
      return;
    }

    this.subscriptionWrite?.unsubscribe();
    this.subscriptionWrite = this.fileService.createEntry(parentPath, commit.name, commit.isFile).subscribe({
      next: () => this.refresh(),
      error: (error) => {
        this.showErrorDialog(error, `create the ${commit.isFile ? 'file' : 'folder'}`, commit.name);
        // The listing never showed the new entry, but the folder may have changed
        // underneath for the very reason the create failed.
        this.refresh();
      }
    });
  }


  // --- Rename ---

  /** Opens the name box on the selected row - the Rename button and F2. */
  startRename() {
    this.folderDetails?.startRenamingSelected();
  }

  /**
   * A name was typed over an existing one.
   *
   * The name box holds the whole name, extension and all, so what comes back is
   * sent as it stands. The extension is the user's to change: "notes.txt" to
   * "notes.md" is a rename like any other.
   */
  onRenameRequested(request: RenameRequest) {
    this.subscriptionWrite?.unsubscribe();
    this.subscriptionWrite = this.fileService.renameEntry(request.item.fullPath, request.newName).subscribe({
      next: () => this.refresh(),
      error: (error) => {
        this.showErrorDialog(error, 'rename', fullNameOf(request.item));
        // Reloading is also what puts the old name back on screen.
        this.refresh();
      }
    });
  }


  // --- Copy, move and delete ---

  copySelection() {
    this.transferSelection(false);
  }

  moveSelection() {
    this.transferSelection(true);
  }

  /**
   * Sends the selection to the other panel's folder.
   *
   * Two panes are the whole reason the destination needs no dialog of its own:
   * the user put the other one where they wanted it, and it is on screen next to
   * this one. The confirmation still names it, because "the other panel" is not
   * something to take on trust before moving thirty files.
   */
  private transferSelection(isMove: boolean) {
    const destinationPath = this.otherPanelPath;

    if (!destinationPath || !this.hasSelection) {
      return;
    }

    const items = [...this.selectedItems];
    const verb = isMove ? 'Move' : 'Copy';

    openConfirmDialog(this.dialog, {
      title: verb,
      severity: 'info',
      message: `${verb} ${describeItems(items)} to "${destinationPath}"?`,
      confirmLabel: verb,
    }).subscribe(isConfirmed => {
      if (isConfirmed) {
        this.runTransfer(items.map(item => item.fullPath), destinationPath, isMove, false);
      }
    });
  }

  private runTransfer(paths: string[], destinationPath: string, isMove: boolean, overwrite: boolean) {
    const transfer = isMove
      ? this.fileService.moveEntries(paths, destinationPath, overwrite)
      : this.fileService.copyEntries(paths, destinationPath, overwrite);

    const action = isMove ? 'move the items' : 'copy the items';

    this.subscriptionWrite?.unsubscribe();
    this.subscriptionWrite = transfer.subscribe({
      next: (result) => {
        // A move empties rows out of this panel; both fill the other one.
        if (isMove) {
          this.refresh();
        }

        if (result.succeededCount > 0) {
          this.otherPanelChanged.emit();
        }

        this.handleTransferFailures(result, destinationPath, isMove, overwrite);
      },
      error: (error) => {
        this.showErrorDialog(error, action, destinationPath);
        this.refresh();
      }
    });
  }

  /**
   * What to do about the entries that did not make it.
   *
   * A collision is the one failure the user can answer, so it is put to them as a
   * question rather than reported as an error - and only the entries that
   * actually collided are re-sent, so that agreeing to replace two files cannot
   * overwrite a third that failed for some other reason.
   */
  private handleTransferFailures(
    result: FsBatchResult,
    destinationPath: string,
    isMove: boolean,
    wasOverwrite: boolean,
  ) {
    const failures = result.failures ?? [];

    if (failures.length === 0) {
      return;
    }

    const conflicts = failures.filter(failure => failure.isConflict);
    const otherFailures = failures.filter(failure => !failure.isConflict);
    const verb = isMove ? 'Move' : 'Copy';
    const pastVerb = isMove ? 'moved' : 'copied';

    // Anything that was not a collision is reported first and separately. It is
    // not a question the user can answer, and folding it into the Replace prompt
    // would put entries in front of them that replacing will not help.
    if (otherFailures.length > 0) {
      this.showFailuresDialog(verb, result.succeededCount, otherFailures, pastVerb);
    }

    // Offered even when something else failed alongside: those two failures have
    // nothing to do with each other, and the collisions are still worth asking
    // about. Only the entries that actually collided are re-sent, so agreeing
    // here cannot overwrite anything that failed for another reason.
    if (!wasOverwrite && conflicts.length > 0) {
      openConfirmDialog(this.dialog, {
        title: 'Already exists',
        severity: 'info',
        message: `${describeNames(conflicts)} already ${conflicts.length === 1 ? 'exists' : 'exist'} in "${destinationPath}".`,
        hint: 'Files are overwritten and folders merged, and that cannot be undone.',
        confirmLabel: 'Replace',
        isDestructive: true,
      }).subscribe(isConfirmed => {
        if (isConfirmed) {
          this.runTransfer(conflicts.map(conflict => conflict.path), destinationPath, isMove, true);
        }
      });

      return;
    }

    // A conflict that survived an overwrite is no longer a question, so it falls
    // in with everything else rather than prompting again.
    if (wasOverwrite && conflicts.length > 0) {
      this.showFailuresDialog(verb, result.succeededCount, conflicts, pastVerb);
    }
  }

  /**
   * Deletes the selection for good.
   *
   * There is no recycle bin behind this and no undo in front of it, so the
   * question says exactly what is about to go and how permanent it is.
   */
  deleteSelection() {
    if (!this.hasSelection) {
      return;
    }

    const items = [...this.selectedItems];
    const containsFolder = items.some(item => !item.isFile);

    openConfirmDialog(this.dialog, {
      title: 'Delete',
      severity: 'error',
      message: `Delete ${describeItems(items)} permanently?`,
      hint: containsFolder
        ? 'Folders go with everything inside them. This cannot be undone.'
        : 'This cannot be undone.',
      confirmLabel: 'Delete',
      isDestructive: true,
    }).subscribe(isConfirmed => {
      if (isConfirmed) {
        this.runDelete(items.map(item => item.fullPath));
      }
    });
  }

  private runDelete(paths: string[]) {
    this.subscriptionWrite?.unsubscribe();
    this.subscriptionWrite = this.fileService.deleteEntries(paths).subscribe({
      next: (result) => {
        this.refresh();

        if (result.failures?.length) {
          this.showFailuresDialog('Delete', result.succeededCount, result.failures, 'deleted');
        }
      },
      error: (error) => {
        this.showErrorDialog(error, 'delete the items');
        this.refresh();
      }
    });
  }


  /**
   * Names the entries an operation could not handle, and says how many it did.
   *
   * The count matters as much as the list: nineteen files copied and one locked
   * is a different situation from nothing having happened, and a popup that only
   * shows the failure reads like the second.
   */
  private showFailuresDialog(title: string, succeededCount: number, failures: FsItemFailure[], pastVerb: string) {
    const message = succeededCount > 0
      ? `${succeededCount} of ${succeededCount + failures.length} items were ${pastVerb}. ${failures.length} could not be.`
      : `Nothing was ${pastVerb}.`;

    openMessageDialog(this.dialog, {
      title,
      severity: 'error',
      message,
      hint: failures.length === 1 ? failures[0].reason : undefined,
      details: failures.map(failure => `${failure.name}: ${failure.reason}`).join('\n'),
    });
  }

  /** Confirms a created snapshot by name and id, not by dumping the response. */
  showSnapshotCreatedDialog(snapshot: SnapshotSummary) {
    const folderName = snapshot?.rootOnlyFolder?.fullPath ?? this.selectedFolder?.fullPath;

    openMessageDialog(this.dialog, {
      ...describeInfo(folderName ? `Snapshot created for "${folderName}".` : 'Snapshot created.'),
      hint: snapshot?.id ? `Snapshot id: ${snapshot.id}` : undefined,
    });
  }

  showErrorDialog(error: any, action: string, target?: string) {
    openMessageDialog(this.dialog, describeHttpError(error, { action, target }));
    console.error(error);
  }

  /**
   * A request that succeeded but came back with nothing. There is no error to
   * describe, so the popup says what was asked for and why nothing came of it.
   */
  private showEmptyResponseDialog(action: string, target?: string) {
    const dialogData: DialogData = {
      title: 'Error',
      severity: 'error',
      message: target
        ? `Could not ${action} "${target}". The server returned no content for it.`
        : `Could not ${action}. The server returned no content.`,
      hint: 'The folder may have become unreadable, or the request was cancelled before it finished.',
    };

    openMessageDialog(this.dialog, dialogData);
  }
}

/** "the folder \"backup\"", "3 items" - the subject of a confirmation question. */
function describeItems(items: FsItemUi[]): string {
  if (items.length !== 1) {
    return `${items.length} items`;
  }

  const [onlyItem] = items;

  return `the ${onlyItem.isFile ? 'file' : 'folder'} "${fullNameOf(onlyItem)}"`;
}

/**
 * The names in a list of failures, as a phrase. A long list is cut short rather
 * than filling the popup - the full list is always in the technical details.
 */
function describeNames(failures: FsItemFailure[]): string {
  const maximumNamesShown = 3;
  const names = failures.slice(0, maximumNamesShown).map(failure => `"${failure.name}"`);
  const remainingCount = failures.length - names.length;

  return remainingCount > 0
    ? `${names.join(', ')} and ${remainingCount} more`
    : names.join(', ');
}
