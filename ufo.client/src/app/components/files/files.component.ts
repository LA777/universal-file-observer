import { Component, OnInit, Injectable, ElementRef, HostListener, Renderer2, ViewChild, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LiveAnnouncer } from '@angular/cdk/a11y';
import { MatTreeNestedDataSource, MatTreeModule } from '@angular/material/tree';
import { MatSort, Sort, MatSortModule } from '@angular/material/sort';
import { MatDialog } from '@angular/material/dialog';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { NestedTreeControl } from '@angular/cdk/tree';
import { Folder, File, FsItem, SnapshotData, FileSystemRoot, DialogData } from '../../models/models';
import { DialogComponent } from '../dialog/dialog.component';
import { FileService } from '../../services/file.service';
import { SnapshotService } from '../../services/snapshot.service';
import { Subscription } from 'rxjs';
import { DomSanitizer, SafeUrl } from '@angular/platform-browser';

@Component({
    selector: 'app-files',
    templateUrl: './files.component.html',
    styleUrl: './files.component.css',
    standalone: true,
    imports: [CommonModule, MatTreeModule, MatSortModule, MatTableModule, MatButtonModule, MatIconModule, MatProgressBarModule]
})
@Injectable()
export class FilesComponent implements OnInit {
  fileSystemRoot: FileSystemRoot;
  treeControl = new NestedTreeControl<Folder>(node => node.childFolders);
  dataSource = new MatTreeNestedDataSource<Folder>();
  startX: number = 100;
  dynamicWidth: number = 200;
  @ViewChild('folderTreePanelElement', { static: true }) folderTreePanelElement?: ElementRef;
  unlistenMouseMove: () => void;
  unlistenMouseUp: () => void;
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

  constructor(private renderer: Renderer2, private _liveAnnouncer: LiveAnnouncer,
    public dialog: MatDialog, private fileService: FileService, private snapshotService: SnapshotService,
    private sanitizer: DomSanitizer) {}

  ngOnInit() {
    this.getRoot();
  }

  ngOnDestroy() {
    this.unlistenMouseMove();
    this.unlistenMouseUp();

    if (this.subscriptionRoot) {
      this.subscriptionRoot.unsubscribe();
    }

    if (this.subscriptionFolder) {
      this.subscriptionFolder.unsubscribe();
    }

    if (this.subscriptionCreateSnapshot) {
      this.subscriptionCreateSnapshot.unsubscribe();
    }
  }

  getRoot() {
    this.subscriptionRoot = this.fileService.getRoot().subscribe({
        next: (result) => {
          this.fileSystemRoot = result;
          this.dataSource.data = [result.folder];
          this.selectedFolder = result.folder;
          this.initiateFolder(result.folder);
          console.log(result);
        },
        error: (error) => {
          this.showErrorDialog(error);
        },
        complete: () => {}
      });
  }

  hasChild = (_: number, node: Folder) => !!node.childFolders && node.childFolders.length > 0;

  initiateFolder(folder: Folder){
    this.selectedFolder = folder;
    this.folderData = [] as FsItem[];

    if (folder.parentFolder) {
      const folderItem: FsItem = {
        name: "..",
        size: undefined,
        isFile: false,
        fileExtension: '<DIR>',
        sha256Hash: '',
        id: '',
        fullPath: folder.fullPath,
        isHidden: false,
        parentFolder: folder?.parentFolder
      };
      this.folderData.push(folderItem);
    }

    folder.childFolders.forEach(element => {
      const folderItem: FsItem = {
        name: element.name,
        size: element?.size,
        isFile: false,
        fileExtension: '<DIR>',
        sha256Hash: element?.sha256Hash,
        id: element?.id,
        fullPath: element.fullPath,
        isHidden: element?.isHidden,
        parentFolder: element?.parentFolder
      };
      this.folderData.push(folderItem);
    });

    folder.files.forEach(element => {
      const folderItem: FsItem = {
        name: element.name,
        size: element?.size,
        isFile: true,
        fileExtension: element.fileExtension,
        sha256Hash: element?.sha256Hash,
        id: element?.id,
        fullPath: element.fullPath,
        isHidden: element?.isHidden,
        parentFolder: element?.parentFolder
      };
      this.folderData.push(folderItem);
    });
  }

  navigateToPath(path?: string) {
    this.subscriptionRoot = this.fileService.getFolder(path).subscribe({
      next: (result) => {
        //this.dataSource.data = [result];
        this.initiateFolder(result);
        console.log(result);
      },
      error: (error) => {
        this.showErrorDialog(error);
      },
      complete: () => {}
    });
  }

