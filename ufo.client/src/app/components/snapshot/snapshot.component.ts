import { Component, OnInit, Injectable, ElementRef, Renderer2, ViewChild, Input } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { LiveAnnouncer } from '@angular/cdk/a11y';
import { MatTreeNestedDataSource } from '@angular/material/tree';
import { MatSort, Sort, MatSortModule } from '@angular/material/sort';
import { NestedTreeControl } from '@angular/cdk/tree';
import { Folder, FsItem, SnapshotData, Snapshot } from '../../models/models';
import { SnapshotService } from '../../services/snapshot.service';

@Component({
  selector: 'app-snapshot',
  templateUrl: './snapshot.component.html',
  styleUrl: './snapshot.component.css'
})
@Injectable()
export class SnapshotComponent implements OnInit {
  public snapshot?: Snapshot;
  treeControl = new NestedTreeControl<Folder>(node => node.childFolders);
  dataSource = new MatTreeNestedDataSource<Folder>();
  startX: number = 100;
  dynamicWidth: number = 200;
  @ViewChild('folderTreePanelElement', { static: true }) folderTreePanelElement?: ElementRef;
  // @ViewChild('interPanelElement', { static: true }) interPanelElement?: ElementRef;
  unlistenMouseMove: () => void;
  unlistenMouseUp: () => void;
  selectedFolder: Folder;
  folderData: FsItem[];
  displayedColumns: string[] = ['Name', 'Extension', 'Size', 'Guid', 'Sha256Hash'];



  constructor(private http: HttpClient, private renderer: Renderer2, private _liveAnnouncer: LiveAnnouncer, private snapshotService: SnapshotService) {}

  ngOnInit() {
    this.getLatestSnapshot(); // with backend

    this.snapshotService.snapshot$.subscribe((snapshot) => {
      this.snapshot = undefined;
      this.getSnapshotByGuid(snapshot);
      console.log("subscribe - SnapshotComponent " + snapshot.guid);
    });

    // Dev
    //this.snapshot = exampleData1;
    //this.dataSource.data = [exampleData1.snapshotEntity.rootFolder];
  }

  ngOnDestroy() {
    this.unlistenMouseMove();
    this.unlistenMouseUp();
  }

  getLatestSnapshot() {
    this.http.get<Snapshot>('/api/snapshot/latest').subscribe(
      (result) => {
        this.snapshot = result;
        this.dataSource.data = [result.rootFolder];
        console.log(result);
      },
      (error) => {
        console.error(error);
      }
    );
  }

