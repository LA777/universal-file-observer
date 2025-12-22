import { Component, OnInit, Injectable, ElementRef, ViewChild, ViewContainerRef, Output, EventEmitter, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Overlay, OverlayRef } from '@angular/cdk/overlay';
import { LiveAnnouncer } from '@angular/cdk/a11y';
import { MatSort, Sort, MatSortModule } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Snapshot, StorageDrive, VolumeInfo, DialogData } from '../../models/models';
import { SnapshotService } from '../../services/snapshot.service';
import { MatDialog } from '@angular/material/dialog';
import { Subscription } from 'rxjs';
import { TabChangeService } from '../../services/tab-change.service';
import { DialogComponent } from '../dialog/dialog.component';

@Component({
    selector: 'app-snapshots',
    templateUrl: './snapshots.component.html',
    styleUrl: './snapshots.component.css',
    standalone: true,
    imports: [CommonModule, MatSortModule, MatTableModule, MatButtonModule, MatIconModule, MatTooltipModule]
})
@Injectable()
export class SnapshotsComponent implements OnInit {
  public snapshots: Snapshot[];
  displayedColumns: string[] = ['PcName', 'StorageDriveName', 'VolumeDriveLetter', 'SnapshotId', 'SnapshotTimestamp', 'RootFolderName', 'RootFolderSize'];
  data: StorageDrive[] = [];
  isOpen = false;
  selectedSnapshot: Snapshot;
  @Input() tabIndex: number;
  private tabChangeSubscription: Subscription;

  @ViewChild('tooltipOrigin') tooltipOrigin: ElementRef;
  @Output() tabChange = new EventEmitter<number>();
  //@Output() snapshotChange = new EventEmitter<Snapshot>();
private subscriptionAllSnapshots: Subscription;

  constructor(private _liveAnnouncer: LiveAnnouncer, private snapshotService: SnapshotService, private tabChangeService: TabChangeService, public dialog: MatDialog) {}

  ngOnInit() {
    this.tabChangeSubscription = this.tabChangeService.tabChanged$.subscribe(index => {
      if (index === this.tabIndex) {
        this.getAllSnapshots();
      }
    });
    this.getAllSnapshots(); // with backend
  }

  ngOnDestroy() {
    if (this.subscriptionAllSnapshots) {
      this.subscriptionAllSnapshots.unsubscribe();
    }

    if (this.tabChangeSubscription) {
      this.tabChangeSubscription.unsubscribe();
    }
  }

  // ngAfterViewInit() {
  //   if (this.tooltipOrigin) { // Check if tooltipOrigin is defined
  //     // Create an observable for the mouseover event on the tooltip origin
  //     fromEvent<MouseEvent>(this.tooltipOrigin.nativeElement, 'mouseover').pipe(
  //       debounceTime(200) // Debounce the event for 200 milliseconds
  //     ).subscribe((event: MouseEvent) => {
  //       if (!this.tooltipVisible) { // Check if tooltip is not already visible
  //         this.showTooltip(event, this.data[0]); // Assuming you want to show tooltip for the first element in data array
  //       }
  //     });
  //   }
  // }

  getAllSnapshots() {
    this.subscriptionAllSnapshots = this.snapshotService.getAllSnapshots().subscribe({
      next: (result) => {
        this.snapshots = result;
        console.log(result);
      },
      error: (error) => {
        console.error(error);
      },
      complete: () => {}
    });
  }

  selectSnapshot(snapshot: Snapshot){
    this.selectedSnapshot = snapshot;
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

  deleteSnapshot(id: string){
    this.subscriptionAllSnapshots = this.snapshotService.deleteSnapshot(id).subscribe({
      next: (result) => {
        console.log(result);
      },
      error: (error) => {
        console.error(error);
      },
      complete: () => {}
    });
  }

  getStorageDriveTooltip(element: StorageDrive): string{
    return `Device ID: ${element.deviceId}\r\n
            Serial number: ${element.serialNumber}\r\n
            Total size: ${element.totalSize}\r\n
            Description: ${element.description}\r\n
            Media type: ${element.mediaType}\r\n
            Interface type: ${element.interfaceType}`;
  }

  getVolumeInfoTooltip(element: VolumeInfo): string{
    return `Volume name: ${element.volume.volumeName}\r\n
            Description: ${element.volume.description}\r\n
            Volume Serial Number: ${element.volume.volumeSerialNumber}\r\n
            Volume size: ${element.volume.volumeSize}\r\n
            Free space: ${element.freeSpace}\r\n
            Drive status: ${element.driveStatus}`;
  }

  onRowDoubleClick(snapshot: Snapshot){
    console.log("onRowDoubleClick - SnapshotsComponent " + snapshot.id);
    this.tabChange.emit(2);
    this.snapshotService.setSnapshot(snapshot);
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
