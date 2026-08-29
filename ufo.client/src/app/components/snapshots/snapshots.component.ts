import { Component, EventEmitter, Input, OnDestroy, OnInit, Output, ChangeDetectionStrategy } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog } from '@angular/material/dialog';
import { AgGridAngular } from 'ag-grid-angular';
import {
  ColDef,
  ICellRendererParams,
  RowDoubleClickedEvent,
  RowSelectionOptions,
  SelectionChangedEvent,
  ValueFormatterParams,
} from 'ag-grid-community';
import { Subscription } from 'rxjs';
import { DialogData, Label, Pc, Snapshot, StorageDrive, VolumeInfo } from '../../models/models';
import { SnapshotService } from '../../services/snapshot.service';
import { TabChangeService } from '../../services/tab-change.service';
import { DialogComponent } from '../dialog/dialog.component';
import { LabelDialogComponent, LabelDialogData } from '../label-dialog/label-dialog.component';
import { darkGridTheme } from '../../shared/grid-theme';

@Component({
  selector: 'app-snapshots',
  templateUrl: './snapshots.component.html',
  styleUrl: './snapshots.component.css',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [MatIconModule, AgGridAngular],
})
export class SnapshotsComponent implements OnInit, OnDestroy {
  public snapshots: Snapshot[];
  selectedSnapshot?: Snapshot;
  activeLabelFilter?: Label;
  @Input() tabIndex: number;
  @Output() tabChange = new EventEmitter<number>();

  private tabChangeSubscription: Subscription;
  private subscriptionAllSnapshots: Subscription;
  private subscriptionDelete: Subscription;

  readonly gridTheme = darkGridTheme;

  readonly rowSelection: RowSelectionOptions = {
    mode: 'singleRow',
    checkboxes: false,
    enableClickSelection: true,
  };

  readonly defaultColDef: ColDef<Snapshot> = {
    sortable: true,
    resizable: true,
    suppressMovable: true,
  };

  readonly columnDefs: ColDef<Snapshot>[] = [
    {
      headerName: 'PC',
      valueGetter: (p) => p.data?.volumeInfo?.volume?.storageDrive?.pcs?.[0]?.name ?? '',
      tooltipValueGetter: (p) => {
        const pc = p.data?.volumeInfo?.volume?.storageDrive?.pcs?.[0];
        return pc ? this.getPcTooltip(pc) : '';
      },
      flex: 1,
      minWidth: 110,
    },
    {
      headerName: 'Storage Drive',
      valueGetter: (p) => {
        const volume = p.data?.volumeInfo?.volume;
        return volume ? `${volume.storageDrive?.name ?? ''}`.trim() : '';
      },
      tooltipValueGetter: (p) => {
        const drive = p.data?.volumeInfo?.volume?.storageDrive;
        return drive ? this.getStorageDriveTooltip(drive) : '';
      },
      flex: 1.5,
      minWidth: 160,
    },
    {
      headerName: 'Volume',
      valueGetter: (p) => p.data?.volumeInfo?.volume?.driveLetter ?? '',
      tooltipValueGetter: (p) => (p.data?.volumeInfo ? this.getVolumeInfoTooltip(p.data.volumeInfo) : ''),
      width: 100,
    },
    {
      headerName: 'Created',
      field: 'timestamp',
      width: 170,
      sort: 'desc',
      valueFormatter: (p: ValueFormatterParams<Snapshot>) =>
        p.value ? new Date(p.value).toLocaleString() : '',
    },
    {
      headerName: 'Root Folder',
      valueGetter: (p) => p.data?.rootOnlyFolder?.name ?? p.data?.rootFolder?.name ?? '',
      tooltipValueGetter: (p) => p.data?.rootOnlyFolder?.fullPath ?? p.data?.rootFolder?.fullPath ?? '',
      flex: 2,
      minWidth: 160,
    },
    {
      headerName: 'Size',
      valueGetter: (p) => p.data?.rootOnlyFolder?.size ?? p.data?.rootFolder?.size,
      width: 120,
      type: 'numericColumn',
    },
    {
      headerName: 'Labels',
      sortable: false,
      flex: 1.2,
      minWidth: 140,
      valueGetter: (p) => (p.data?.labels ?? []).map((l) => l.name).join(', '),
      cellRenderer: (params: ICellRendererParams<Snapshot>) => this.renderLabelsCell(params),
    },
    {
      headerName: 'Snapshot Id',
      field: 'id',
      width: 260,
      cellClass: 'snapshot-id-cell',
    },
  ];

  constructor(
    private snapshotService: SnapshotService,
    private tabChangeService: TabChangeService,
    public dialog: MatDialog,
  ) {}

  ngOnInit() {
    this.tabChangeSubscription = this.tabChangeService.tabChanged$.subscribe((index) => {
      if (index === this.tabIndex) {
        this.getAllSnapshots();
      }
    });
    this.getAllSnapshots();
  }

