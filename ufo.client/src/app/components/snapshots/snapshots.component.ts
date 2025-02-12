import { Component, OnInit, Injectable, ElementRef, ViewChild, ViewContainerRef, Output, EventEmitter } from '@angular/core';
import { Overlay, OverlayRef } from '@angular/cdk/overlay';
import { LiveAnnouncer } from '@angular/cdk/a11y';
import { MatSort, Sort, MatSortModule } from '@angular/material/sort';
import { Snapshot, StorageDrive, VolumeInfo } from '../../models/models';
import { SnapshotService } from '../../services/snapshot.service';
import { Subscription } from 'rxjs';

@Component({
    selector: 'app-snapshots',
    templateUrl: './snapshots.component.html',
    styleUrl: './snapshots.component.css',
    standalone: false
})
@Injectable()
export class SnapshotsComponent implements OnInit {
  public snapshots: Snapshot[];
  displayedColumns: string[] = ['PcName', 'StorageDriveName', 'VolumeDriveLetter', 'SnapshotGuid', 'SnapshotTimestamp', 'RootFolderName', 'RootFolderSize'];
  data: StorageDrive[] = [];
  isOpen = false;

  @ViewChild('tooltipOrigin') tooltipOrigin: ElementRef;
  @Output() tabChange = new EventEmitter<number>();
  //@Output() snapshotChange = new EventEmitter<Snapshot>();
private subscriptionAllSnapshots: Subscription;

  constructor(private _liveAnnouncer: LiveAnnouncer, private snapshotService: SnapshotService) {}

  ngOnInit() {
    this.getAllSnapshots(); // with backend
  }

  ngOnDestroy() {
    if (this.subscriptionAllSnapshots) {
      this.subscriptionAllSnapshots.unsubscribe();
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
    console.log("onRowDoubleClick - SnapshotsComponent " + snapshot.guid);
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
