import { Component, Input, OnDestroy, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { AgGridAngular } from 'ag-grid-angular';
import { ColDef, ICellRendererParams, ValueFormatterParams } from 'ag-grid-community';
import { forkJoin, Subscription } from 'rxjs';
import {
  FsSearchResult,
  IndexedSearchItem,
  Label,
  Snapshot,
} from '../../models/models';
import { LabelService } from '../../services/label.service';
import { SearchService } from '../../services/search.service';
import { SnapshotService } from '../../services/snapshot.service';
import { TabChangeService } from '../../services/tab-change.service';
import { darkGridTheme } from '../../shared/grid-theme';

type SearchSource = 'snapshots' | 'labels' | 'filesystem';

/** Unified result row for both search back-ends. */
interface SearchRow {
  isFile: boolean;
  name: string;
  extension: string;
  size?: number;
  date: string;
  location: string;
  labels: Label[];
  snapshots: { id: string; name: string; timestamp?: string }[];
}

@Component({
  selector: 'app-search',
  templateUrl: './search.component.html',
  styleUrl: './search.component.css',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [FormsModule, MatIconModule, AgGridAngular],
})
export class SearchComponent implements OnInit, OnDestroy {
  @Input() tabIndex: number;

  // What
  includeFiles = true;
  includeFolders = true;

  // Criteria
  name = '';
  extension = '';
  sizeMin?: number;
  sizeMax?: number;
  sizeUnit = 1048576; // MB
  readonly sizeUnits = [
    { label: 'B', factor: 1 },
    { label: 'KB', factor: 1024 },
    { label: 'MB', factor: 1048576 },
    { label: 'GB', factor: 1073741824 },
  ];
  dateFrom = '';
  dateTo = '';

  // Where
  source: SearchSource = 'snapshots';
  fsPath = '';
  snapshots: Snapshot[] = [];
  labels: Label[] = [];
  readonly selectedSnapshotIds = new Set<string>();
  readonly selectedLabelIds = new Set<string>();

  rows?: SearchRow[];
  searching = false;
  error = '';

  readonly gridTheme = darkGridTheme;

  readonly defaultColDef: ColDef<SearchRow> = {
    sortable: true,
    resizable: true,
    suppressMovable: true,
  };

  readonly columnDefs: ColDef<SearchRow>[] = [
    {
      headerName: 'Name',
      field: 'name',
      flex: 1.2,
      minWidth: 200,
      cellRenderer: (params: ICellRendererParams<SearchRow>) => this.renderNameCell(params),
    },
    {
      headerName: 'Ext',
      field: 'extension',
      width: 90,
      cellClass: 'ag-right-aligned-cell',
      headerClass: 'ag-right-aligned-header',
    },
    {
      headerName: 'Size',
      field: 'size',
      width: 110,
      type: 'numericColumn',
      valueFormatter: (p: ValueFormatterParams<SearchRow>) =>
        p.value == null ? '' : this.formatSize(p.value),
    },
    {
      headerName: 'Date',
      field: 'date',
      width: 170,
    },
    {
      headerName: 'Snapshot',
      sortable: false,
      flex: 1,
      minWidth: 140,
      valueGetter: (p) => (p.data?.snapshots ?? []).map((s) => s.name).join(', '),
      cellRenderer: (params: ICellRendererParams<SearchRow>) => this.renderSnapshotsCell(params),
    },
    {
      headerName: 'Full Path',
      field: 'location',
      flex: 2,
      minWidth: 200,
      cellClass: 'location-cell',
    },
    {
      headerName: 'Labels',
      sortable: false,
      flex: 1,
      minWidth: 120,
      valueGetter: (p) => (p.data?.labels ?? []).map((l) => l.name).join(', '),
      cellRenderer: (params: ICellRendererParams<SearchRow>) => this.renderLabelsCell(params),
    },
  ];

  private tabChangeSubscription: Subscription;

  constructor(
    private searchService: SearchService,
    private snapshotService: SnapshotService,
    private labelService: LabelService,
    private tabChangeService: TabChangeService,
  ) {}

  ngOnInit() {
    this.tabChangeSubscription = this.tabChangeService.tabChanged$.subscribe((index) => {
      if (index === this.tabIndex) {
        this.loadPickers();
      }
    });
    this.loadPickers();
  }

  ngOnDestroy() {
    this.tabChangeSubscription?.unsubscribe();
  }

  /** Snapshot and label lists for the "Where" pickers. */
  private loadPickers() {
    forkJoin({
      snapshots: this.snapshotService.getAllSnapshots(),
      labels: this.labelService.getAllLabels(),
    }).subscribe({
      next: ({ snapshots, labels }) => {
        this.snapshots = snapshots ?? [];
        this.labels = labels;
      },
      error: (error) => console.error(error),
    });
  }

  toggleSnapshot(id: string) {
    this.selectedSnapshotIds.has(id) ? this.selectedSnapshotIds.delete(id) : this.selectedSnapshotIds.add(id);
  }

  toggleLabel(id: string) {
    this.selectedLabelIds.has(id) ? this.selectedLabelIds.delete(id) : this.selectedLabelIds.add(id);
  }

  snapshotChipName(snapshot: Snapshot): string {
    return snapshot.rootOnlyFolder?.name ?? snapshot.rootFolder?.name ?? snapshot.id;
  }

  search() {
    this.error = '';

    if (!this.includeFiles && !this.includeFolders) {
      this.error = 'Select at least one of Files / Folders.';
      return;
    }
    const name = this.name.trim();
    if (name.length > 0 && name.length < 3) {
      this.error = 'Name must be at least 3 characters.';
      return;
    }
    if (this.source === 'filesystem' && !this.fsPath.trim()) {
      this.error = 'Enter a folder path to search under.';
      return;
    }
    if (this.source === 'labels' && this.selectedLabelIds.size === 0) {
      this.error = 'Select at least one label.';
      return;
    }

    const minSize = this.sizeMin != null && this.sizeMin !== ('' as never) ? this.sizeMin * this.sizeUnit : undefined;
    const maxSize = this.sizeMax != null && this.sizeMax !== ('' as never) ? this.sizeMax * this.sizeUnit : undefined;
    const dateFrom = this.dateFrom ? new Date(this.dateFrom + 'T00:00:00').toISOString() : undefined;
    const dateTo = this.dateTo ? new Date(this.dateTo + 'T23:59:59.999').toISOString() : undefined;
    const extension = this.includeFiles && this.extension.trim() ? this.extension.trim() : undefined;

    if (this.source !== 'filesystem') {
      const snapshotIds = this.source === 'snapshots' ? [...this.selectedSnapshotIds] : [];
      const labelIds = this.source === 'labels' ? [...this.selectedLabelIds] : [];
      const hasCriteria =
        name.length > 0 || extension || minSize != null || maxSize != null || dateFrom || dateTo ||
        snapshotIds.length > 0 || labelIds.length > 0;
      if (!hasCriteria) {
        this.error = 'Enter at least one search criterion.';
        return;
      }

      this.searching = true;
      this.searchService
        .searchIndexed({
          query: name,
          includeFiles: this.includeFiles,
          includeFolders: this.includeFolders,
          extension,
          minSize,
          maxSize,
          dateFrom,
          dateTo,
          snapshotIds,
          labelIds,
        })
        .subscribe({
          next: (response) => {
            this.searching = false;
            this.rows = [
              ...response.folders.map((f) => this.indexedToRow(f, false)),
              ...response.files.map((f) => this.indexedToRow(f, true)),
            ];
          },
          error: (error) => {
            this.searching = false;
            this.error = error?.error ?? 'Search failed.';
          },
        });
      return;
    }

    this.searching = true;
    this.searchService
      .searchFileSystem({
        path: this.fsPath.trim(),
        query: name,
        includeFiles: this.includeFiles,
        includeFolders: this.includeFolders,
        extension,
        minSize,
        maxSize,
        dateFrom,
        dateTo,
      })
      .subscribe({
        next: (results) => {
          this.searching = false;
          this.rows = results.map((r) => ({
            isFile: r.isFile,
            name: r.name,
            extension: r.isFile ? (r.fileExtension ?? '') : '<DIR>',
            size: r.size,
            date: r.modifiedAt ? new Date(r.modifiedAt).toLocaleString() : '',
            location: r.fullPath,
            labels: [],
            snapshots: [],
          }));
        },
        error: (error) => {
          this.searching = false;
          this.error = error?.error ?? 'File system search failed.';
        },
      });
  }

  private indexedToRow(item: IndexedSearchItem, isFile: boolean): SearchRow {
    const firstSnapshot = item.snapshots?.[0];
    const labelById = new Map<string, Label>();
    (item.snapshots ?? []).forEach((s) => (s.labels ?? []).forEach((l) => labelById.set(l.id, l)));
    return {
      isFile,
      name: item.name,
      extension: isFile ? (item.fileExtension ?? '') : '<DIR>',
      size: item.size,
      date: firstSnapshot?.timestamp ? new Date(firstSnapshot.timestamp).toLocaleString() : '',
      location: item.fullPath ?? '',
      labels: [...labelById.values()],
      snapshots: (item.snapshots ?? []).map((s) => ({
        id: s.id,
        // The search response carries only id/timestamp; resolve the display
        // name from the snapshot list already loaded for the "Where" picker.
        name: this.resolveSnapshotName(s.id, s.timestamp),
        timestamp: s.timestamp,
      })),
    };
  }

  private resolveSnapshotName(snapshotId: string, timestamp?: string): string {
    const known = this.snapshots.find((s) => s.id === snapshotId);
    if (known) {
      return this.snapshotChipName(known);
    }
    return timestamp ? new Date(timestamp).toLocaleString() : snapshotId.substring(0, 8);
  }

  private renderSnapshotsCell(params: ICellRendererParams<SearchRow>): HTMLElement {
    const container = document.createElement('div');
    container.className = 'labels-cell';
    (params.data?.snapshots ?? []).forEach((snapshot) => {
      const chip = document.createElement('span');
      chip.className = 'snapshot-chip';
      chip.textContent = snapshot.name;
      const when = snapshot.timestamp ? new Date(snapshot.timestamp).toLocaleString() : '';
      chip.title = `${when}\n${snapshot.id}`;
      container.appendChild(chip);
    });
    return container;
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

  private renderNameCell(params: ICellRendererParams<SearchRow>): HTMLElement {
    const row = params.data as SearchRow;
    const container = document.createElement('div');
    container.className = 'name-cell';

    const icon = document.createElement('span');
    icon.className = 'material-icons name-icon';
    icon.textContent = row.isFile ? 'description' : 'folder';
    icon.style.color = row.isFile ? '#e0e0e0' : '#ffd04c';
    container.appendChild(icon);

    const label = document.createElement('span');
    label.className = 'name-label';
    label.textContent = row.name;
    container.appendChild(label);
    return container;
  }

  private renderLabelsCell(params: ICellRendererParams<SearchRow>): HTMLElement {
    const container = document.createElement('div');
    container.className = 'labels-cell';
    (params.data?.labels ?? []).forEach((label) => {
      const color = label.colorHex || '#7986cb';
      const chip = document.createElement('span');
      chip.className = 'label-chip';
      chip.textContent = label.name;
      chip.style.backgroundColor = `${color}33`;
      chip.style.borderColor = color;
      container.appendChild(chip);
    });
    return container;
  }
}