  ngOnDestroy() {
    this.subscriptionAllSnapshots?.unsubscribe();
    this.subscriptionDelete?.unsubscribe();
    this.tabChangeSubscription?.unsubscribe();
  }

  getAllSnapshots() {
    this.selectedSnapshot = undefined;
    this.subscriptionAllSnapshots = this.snapshotService.getAllSnapshots().subscribe({
      next: (result) => {
        // Fetch full snapshot details for each summary to populate table display
        result.forEach((summarySnapshot) => {
          this.snapshotService.getSnapshotById(summarySnapshot.id).subscribe({
            next: (fullSnapshot) => {
              const index = result.indexOf(summarySnapshot);
              if (index !== -1) {
                // The by-id endpoint does not include labels — keep the summary's.
                result[index] = {
                  ...fullSnapshot,
                  labels: fullSnapshot.labels?.length ? fullSnapshot.labels : summarySnapshot.labels,
                };
              }
              // New array reference so the grid picks up the enriched row.
              this.snapshots = [...result];
            },
            error: (error) => {
              console.error(`Error loading full snapshot ${summarySnapshot.id}:`, error);
            },
          });
        });
        this.snapshots = result;
      },
      error: (error) => {
        console.error(error);
      },
    });
  }

  /** Rows shown in the grid, honoring the active label filter. */
  get filteredSnapshots(): Snapshot[] {
    if (!this.snapshots || !this.activeLabelFilter) {
      return this.snapshots;
    }
    const filterId = this.activeLabelFilter.id;
    return this.snapshots.filter((s) => s.labels?.some((l) => l.id === filterId));
  }

  applyLabelFilter(label: Label) {
    this.activeLabelFilter = label;
  }

  clearLabelFilter() {
    this.activeLabelFilter = undefined;
  }

  openLabelDialog() {
    if (!this.selectedSnapshot) return;
    const data: LabelDialogData = {
      snapshotId: this.selectedSnapshot.id,
      snapshotName:
        this.selectedSnapshot.rootOnlyFolder?.name ?? this.selectedSnapshot.rootFolder?.name ?? this.selectedSnapshot.id,
    };
    this.dialog
      .open(LabelDialogComponent, { data, panelClass: 'dark-dialog-panel', autoFocus: false })
      .afterClosed()
      .subscribe((changed) => {
        if (changed) {
          this.getAllSnapshots();
        }
      });
  }

  private renderLabelsCell(params: ICellRendererParams<Snapshot>): HTMLElement {
    const container = document.createElement('div');
    container.className = 'labels-cell';
    (params.data?.labels ?? []).forEach((label) => {
      const color = label.colorHex || '#7986cb';
      const chip = document.createElement('span');
      chip.className = 'label-chip';
      chip.textContent = label.name;
      chip.style.backgroundColor = `${color}33`; // translucent fill
      chip.style.borderColor = color;
      chip.title = `Filter by "${label.name}"`;
      chip.addEventListener('click', (e) => {
        e.stopPropagation();
        this.applyLabelFilter(label);
      });
      container.appendChild(chip);
    });
    return container;
  }

  onSelectionChanged(event: SelectionChangedEvent<Snapshot>) {
    this.selectedSnapshot = event.api.getSelectedRows()[0];
  }

  onRowDoubleClicked(event: RowDoubleClickedEvent<Snapshot>) {
    if (!event.data) return;
    this.tabChange.emit(2);
    this.snapshotService.setSnapshot(event.data);
  }

  deleteSnapshot(id: string) {
    this.subscriptionDelete = this.snapshotService.deleteSnapshot(id).subscribe({
      next: () => {
        this.getAllSnapshots();
      },
      error: (error) => {
        this.showErrorDialog(error);
      },
    });
  }

  showErrorDialog(error: any) {
    const dialogData: DialogData = { title: 'Error', message: error.error };
    this.dialog.open(DialogComponent, { data: dialogData });
    console.error(error);
  }

  getPcTooltip(element: Pc): string {
    return `ID: ${element.id}\r\n
            Machine ID: ${element.machineId}\r\n
            Hardware UUID: ${element.hardwareUuid}\r\n
            Hardware Serial Number: ${element.hardwareSerialNumber}`;
  }

  getStorageDriveTooltip(element: StorageDrive): string {
    return `ID: ${element.id}\r\n
            Name: ${element.name}\r\n
            Device ID: ${element.deviceId}\r\n
            Serial number: ${element.serialNumber}\r\n
            Total size: ${element.totalSize}\r\n
            Description: ${element.description}\r\n
            Media type: ${element.mediaType}\r\n
            Interface type: ${element.interfaceType}`;
  }

  getVolumeInfoTooltip(element: VolumeInfo): string {
    return `ID: ${element.id}\r\n
            Volume name: ${element.volume?.volumeName}\r\n
            Description: ${element.volume?.description}\r\n
            Volume Serial Number: ${element.volume?.volumeSerialNumber}\r\n
            Volume size: ${element.volume?.volumeSize}\r\n
            Free space: ${element.freeSpace}\r\n
            Drive status: ${element.driveStatus}`;
  }
}