  createSnapshot() {
    this.subscriptionCreateSnapshot = this.snapshotService.createSnapshot(this.selectedFolder.fullPath).subscribe({
      next: (result) => {
        this.showInfoDialog(result);
      },
      error: (error) => {
        this.showErrorDialog(error);
      },
      complete: () => {}
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

  isVideoFile(fsItem: FsItem): boolean {
    if(this.isVideosView === false){
      return false;
    }

    if (fsItem.fileExtension) {
      return this.videoExtensions.includes(fsItem.fileExtension.toLowerCase());
    }

    return false;
  }

  isImageFile(fsItem: FsItem): boolean {
    if(this.isImagesView === false){
      return false;
    }

    if (fsItem.fileExtension) {
      return this.imageExtensions.includes(fsItem.fileExtension.toLowerCase());
    }

    return false;
  }

  getVideoFileUri(fsItem: FsItem){
    let baseUrl = window.location.origin;
    const encodedFilename = encodeURIComponent(fsItem.fullPath);
    let fullApiUrl = `https://localhost:44394/api/video?filePath=${encodedFilename}`;

    return this.sanitizer.bypassSecurityTrustResourceUrl(fullApiUrl);
  }

  getVideoMimeType(fsItem: FsItem){
    const lastDotIndex = fsItem.fullPath.lastIndexOf('.');
    if (lastDotIndex === -1) {
      return 'application/octet-stream'; // Default or unknown type
    }

    const extension = fsItem.fullPath.substring(lastDotIndex).toLowerCase(); // ".mp4", ".webm", etc.

    switch (extension) {
      case '.3gp':
        return 'video/3gp2';
      case '.avi': // AVI
        return 'video/x-msvideo';
      case '.mpg':
      case '.mpeg':
        return 'video/mpeg';
      case '.mp4':
      case '.m4v':
      case '.m4p':
        return 'video/mp4';
      case '.ogg':
      case '.ogv':
        return 'video/ogg';
      case '.mov': // QuickTime
        return 'video/quicktime';
      case '.mkv':
      case '.webm':
        return 'video/webm';
      // Add more as needed
      default:
        // Log a warning if you encounter an unexpected extension
        console.warn(`Unknown video extension: ${extension}. Defaulting to 'application/octet-stream'.`);
        return 'application/octet-stream'; // A generic binary type
    }
  }

  onRowDoubleClick(fsItem: FsItem) {
    if (fsItem.isFile) {
      return;
    }

    if (fsItem.name === "..") {
      this.navigateToPath(fsItem.parentFolder?.fullPath);
     }
    else {
      this.navigateToPath(fsItem.fullPath);
    }
  }

  announceSortChange(sortState: Sort) {
    // This example uses English messages. If your application supports
    // multiple language, you would internationalize these strings.
    // Furthermore, you can customize the message to add additional
    // details about the values being sorted.
    if (sortState.direction) {
      this._liveAnnouncer.announce(`Sorted ${sortState.direction}ending`);
    } else {
      this._liveAnnouncer.announce('Sorting cleared');
    }
  }

  navigateBackward() {
    console.log("navigateBackward");
  }

  navigateForward() {
    console.log("navigateForward");
  }

  showInfoDialog(result: any){
    const dialogData: DialogData = {
      title: 'Info',
      message: result
    };
    this.dialog.open(DialogComponent, { data: dialogData });
    console.log(result);
  }

  showErrorDialog(error: any){
    const dialogData: DialogData = {
      title: 'Error',
      message: error.error
    };
    this.dialog.open(DialogComponent, { data: dialogData });
    console.error(error);
  }

  onMouseDown(event: Event) {
    return;
    this.unlistenMouseMove = this.renderer.listen('document', 'mousemove', this.onMouseMove.bind(this));
    this.unlistenMouseUp = this.renderer.listen('document', 'mouseup', this.onMouseUp.bind(this));
  }

  onMouseMove(event: MouseEvent) {
    console.log("onMouseMove");
    if (event.buttons !== 1) {
      return;
    }
    console.log("clientWidth 1: " + this.folderTreePanelElement?.nativeElement.clientWidth);
    let mouseX = event.x;
    console.log("mouseX: " + mouseX);

    const offset =  this.folderTreePanelElement?.nativeElement.offsetWidth + event.pageX;
    // const offset = event.pageX - this.startX;
    console.log("offset: " + offset);
    const newWidth = Math.max(50, this.folderTreePanelElement?.nativeElement.offsetWidth + (offset*3)); // Minimum width constraint
    //const newWidth = Math.max(0, this.folderTreePanelElement?.nativeElement.clientWidth + offset); // Minimum width constraint
     console.log("newWidth: " + newWidth);
    console.log("innerWidth: " + window.innerWidth);
    let percent =(mouseX/window.innerWidth)*100;
    console.log("percent : " + percent);

    //this.renderer.setStyle(this.folderTreePanelElement?.nativeElement, 'width', `${newWidth}px`);
     this.renderer.setStyle(this.folderTreePanelElement?.nativeElement, 'width', `${percent}%`);
    console.log("clientWidth 2: " + this.folderTreePanelElement?.nativeElement.clientWidth);
  }

  onMouseUp(event: MouseEvent) {
    this.unlistenMouseMove();
    this.unlistenMouseUp();
  }
}
