import { Component, OnDestroy, OnInit, ViewChild, ChangeDetectionStrategy } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTree, MatTreeModule, MatTreeNestedDataSource } from '@angular/material/tree';
import { AgGridAngular } from 'ag-grid-angular';
import { ColDef, ICellRendererParams, ValueFormatterParams } from 'ag-grid-community';
import { Subscription } from 'rxjs';
import { File, Folder, Snapshot } from '../../models/models';
import { SnapshotService } from '../../services/snapshot.service';
import { darkGridTheme } from '../../shared/grid-theme';

@Component({
  selector: 'app-snapshot',
  templateUrl: './snapshot.component.html',
  styleUrl: './snapshot.component.css',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [MatTreeModule, MatIconModule, AgGridAngular],
})
export class SnapshotComponent implements OnInit, OnDestroy {
  snapshot?: Snapshot;
  dataSource = new MatTreeNestedDataSource<Folder>();
  clickedNode: Folder | null = null;
  folderData: File[] = [];

  @ViewChild('tree') tree: MatTree<Folder>;

  private snapshotSubscription?: Subscription;

  readonly gridTheme = darkGridTheme;

  childrenAccessor = (node: Folder) => node.childFolders ?? [];
  hasChild = (_: number, node: Folder) => !!node.childFolders && node.childFolders.length > 0;

  readonly defaultColDef: ColDef<File> = {
    sortable: true,
    resizable: true,
    suppressMovable: true,
  };

  readonly columnDefs: ColDef<File>[] = [
    {
      headerName: 'Name',
      field: 'name',
      flex: 1,
      minWidth: 220,
      cellRenderer: (params: ICellRendererParams<File>) => this.renderNameCell(params),
    },
    {
      headerName: 'Ext',
      field: 'fileExtension',
      width: 90,
      cellClass: 'ag-right-aligned-cell',
      headerClass: 'ag-right-aligned-header',
    },
    {
      headerName: 'Size',
      field: 'size',
      width: 110,
      type: 'numericColumn',
      valueFormatter: (p: ValueFormatterParams<File>) =>
        p.value == null ? '' : this.formatSize(p.value),
    },
    {
      headerName: 'Hidden',
      field: 'isHidden',
      width: 90,
      valueFormatter: (p: ValueFormatterParams<File>) => (p.value ? 'Yes' : ''),
    },
  ];

  constructor(private snapshotService: SnapshotService) {}

  ngOnInit() {
    this.getLatestSnapshot();

    this.snapshotSubscription = this.snapshotService.snapshot$.subscribe((snapshot) => {
      this.snapshot = undefined;
      this.getSnapshotById(snapshot.id);
    });
  }

  ngOnDestroy() {
    this.snapshotSubscription?.unsubscribe();
  }

  getLatestSnapshot() {
    this.snapshotService.getLatestSnapshot().subscribe({
      next: (result) => this.applySnapshot(result),
      error: (error) => console.error(error),
    });
  }

  getSnapshotById(id: string) {
    this.snapshotService.getSnapshotById(id).subscribe({
      next: (result) => this.applySnapshot(result),
      error: (error) => console.error(error),
    });
  }

  private applySnapshot(result: Snapshot) {
    this.snapshot = result;
    if (result.rootFolder) {
      this.dataSource.data = [result.rootFolder];
      this.folderData = result.rootFolder.files ?? [];
      this.clickedNode = result.rootFolder;
      // ViewChild is set once the view renders; expand on the next tick.
      setTimeout(() => this.tree?.expandAll());
    }
  }

  onNodeClick(node: Folder) {
    this.clickedNode = node;
    this.folderData = node.files ?? [];
  }

  formatTimestamp(timestamp: string): string {
    return timestamp ? new Date(timestamp).toLocaleString() : '';
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

  private renderNameCell(params: ICellRendererParams<File>): HTMLElement {
    const item = params.data as File;
    const container = document.createElement('div');
    container.className = 'name-cell';

    const icon = document.createElement('span');
    icon.className = 'material-icons name-icon';
    icon.textContent = 'description';
    icon.style.color = item.isHidden ? '#8a8a8a' : '#e0e0e0';
    container.appendChild(icon);

    const label = document.createElement('span');
    label.className = 'name-label';
    label.textContent = item.name;
    container.appendChild(label);
    return container;
  }
}
