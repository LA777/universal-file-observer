import { Component, OnInit, Injectable, ElementRef, HostListener, Renderer2, ViewChild, OnDestroy } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import {LiveAnnouncer} from '@angular/cdk/a11y';
import { MatTreeNestedDataSource } from '@angular/material/tree';
import { MatSort, Sort, MatSortModule } from '@angular/material/sort';
import { MatDialog } from '@angular/material/dialog';
import { NestedTreeControl } from '@angular/cdk/tree';
import { Folder, File, FsItem, SnapshotData, FileSystemRoot, DialogData } from '../../models/models';
import { DialogComponent } from '../dialog/dialog.component';

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

  constructor(private http: HttpClient, private renderer: Renderer2, private _liveAnnouncer: LiveAnnouncer, public dialog: MatDialog) {}

  ngOnInit() {
    this.getRoot();
  }

  ngOnDestroy() {
    this.unlistenMouseMove();
    this.unlistenMouseUp();
  }

  getRoot() {
    this.http.get<FileSystemRoot>('/api/filesystem/root').subscribe(
      (result) => {
        this.fileSystemRoot = result;
        this.dataSource.data = [result.folder];
        this.selectedFolder = result.folder;
        this.handleButtonClick(result.folder);
        console.log(result);
      },
      (error) => {
        const dialogData: DialogData = {
          title: 'Error',
          message: error.error
        };
        this.dialog.open(DialogComponent, { data: dialogData });
        console.error(error);
      }
    );
  }

  hasChild = (_: number, node: Folder) => !!node.childFolders && node.childFolders.length > 0;

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

  handleButtonClick(folder: Folder){
    this.selectedFolder = folder;
    this.folderData = [] as FsItem[];

    if (folder.hasParent) {
      const folderItem: FsItem = {
        name: "..",
        size: undefined,
        isFile: false,
        fileExtension: '<DIR>',
        sha256Hash: '',
        guid: '',
        fullPath: folder.fullPath,
        hasParent: true,
        isHidden: false
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
        hasParent: true,
        isHidden: element?.isHidden
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
        hasParent: true,
        isHidden: element?.isHidden
      };
      this.folderData.push(folderItem);
    });
  }

  mapRootFolder(folder: Folder) {
    this.selectedFolder = folder;
    this.folderData = [] as FsItem[];

    folder.childFolders.forEach(element => {
      const folderItem: FsItem = {
        name: element.name,
        size: element?.size,
        isFile: false,
        fileExtension: '<DIR>',
        sha256Hash: element?.sha256Hash,
        guid: element?.guid,
        fullPath: element.fullPath,
        hasParent: true,
        isHidden: element?.isHidden
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
        hasParent: true,
        isHidden: element?.isHidden
      };
      this.folderData.push(folderItem);
    });
  }

  handleDriveButtonClick(path: string) {
    const headers = new HttpHeaders({
      'Content-Type': 'application/json',
      'accept': 'text/plain'
    });

    const body = `"${path}\\"`;

    this.http.post<Folder>('/api/filesystem/folder', { path: path }, { headers }).subscribe(
      (result) => {
        //this.dataSource.data = [result];
        this.handleButtonClick(result);
        console.log(result);
      },
      (error) => {
        const dialogData: DialogData = {
          title: 'Error',
          message: error.error
        };
        this.dialog.open(DialogComponent, { data: dialogData });
        console.error(error);
      }
    );
  }

  handleHomeButtonClick() {
    this.handleButtonClick(this.fileSystemRoot.folder);
  }

  createSnapshotButtonClick() {
    const headers = new HttpHeaders({
      'Content-Type': 'application/json',
      'accept': 'text/plain'
    });

    this.http.post<string>('/api/snapshot/create', { path: this.selectedFolder.fullPath }, { headers }).subscribe(
      (result) => {
        const dialogData: DialogData = {
          title: 'Info',
          message: result
        };
        this.dialog.open(DialogComponent, { data: dialogData });
        console.log(result);
      },
      (error) => {
        const dialogData: DialogData = {
          title: 'Error',
          message: error.error
        };
        this.dialog.open(DialogComponent, { data: dialogData });
        console.error(error);
      }
    );
  }

  onRowDoubleClick(fsItem: FsItem) {
    if (fsItem.isFile) {
      return;
    }

    const headers = new HttpHeaders({
      'Content-Type': 'application/json',
      'accept': 'text/plain'
    });

    let url = '';

    if (fsItem.name === "..") {
      url = '/api/filesystem/parent';    }
    else {
      url = '/api/filesystem/folder';
    }

    this.http.post<Folder>(url, { path: fsItem.fullPath }, { headers }).subscribe(
      (result) => {
        //this.dataSource.data = [result];
        this.handleButtonClick(result);
        console.log(result);
      },
      (error) => {
        const dialogData: DialogData = {
          title: 'Error',
          message: error.error
        };
        this.dialog.open(DialogComponent, { data: dialogData });
        console.error(error);
      }
    );
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
}
