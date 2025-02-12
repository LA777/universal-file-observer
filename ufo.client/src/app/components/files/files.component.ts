import { Component, OnInit, Injectable, ElementRef, HostListener, Renderer2, ViewChild, OnDestroy } from '@angular/core';
import { LiveAnnouncer } from '@angular/cdk/a11y';
import { MatTreeNestedDataSource } from '@angular/material/tree';
import { MatSort, Sort, MatSortModule } from '@angular/material/sort';
import { MatDialog } from '@angular/material/dialog';
import { NestedTreeControl } from '@angular/cdk/tree';
import { Folder, File, FsItem, SnapshotData, FileSystemRoot, DialogData } from '../../models/models';
import { DialogComponent } from '../dialog/dialog.component';
import { FileService } from '../../services/file.service';
import { SnapshotService } from '../../services/snapshot.service';
import { Subscription } from 'rxjs';

@Component({
  selector: 'app-files',
  templateUrl: './files.component.html',
  styleUrl: './files.component.css'
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
  private subscriptionRoot: Subscription;
  private subscriptionFolder: Subscription;
  private subscriptionCreateSnapshot: Subscription;

  constructor(private renderer: Renderer2, private _liveAnnouncer: LiveAnnouncer,
    public dialog: MatDialog, private fileService: FileService, private snapshotService: SnapshotService) {}

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
        guid: '',
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
        guid: element?.guid,
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
        guid: element?.guid,
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
