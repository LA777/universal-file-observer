import { Component, OnInit, AfterViewInit, Injectable, signal, ElementRef, ChangeDetectionStrategy, Renderer2, ViewChild, Input, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LiveAnnouncer } from '@angular/cdk/a11y';
import { MatTreeNestedDataSource, MatTreeModule } from '@angular/material/tree';
import { MatSort, Sort, MatSortModule } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { NestedTreeControl } from '@angular/cdk/tree';
import { Folder, File, FsItem, Snapshot } from '../../models/models';
import { SnapshotService } from '../../services/snapshot.service';
import {ArrayDataSource} from '@angular/cdk/collections';
import {CdkTree, CdkTreeModule} from '@angular/cdk/tree';
import { MatTree } from '@angular/material/tree';

@Component({
    selector: 'app-snapshot',
    templateUrl: './snapshot.component.html',
    styleUrl: './snapshot.component.css',
    standalone: true,
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [CommonModule, MatTreeModule, MatSortModule, MatTableModule, MatButtonModule, MatIconModule, MatProgressBarModule, CdkTreeModule]
})
@Injectable()
export class SnapshotComponent implements OnInit, AfterViewInit {
  public snapshot?: Snapshot;
  //treeControl = new NestedTreeControl<Folder>(node => node.childFolders);
  dataSource = new MatTreeNestedDataSource<Folder>();
  startX: number = 100;
  dynamicWidth: number = 200;
  //@ViewChild('folderTreePanelElement', { static: true }) folderTreePanelElement?: ElementRef;
  // @ViewChild('interPanelElement', { static: true }) interPanelElement?: ElementRef;
  unlistenMouseMove: () => void;
  unlistenMouseUp: () => void;
  selectedFolder: Folder;
  //displayedColumns: string[] = ['Name', 'Extension', 'Size', 'Id', 'Sha256Hash'];
  displayedColumns: string[] = ['Name', 'Extension', 'Size', 'Hidden'];
  private renderer = inject(Renderer2);
  childrenAccessor = (node: Folder) => node.childFolders ?? [];
  selectedFolderFiles: File[] = [];
  hasChild = (_: number, node: Folder) => !!node.childFolders && node.childFolders.length > 0;
  // getLevel = (node: DynamicFlatNode) => node.level;
  // isExpandable = (node: DynamicFlatNode) => node.expandable;
  clickedNode: Folder | null = null;
  folderData: File[] = [];
  @ViewChild('tree') tree: MatTree<Folder>;  // Or MatTree if you have the type

  constructor(private _liveAnnouncer: LiveAnnouncer, private snapshotService: SnapshotService) {}

  ngOnInit() {
    this.getLatestSnapshot(); // with backend

    this.snapshotService.snapshot$.subscribe((snapshot) => {
      this.snapshot = undefined;
      this.getSnapshotById(snapshot.id);
      console.log("subscribe - SnapshotComponent " + snapshot.id);
    });

    // Dev
    //this.snapshot = exampleData1;
    //this.dataSource.data = [exampleData1.snapshotEntity.rootFolder];
  }

  onNodeClick(node: Folder) {
    this.selectedFolder = node;
    this.selectedFolderFiles = node.files;
    this.folderData = node.files;
    this.clickedNode = node;
  }

  formatSize(bytes: number): string {
    if (bytes === 0) {
      return '0 B';
    }
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
  }

  ngOnDestroy() {
    this.unlistenMouseMove();
    this.unlistenMouseUp();
  }

  getLatestSnapshot() {
    this.snapshotService.getLatestSnapshot().subscribe(
      (result) => {
        this.snapshot = result;
        this.dataSource.data = [result.rootFolder];
        this.selectedFolder = result.rootFolder;
        this.folderData = result.rootFolder.files;
        this.clickedNode = result.rootFolder;
        this.expandAllNodes();
        console.log(result);
      },
      (error) => {
        console.error(error);
      }
    );
  }

  getSnapshotById(id: string) {
    this.snapshotService.getSnapshotById(id).subscribe(
      (result) => {
        this.snapshot = result;
        this.dataSource.data = [result.rootFolder];
        this.selectedFolder = result.rootFolder;
        this.folderData = result.rootFolder.files;
        this.clickedNode = result.rootFolder;
        this.expandAllNodes();
        console.log(result);
      },
      (error) => {
        console.error(error);
      }
    );
  }

  expandAllNodes() {
    this.tree.expandAll();
  }

  ngAfterViewInit() {
    this.expandAllNodes();
  }

  // onMouseDown(event: Event) {
  //   return;
  //   this.unlistenMouseMove = this.renderer.listen('document', 'mousemove', this.onMouseMove.bind(this));
  //   this.unlistenMouseUp = this.renderer.listen('document', 'mouseup', this.onMouseUp.bind(this));
  // }

