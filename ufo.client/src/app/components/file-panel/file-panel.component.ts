import { Component, OnInit, OnDestroy, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatDialog } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Folder, FileSystemRoot, DialogData, FsItemUi } from '../../models/models';
import { DialogComponent } from '../dialog/dialog.component';
import { FileService } from '../../services/file.service';
import { SnapshotService } from '../../services/snapshot.service';
import { Subscription } from 'rxjs';
import { FolderDetailsComponent } from '../folder-details/folder-details.component';

@Component({
  selector: 'app-file-panel',
  standalone: true,
  templateUrl: './file-panel.component.html',
  styleUrl: './file-panel.component.css',
  imports: [CommonModule, MatButtonModule, MatIconModule, MatProgressBarModule, FolderDetailsComponent]
})
export class FilePanelComponent implements OnInit, OnDestroy {
  @Input() panelId: string = 'panel';
  @Input() isActive: boolean = false;
  @Output() activate = new EventEmitter<void>();

  fileSystemRoot: FileSystemRoot;
  selectedFolder: Folder;
  folderData: FsItemUi[] = [];
  isFilesView: boolean = true;
  isVideosView: boolean = false;
  isImagesView: boolean = false;

  private subscriptionRoot: Subscription;
  private subscriptionFolder: Subscription;
  private subscriptionCreateSnapshot: Subscription;

  /** Browser-style navigation history of visited folder paths. */
  private history: string[] = [];
  private historyIndex = -1;

  constructor(
    public dialog: MatDialog,
    private fileService: FileService,
    private snapshotService: SnapshotService
  ) {}

  ngOnInit() {
    this.getRoot();
  }

  ngOnDestroy() {
    this.subscriptionRoot?.unsubscribe();
    this.subscriptionFolder?.unsubscribe();
    this.subscriptionCreateSnapshot?.unsubscribe();
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
        this.fileSystemRoot = result;
        this.selectedFolder = result.folder;
        this.initiateFolder(result.folder);
        this.recordHistory(result.folder.fullPath);
      },
      error: (error) => {
        this.showErrorDialog(error);
      }
    });
  }

  initiateFolder(folder: Folder) {
    this.selectedFolder = folder;
    this.folderData = [];

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
        this.initiateFolder(result);
        if (recordHistory) {
          this.recordHistory(result.fullPath);
        }
      },
      error: (error) => {
        this.showErrorDialog(error);
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
        this.showInfoDialog(result);
      },
      error: (error) => {
        this.showErrorDialog(error);
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

  showInfoDialog(result: any) {
    const dialogData: DialogData = { title: 'Info', message: result };
    this.dialog.open(DialogComponent, { data: dialogData });
  }

  showErrorDialog(error: any) {
    const dialogData: DialogData = { title: 'Error', message: error.error };
    this.dialog.open(DialogComponent, { data: dialogData });
    console.error(error);
  }
}
