import { Component, Input, OnDestroy, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { AgGridAngular } from 'ag-grid-angular';
import {
  ColDef,
  ICellRendererParams,
  RowSelectionOptions,
  SelectionChangedEvent,
} from 'ag-grid-community';
import { forkJoin, Subscription } from 'rxjs';
import { Label, Snapshot } from '../../models/models';
import { LabelService } from '../../services/label.service';
import { SnapshotService } from '../../services/snapshot.service';
import { TabChangeService } from '../../services/tab-change.service';
import { gridThemeFor } from '../../shared/grid-theme';
import { ThemeService } from '../../services/theme.service';
import { LABEL_COLORS } from '../label-dialog/label-dialog.component';

interface LabelRow extends Label {
  snapshots: Snapshot[];
}

@Component({
  selector: 'app-labels',
  templateUrl: './labels.component.html',
  styleUrl: './labels.component.css',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [FormsModule, MatIconModule, AgGridAngular],
})
export class LabelsComponent implements OnInit, OnDestroy {
  @Input() tabIndex: number;

  labelRows: LabelRow[];
  selectedLabel?: LabelRow;
  error = '';

  /** Editor state: closed / creating (editingLabel undefined) / editing. */
  editorOpen = false;
  editingLabel?: LabelRow;
  editorName = '';
  editorColor = LABEL_COLORS[3];

  readonly palette = LABEL_COLORS;
  // A getter, not a field: the grid then follows the active theme without
  // this component having to subscribe. gridThemeFor returns one of two
  // module-level constants, so the binding only actually changes on a switch.
  get gridTheme() { return gridThemeFor(this.themeService.currentTheme); }

  readonly rowSelection: RowSelectionOptions = {
    mode: 'singleRow',
    checkboxes: false,
    enableClickSelection: true,
  };

  readonly defaultColDef: ColDef<LabelRow> = {
    sortable: true,
    resizable: true,
    suppressMovable: true,
  };

  readonly columnDefs: ColDef<LabelRow>[] = [
    {
      headerName: '',
      width: 56,
      sortable: false,
      resizable: false,
      cellRenderer: (params: ICellRendererParams<LabelRow>) => this.renderColorCell(params),
    },
    {
      headerName: 'Name',
      field: 'name',
      flex: 1,
      minWidth: 140,
      sort: 'asc',
    },
    {
      headerName: 'Used',
      valueGetter: (p) => p.data?.snapshots.length ?? 0,
      width: 80,
      type: 'numericColumn',
    },
    {
      headerName: 'Snapshots',
      sortable: false,
      flex: 2.5,
      minWidth: 220,
      valueGetter: (p) =>
        (p.data?.snapshots ?? [])
          .map((s) => s.rootOnlyFolder?.name ?? s.rootFolder?.name ?? s.id)
          .join(', '),
      cellRenderer: (params: ICellRendererParams<LabelRow>) => this.renderSnapshotsCell(params),
    },
  ];

  private tabChangeSubscription: Subscription;

  constructor(
    private labelService: LabelService,
    private snapshotService: SnapshotService,
    private tabChangeService: TabChangeService,
    private themeService: ThemeService,
  ) {}

  ngOnInit() {
    this.tabChangeSubscription = this.tabChangeService.tabChanged$.subscribe((index) => {
      if (index === this.tabIndex) {
        this.load();
      }
    });
    this.load();
  }

  ngOnDestroy() {
    this.tabChangeSubscription?.unsubscribe();
  }

  load() {
    this.selectedLabel = undefined;
    this.error = '';
    forkJoin({
      labels: this.labelService.getAllLabels(),
      snapshots: this.snapshotService.getAllSnapshots(),
    }).subscribe({
      next: ({ labels, snapshots }) => {
        this.labelRows = labels.map((label) => ({
          ...label,
          snapshots: (snapshots ?? []).filter((s) => s.labels?.some((l) => l.id === label.id)),
        }));
      },
      error: (error) => {
        console.error(error);
        this.error = 'Failed to load labels.';
        this.labelRows = [];
      },
    });
  }

  onSelectionChanged(event: SelectionChangedEvent<LabelRow>) {
    this.selectedLabel = event.api.getSelectedRows()[0];
  }

  openCreateEditor() {
    this.editorOpen = true;
    this.editingLabel = undefined;
    this.editorName = '';
    this.editorColor = LABEL_COLORS[3];
  }

  openEditEditor() {
    if (!this.selectedLabel) return;
    this.editorOpen = true;
    this.editingLabel = this.selectedLabel;
    this.editorName = this.selectedLabel.name;
    this.editorColor = this.selectedLabel.colorHex || LABEL_COLORS[3];
  }

  closeEditor() {
    this.editorOpen = false;
    this.editingLabel = undefined;
  }

  saveEditor() {
    const name = this.editorName.trim();
    if (!name) return;
    this.error = '';

    const request = this.editingLabel
      ? this.labelService.updateLabel({ id: this.editingLabel.id, name, colorHex: this.editorColor })
      : this.labelService.createLabel(name, this.editorColor);

    request.subscribe({
      next: () => {
        this.closeEditor();
        this.load();
      },
      error: (e) => {
        this.error = e?.error?.[0]?.message ?? e?.error?.message ?? 'Failed to save label.';
      },
    });
  }

  deleteSelected() {
    if (!this.selectedLabel) return;
    this.error = '';
    this.labelService.deleteLabel(this.selectedLabel.id).subscribe({
      next: () => this.load(),
      error: () => {
        this.error = `Failed to delete "${this.selectedLabel?.name}".`;
      },
    });
  }

  private renderColorCell(params: ICellRendererParams<LabelRow>): HTMLElement {
    const container = document.createElement('div');
    container.className = 'color-cell';
    const dot = document.createElement('span');
    dot.className = 'color-swatch';
    dot.style.backgroundColor = params.data?.colorHex || '#7986cb';
    container.appendChild(dot);
    return container;
  }

  private renderSnapshotsCell(params: ICellRendererParams<LabelRow>): HTMLElement {
    const container = document.createElement('div');
    container.className = 'labels-cell';
    (params.data?.snapshots ?? []).forEach((snapshot) => {
      const chip = document.createElement('span');
      chip.className = 'snapshot-chip';
      chip.textContent =
        snapshot.rootOnlyFolder?.name ?? snapshot.rootFolder?.name ?? snapshot.id;
      const when = snapshot.timestamp ? new Date(snapshot.timestamp).toLocaleString() : '';
      chip.title = `${when}\n${snapshot.id}`;
      container.appendChild(chip);
    });
    return container;
  }
}