  // onMouseMove(event: MouseEvent) {
  //   console.log("onMouseMove");
  //   if (event.buttons !== 1) {
  //     return;
  //   }
  //   console.log("clientWidth 1: " + this.folderTreePanelElement?.nativeElement.clientWidth);
  //   let mouseX = event.x;
  //   console.log("mouseX: " + mouseX);

  //   const offset =  this.folderTreePanelElement?.nativeElement.offsetWidth + event.pageX;
  //   // const offset = event.pageX - this.startX;
  //   console.log("offset: " + offset);
  //   const newWidth = Math.max(50, this.folderTreePanelElement?.nativeElement.offsetWidth + (offset*3)); // Minimum width constraint
  //   //const newWidth = Math.max(0, this.folderTreePanelElement?.nativeElement.clientWidth + offset); // Minimum width constraint
  //    console.log("newWidth: " + newWidth);
  //   console.log("innerWidth: " + window.innerWidth);
  //   let percent =(mouseX/window.innerWidth)*100;
  //   console.log("percent : " + percent);

  //   //this.renderer.setStyle(this.folderTreePanelElement?.nativeElement, 'width', `${newWidth}px`);
  //    this.renderer.setStyle(this.folderTreePanelElement?.nativeElement, 'width', `${percent}%`);
  //   console.log("clientWidth 2: " + this.folderTreePanelElement?.nativeElement.clientWidth);
  // }

  // onMouseUp(event: MouseEvent) {
  //   this.unlistenMouseMove();
  //   this.unlistenMouseUp();
  // }