  getSnapshotByGuid(snapshot: Snapshot) {
    this.http.get<Snapshot>('/api/snapshot/' + snapshot.guid).subscribe(
      (result) => {
        this.snapshot = result;
        this.dataSource.data = [result.rootFolder];
        console.log(result);
      },
      (error) => {
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
    folder.childFolders.forEach(element => {
      const folderItem: FsItem = {
        name: element.name,
        size: element?.size,
        isFile: false,
        fileExtension: '<DIR>',
        sha256Hash: element?.sha256Hash,
        guid: element?.guid,
        fullPath: '',
        hasParent: false,
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
        guid: element?.guid,
        fullPath: '',
        hasParent: false,
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
const exampleData1: SnapshotData = {
  snapshotEntity: {
    guid: "81449418-0000-0000-0000-257df1708235",
    timestamp: "2024-02-27T23:45:56",
    rootFolder: {
        files: [
            {
              fileExtension: ".txt",
              size: 13,
              sha256Hash: "e9270513606e1073d7c97b3e6464b64c22eafe1ce2911eeb3da97a8ef62c0086",
              guid: "9f7e7914-771a-44a3-aa75-0e6ed01e8cd6",
              name: "File-TXT-2",
              fullPath: '',
              isFile: true,
              hasParent: true,
              isHidden: false
            },
            {
              fileExtension: ".txt",
              size: 10,
              sha256Hash: "6d00a19c87669afe5a48c860a258d30befff0f35fbce4be30f2e28d368545877",
              guid: "aa04fc32-a932-4f96-9def-aa90b8466bae",
              name: "File-TXT-1",
              fullPath: '',
              isFile: true,
              hasParent: true,
              isHidden: false
            }
        ],
        childFolders: [
            {
                files: [
                    {
                      fileExtension: ".txt",
                      size: 14,
                      sha256Hash: "0de14c58d31a7aef827219cde13fc84f22d9b35d9be4cfb22bffbaf8160ef315",
                      guid: "1e20837a-9488-45b6-a0ed-e94885aa774a",
                      name: "text-file-1",
                      fullPath: '',
                      isFile: true,
                      hasParent: true,
                      isHidden: false
                    },
                    // Other files and folders...
                ],
                childFolders: [
                    {
                        files: [
                            {
                              fileExtension: ".txt",
                              size: 13,
                              sha256Hash: "1dd0a18f33e09b1d7e4035544090d2cbd5e7b0dd84cde3c213f44ee2f32d310e",
                              guid: "5caea84f-4371-4a03-bf87-9ff38ee97602",
                              name: "text-file-2-1",
                              fullPath: '',
                              isFile: true,
                              hasParent: true,
                              isHidden: false
                            }
                        ],
                        childFolders: [
                            {
                                files: [
                                    {
                                      fileExtension: ".txt",
                                      size: 13,
                                      sha256Hash: "e9270513606e1073d7c97b3e6464b64c22eafe1ce2911eeb3da97a8ef62c0086",
                                      guid: "9f7e7914-771a-44a3-aa75-0e6ed01e8cd6",
                                      name: "File-TXT-77",
                                      fullPath: '',
                                      isFile: true,
                                      hasParent: true,
                                      isHidden: false
                                    }
                                ],
                                childFolders: [
                                    {
                                        files: [
                                            {
                                              fileExtension: ".txt",
                                              size: 13,
                                              sha256Hash: "e9270513606e1073d7c97b3e6464b64c22eafe1ce2911eeb3da97a8ef62c0086",
                                              guid: "9f7e7914-771a-44a3-aa75-0e6ed01e8cd6",
                                              name: "File-TXT-999",
                                              fullPath: '',
                                              isFile: true,
                                              hasParent: true,
                                              isHidden: false
                                            }
                                        ],
                                      childFolders: [],
                                      size: 134,
                                      sha256Hash: "4e6361ac96951274f98ed70fd29960ee15c0f2871721f8634c7feba5762433e5",
                                      guid: "75a04359-0469-4ccb-bf01-f1594a69fab9",
                                      name: "folder 4 4",
                                      fileExtension: '',
                                      isFile: false,
                                      fullPath: '',
                                      hasParent: true,
                                      isHidden: false
                                    }
                                ],
                                size: 168,
                                sha256Hash: "2e6361ac96951274f98ed70fd29960ee15c0f2871721f8634c7feba5762433e5",
                                guid: "75a04359-0869-4ccb-bf01-f1594a69fab9",
                                name: "folder 3 3 3",
                                fileExtension: '',
                                isFile: false,
                                fullPath: '',
                            hasParent: true,
                            isHidden: false
                            }
                        ],
                        size: 13,
                        sha256Hash: "1677418d5dd669ef09c20216fcebb6fc1cc3a2d2ed8303332c54044ccff771a4",
                        guid: "d3c6aaa8-e44d-4525-bc7a-8ace2472cc17",
                        name: "folder-1-2",
                        fileExtension: '',
                        isFile: false,
                        fullPath: '',
                    hasParent: true,
                    isHidden: false
                    },
                    {
                        files: [
                            {
                                fileExtension: ".txt",
                                size: 13,
                                sha256Hash: "e9270513606e1073d7c97b3e6464b64c22eafe1ce2911eeb3da97a8ef62c0086",
                                guid: "9f7e7914-771a-44a3-aa75-0e6ed01e8cd6",
                                name: "File-TXT-345",
                                fullPath: '',
                                isFile: true,
                            hasParent: true,
                            isHidden: false
                            }
                        ],
                        childFolders: [],
                        size: 114,
                        sha256Hash: "1e6361ac96951274f98ed70fd29960ee15c0f2871721f8634c7feba5762433e5",
                        guid: "75a34359-0469-4ccb-bf01-f1594a69fab9",
                        name: "folder 1 3",
                        fileExtension: '',
                        isFile: false,
                        fullPath: '',
                      hasParent: true,
                      isHidden: false
                    }
                ],
                size: 69,
                sha256Hash: "c618b8e81813a6639304278276f5325db73165f9fcb732a4ff9f1ecea8a4700c",
                guid: "1d04bbfe-2f79-4488-8605-16e61196c2d1",
                name: "folder-1",
                fileExtension: '',
                isFile: false,
                fullPath: '',
            hasParent: true,
            isHidden: false
            },
            // Other folders...
        ],
        size: 162,
        sha256Hash: "2e6361ac96951274f98ed70fd29260ee15c0f2871721f8634c7feba5762433e5",
        guid: "75a04359-0868-4ccb-bf01-f1594a69fab9",
        name: "Source Fotos",
        fileExtension: '',
        isFile: false,
        fullPath: '',
      hasParent: false,
      isHidden: false
    },
    volumeInfo: {
      guid: "87789418-0000-0000-0000-257df1703883",
      freeSpace: 33333322,
      driveStatus: "",
      volumeGuid: "06609418-0000-0000-0000-257df1704343",
      volume: {
        guid: "06609418-0000-0000-0000-257df1704343",
        driveLetter: "K",
        volumeName: "G1ldkFs45224",
        description: "Unit tests ABC",
        volumeSerialNumber: "345-22222-12",
        volumeSize: 7878787,
        storageDrive: {
          deviceId: "Device #340394",
          serialNumber: "56-34-23-756",
          totalSize: 55555555,
          description: "Unit tests",
          mediaType: "0990",
          interfaceType: "333",
          pcs: [
            {
              guid: "11119418-0000-0000-0000-257df1704040",
              name: "My PC 1"
            }
          ],
          guid: "34449418-0000-0000-0000-257df1708879",
          name: "Name123"
        }
      }
    }
  }
};
