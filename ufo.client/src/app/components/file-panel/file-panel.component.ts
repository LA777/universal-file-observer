import { Component, OnInit, OnDestroy, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LiveAnnouncer } from '@angular/cdk/a11y';
import { MatSort, Sort, MatSortModule } from '@angular/material/sort';
import { MatDialog } from '@angular/material/dialog';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Folder, FsItem, FileSystemRoot, DialogData, FsItemUi } from '../../models/models';
import { DialogComponent } from '../dialog/dialog.component';
import { FileService } from '../../services/file.service';
import { SnapshotService } from '../../services/snapshot.service';
import { Subscription } from 'rxjs';
import { DomSanitizer } from '@angular/platform-browser';

@Component({
  selector: 'app-file-panel',
  standalone: true,
  templateUrl: './file-panel.component.html',
  styleUrl: './file-panel.component.css',
  imports: [CommonModule, MatSortModule, MatTableModule, MatButtonModule, MatIconModule, MatProgressBarModule]
})
export class FilePanelComponent implements OnInit, OnDestroy {
  @Input() panelId: string = 'panel';
  @Input() isActive: boolean = false;
  @Output() activate = new EventEmitter<void>();

  fileSystemRoot: FileSystemRoot;
  selectedFolder: Folder;
  folderData: FsItem[];
  displayedColumns: string[] = ['Name', 'Extension', 'Size'];
  isFilesView: boolean = true;
  isVideosView: boolean = false;
  isImagesView: boolean = false;
  videoExtensions = ['.mp4', '.webm', '.ogg', '.mov', '.avi', '.flv', '.mkv', '.m4v'];
  imageExtensions = ['.jpg', '.jpeg', '.png', '.gif', '.bmp', '.svg', '.webp'];

  private subscriptionRoot: Subscription;
  private subscriptionFolder: Subscription;
  private subscriptionCreateSnapshot: Subscription;

  constructor(
    private _liveAnnouncer: LiveAnnouncer,
    public dialog: MatDialog,
    private fileService: FileService,
    private snapshotService: SnapshotService,
    private sanitizer: DomSanitizer
  ) {}

  ngOnInit() {
    this.getRoot();
  }

  ngOnDestroy() {
    this.subscriptionRoot?.unsubscribe();
    this.subscriptionFolder?.unsubscribe();
    this.subscriptionCreateSnapshot?.unsubscribe();
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
      },
      error: (error) => {
        this.showErrorDialog(error);
      }
    });
  }

  initiateFolder(folder: Folder) {
    this.selectedFolder = folder;
    this.folderData = [] as FsItemUi[];

    if (folder.hasParent) {
      const folderItem: FsItemUi = {
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
      };
      this.folderData.push(folderItem);
    }

    folder.childFolders.forEach(element => {
      const folderItem: FsItemUi = {
        name: element.name,
        size: element.size,
        fileExtension: '<DIR>',
        sha256Hash: element.sha256Hash,
        id: element.id,
        fullPath: element.parentFolderPath,
        isHidden: element.isHidden,
        hasParent: element.hasParent,
        parentFolderPath: element.parentFolderPath,
        createdAt: element.createdAt,
        updatedAt: element.updatedAt,
        isFile: false
      };
      this.folderData.push(folderItem);
    });

    folder.files.forEach(element => {
      const folderItem: FsItemUi = {
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
      };
      this.folderData.push(folderItem);
    });
  }

  navigateToPath(path?: string) {
    this.subscriptionFolder = this.fileService.getFolder(path).subscribe({
      next: (result) => {
        this.initiateFolder(result);
      },
      error: (error) => {
        this.showErrorDialog(error);
      }
    });
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

  isVideoFile(fsItem: FsItemUi): boolean {
    if (!this.isVideosView) return false;
    if (fsItem.fileExtension) {
      return this.videoExtensions.includes(fsItem.fileExtension.toLowerCase());
    }
    return false;
  }

  isImageFile(fsItem: FsItemUi): boolean {
    if (!this.isImagesView) return false;
    if (fsItem.fileExtension) {
      return this.imageExtensions.includes(fsItem.fileExtension.toLowerCase());
    }
    return false;
  }

  getVideoFileUri(fsItem: FsItem) {
    const encodedFilename = encodeURIComponent(fsItem.fullPath);
    const fullApiUrl = `https://localhost:44394/api/video?filePath=${encodedFilename}`;
    return this.sanitizer.bypassSecurityTrustResourceUrl(fullApiUrl);
  }

  getVideoMimeType(fsItem: FsItemUi) {
    const lastDotIndex = fsItem.fullPath.lastIndexOf('.');
    if (lastDotIndex === -1) {
      return 'application/octet-stream';
    }
    const extension = fsItem.fullPath.substring(lastDotIndex).toLowerCase();
    switch (extension) {
      case '.3gp': return 'video/3gp2';
      case '.avi': return 'video/x-msvideo';
      case '.mpg':
      case '.mpeg': return 'video/mpeg';
      case '.mp4':
      case '.m4v':
      case '.m4p': return 'video/mp4';
      case '.ogg':
      case '.ogv': return 'video/ogg';
      case '.mov': return 'video/quicktime';
      case '.mkv':
      case '.webm': return 'video/webm';
      default:
        console.warn(`Unknown video extension: ${extension}.`);
        return 'application/octet-stream';
    }
  }

  onRowDoubleClick(fsItem: FsItemUi) {
    if (fsItem.isFile) return;
    if (fsItem.name === '..') {
      this.navigateToPath(fsItem.parentFolderPath);
    } else {
      this.navigateToPath(fsItem.fullPath);
    }
  }

  announceSortChange(sortState: Sort) {
    if (sortState.direction) {
      this._liveAnnouncer.announce(`Sorted ${sortState.direction}ending`);
    } else {
      this._liveAnnouncer.announce('Sorting cleared');
    }
  }

  navigateBackward() {
    console.log('navigateBackward');
  }

  navigateForward() {
    console.log('navigateForward');
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