  handleButtonClick(folder: Folder){
    this.selectedFolder = folder;
    this.folderData = [] as FsItem[];
    folder.childFolders.forEach(element => {
      const folderItem: FsItem = {
        name: element.name,
        size: element?.size,
        isFile: false,
        fileExtension: '<DIR>',
        sha256Hash: element?.sha256Hash,
        id: element?.id,
        fullPath: '',
        parentFolder: element?.parentFolder,
        isHidden: false
      };
      this.folderData.push(folderItem);
    });
    folder.files.forEach(element => {
      const folderItem: FsItem = {
        name: element.name,
        size: element?.size,
        isFile: true,
        fileExtension: element?.fileExtension,
        sha256Hash: element?.sha256Hash,
        id: element?.id,
        fullPath: '',
        parentFolder: element?.parentFolder,
        isHidden: false
      };
      this.folderData.push(folderItem);
    });
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

// Example object based on the provided JSON
// const exampleData1: SnapshotData = {
//   snapshotEntity: {
//     id: "81449418-0000-0000-0000-257df1708235",
//     timestamp: "2024-02-27T23:45:56",
//     rootFolder: {
//         files: [
//             {
//               fileExtension: ".txt",
//               size: 13,
//               sha256Hash: "e9270513606e1073d7c97b3e6464b64c22eafe1ce2911eeb3da97a8ef62c0086",
//               id: "9f7e7914-771a-44a3-aa75-0e6ed01e8cd6",
//               name: "File-TXT-2",
//               fullPath: '',
//               isFile: true,
//               isHidden: false,
//               parent: null
//             },
//             {
//               fileExtension: ".txt",
//               size: 10,
//               sha256Hash: "6d00a19c87669afe5a48c860a258d30befff0f35fbce4be30f2e28d368545877",
//               id: "aa04fc32-a932-4f96-9def-aa90b8466bae",
//               name: "File-TXT-1",
//               fullPath: '',
//               isFile: true,
//               isHidden: false,
//               parent: null
//             }
//         ],
//         childFolders: [
//             {
//                 files: [
//                     {
//                       fileExtension: ".txt",
//                       size: 14,
//                       sha256Hash: "0de14c58d31a7aef827219cde13fc84f22d9b35d9be4cfb22bffbaf8160ef315",
//                       id: "1e20837a-9488-45b6-a0ed-e94885aa774a",
//                       name: "text-file-1",
//                       fullPath: '',
//                       isFile: true,
//                       isHidden: false,
//                       parent: null
//                     },
//                     // Other files and folders...
//                 ],
//                 childFolders: [
//                     {
//                         files: [
//                             {
//                               fileExtension: ".txt",
//                               size: 13,
//                               sha256Hash: "1dd0a18f33e09b1d7e4035544090d2cbd5e7b0dd84cde3c213f44ee2f32d310e",
//                               id: "5caea84f-4371-4a03-bf87-9ff38ee97602",
//                               name: "text-file-2-1",
//                               fullPath: '',
//                               isFile: true,
//                               isHidden: false,
//                               parent: null
//                             }
//                         ],
//                         childFolders: [
//                             {
//                                 files: [
//                                     {
//                                       fileExtension: ".txt",
//                                       size: 13,
//                                       sha256Hash: "e9270513606e1073d7c97b3e6464b64c22eafe1ce2911eeb3da97a8ef62c0086",
//                                       id: "9f7e7914-771a-44a3-aa75-0e6ed01e8cd6",
//                                       name: "File-TXT-77",
//                                       fullPath: '',
//                                       isFile: true,
//                                       isHidden: false,
//                                       parent: null
//                                     }
//                                 ],
//                                 childFolders: [
//                                     {
//                                         files: [
//                                             {
//                                               fileExtension: ".txt",
//                                               size: 13,
//                                               sha256Hash: "e9270513606e1073d7c97b3e6464b64c22eafe1ce2911eeb3da97a8ef62c0086",
//                                               id: "9f7e7914-771a-44a3-aa75-0e6ed01e8cd6",
//                                               name: "File-TXT-999",
//                                               fullPath: '',
//                                               isFile: true,
//                                               isHidden: false,
//                                               parent: null
//                                             }
//                                         ],
//                                       childFolders: [],
//                                       size: 134,
//                                       sha256Hash: "4e6361ac96951274f98ed70fd29960ee15c0f2871721f8634c7feba5762433e5",
//                                       id: "75a04359-0469-4ccb-bf01-f1594a69fab9",
//                                       name: "folder 4 4",
//                                       fileExtension: '',
//                                       isFile: false,
//                                       fullPath: '',
//                                       isHidden: false,
//                                       parent: null
//                                     }
//                                 ],
//                                 size: 168,
//                                 sha256Hash: "2e6361ac96951274f98ed70fd29960ee15c0f2871721f8634c7feba5762433e5",
//                                 id: "75a04359-0869-4ccb-bf01-f1594a69fab9",
//                                 name: "folder 3 3 3",
//                                 fileExtension: '',
//                                 isFile: false,
//                                 fullPath: '',
//                             isHidden: false,
//                             parent: null
//                             }
//                         ],
//                         size: 13,
//                         sha256Hash: "1677418d5dd669ef09c20216fcebb6fc1cc3a2d2ed8303332c54044ccff771a4",
//                         id: "d3c6aaa8-e44d-4525-bc7a-8ace2472cc17",
//                         name: "folder-1-2",
//                         fileExtension: '',
//                         isFile: false,
//                         fullPath: '',
//                     isHidden: false,
//                     parent: null
//                     },
//                     {
//                         files: [
//                             {
//                                 fileExtension: ".txt",
//                                 size: 13,
//                                 sha256Hash: "e9270513606e1073d7c97b3e6464b64c22eafe1ce2911eeb3da97a8ef62c0086",
//                                 id: "9f7e7914-771a-44a3-aa75-0e6ed01e8cd6",
//                                 name: "File-TXT-345",
//                                 fullPath: '',
//                                 isFile: true,
//                             isHidden: false,
//                             parent: null
//                             }
//                         ],
//                         childFolders: [],
//                         size: 114,
//                         sha256Hash: "1e6361ac96951274f98ed70fd29960ee15c0f2871721f8634c7feba5762433e5",
//                         id: "75a34359-0469-4ccb-bf01-f1594a69fab9",
//                         name: "folder 1 3",
//                         fileExtension: '',
//                         isFile: false,
//                         fullPath: '',
//                       isHidden: false,
//                       parent: null
//                     }
//                 ],
//                 size: 69,
//                 sha256Hash: "c618b8e81813a6639304278276f5325db73165f9fcb732a4ff9f1ecea8a4700c",
//                 id: "1d04bbfe-2f79-4488-8605-16e61196c2d1",
//                 name: "folder-1",
//                 fileExtension: '',
//                 isFile: false,
//                 fullPath: '',
//             isHidden: false,
//             parent: null
//             },
//             // Other folders...
//         ],
//         size: 162,
//         sha256Hash: "2e6361ac96951274f98ed70fd29260ee15c0f2871721f8634c7feba5762433e5",
//         id: "75a04359-0868-4ccb-bf01-f1594a69fab9",
//         name: "Source Fotos",
//         fileExtension: '',
//         isFile: false,
//         fullPath: '',
//       isHidden: false,
//       parent: null
//     },
//     volumeInfo: {
//       id: "87789418-0000-0000-0000-257df1703883",
//       freeSpace: 33333322,
//       driveStatus: "",
//       volumeId: "06609418-0000-0000-0000-257df1704343",
//       volume: {
//         id: "06609418-0000-0000-0000-257df1704343",
//         driveLetter: "K",
//         volumeName: "G1ldkFs45224",
//         description: "Unit tests ABC",
//         volumeSerialNumber: "345-22222-12",
//         volumeSize: 7878787,
//         storageDrive: {
//           deviceId: "Device #340394",
//           serialNumber: "56-34-23-756",
//           totalSize: 55555555,
//           description: "Unit tests",
//           mediaType: "0990",
//           interfaceType: "333",
//           pcs: [
//             {
//               id: "11119418-0000-0000-0000-257df1704040",
//               name: "My PC 1"
//             }
//           ],
//           id: "34449418-0000-0000-0000-257df1708879",
//           name: "Name123"
//         }
//       }
//     }
//   }
// };
