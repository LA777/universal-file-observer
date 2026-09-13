import { Component, OnDestroy, OnInit, ViewChild, ChangeDetectionStrategy } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTree, MatTreeModule, MatTreeNestedDataSource } from '@angular/material/tree';
import { AgGridAngular } from 'ag-grid-angular';
import { ColDef, ICellRendererParams, ValueFormatterParams } from 'ag-grid-community';
import { Subscription } from 'rxjs';
import { File, Folder, Snapshot, Tag } from '../../models/models';
import { SnapshotService } from '../../services/snapshot.service';
import { gridThemeFor } from '../../shared/grid-theme';
import { ThemeService } from '../../services/theme.service';

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

  // A getter, not a field: the grid then follows the active theme without
  // this component having to subscribe. gridThemeFor returns one of two
  // module-level constants, so the binding only actually changes on a switch.
  get gridTheme() { return gridThemeFor(this.themeService.currentTheme); }

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
      // The flag as it stood when this snapshot was taken, not as it stands now.
      // A snapshot is a record of a moment, and flagging something today does
      // not change what a snapshot made yesterday says about it.
      headerName: 'Flag',
      colId: 'flag',
      width: 72,
      sortable: true,
      resizable: false,
      headerTooltip: 'Flagged when this snapshot was taken',
      // Without a value there is nothing to order by, and clicking the header
      // would appear to do nothing at all: the cell is drawn by a renderer, so
      // the column has no field of its own to sort on.
      valueGetter: (params) => (params.data?.isFlagEnabled ? 1 : 0),
      // Flagged first on the first click. Ascending would lead with everything
      // the user did not mark, which is never what they opened the column for.
      sortingOrder: ['desc', 'asc', null],
      cellRenderer: (params: ICellRendererParams<File>) => this.renderFlagCell(params),
    },
    {
      // The rating as at capture, like the flag beside it.
      headerName: 'Rating',
      colId: 'rating',
      width: 86,
      sortable: true,
      resizable: false,
      headerTooltip: 'Rating when this snapshot was taken',
      // Unrated sorts as 0, so it gathers at the bottom of a descending sort
      // rather than being scattered through it as a missing value would be.
      valueGetter: (params) => params.data?.rating ?? 0,
      sortingOrder: ['desc', 'asc', null],
      cellRenderer: (params: ICellRendererParams<File>) => this.renderRatingCell(params),
    },
    {
      headerName: 'Tags',
      colId: 'tags',
      width: 160,
      sortable: true,
      resizable: true,
      headerTooltip: 'Tags when this snapshot was taken',
      // Sorted on the joined names, so ordering by this column gathers rows
      // carrying the same tags together instead of doing nothing at all.
      valueGetter: (params) =>
        (params.data?.tags ?? []).map((tag: Tag) => tag.name).sort().join(', '),
      cellRenderer: (params: ICellRendererParams<File>) => this.renderTagsCell(params.data?.tags),
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

  constructor(
    private snapshotService: SnapshotService,
    private themeService: ThemeService,
  ) {}

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

  /**
   * The flag marker: shown only when set, so the eye finds the marked few rather
   * than reading a column of icons standing for "no".
   */
  private renderFlagCell(params: ICellRendererParams<File>): HTMLElement {
    const container = document.createElement('span');

    if (!params.data?.isFlagEnabled) {
      return container;
    }

    container.className = 'material-icons flag-marker';
    container.textContent = 'flag';
    container.title = 'Flagged when this snapshot was taken';

    return container;
  }

  /** Star plus number, or nothing when the item was unrated at capture. */
  private renderRatingCell(params: ICellRendererParams<File>): HTMLElement {
    const container = document.createElement('span');
    const rating = params.data?.rating ?? 0;

    if (!rating) {
      return container;
    }

    container.className = 'rating-marker';
    container.title = `Rated ${rating} of 10 when this snapshot was taken`;

    const star = document.createElement('span');
    star.className = 'material-icons rating-star';
    star.textContent = 'star';
    container.appendChild(star);

    const value = document.createElement('span');
    value.textContent = String(rating);
    container.appendChild(value);

    return container;
  }


  /**
   * Tag chips: the colour, and the name when there is room.
   *
   * Sorted by name so a row's tags do not reorder between listings, and the
   * column sorts on the joined names so tagging groups rows together.
   */
  private renderTagsCell(tags: Tag[] | undefined): HTMLElement {
    const container = document.createElement('span');

    if (!tags?.length) {
      return container;
    }

    container.className = 'tag-chips';

    for (const tag of tags) {
      const chip = document.createElement('span');
      chip.className = 'tag-chip';
      chip.title = tag.name;

      const swatch = document.createElement('span');
      swatch.className = 'tag-chip-swatch';
      swatch.style.backgroundColor = tag.colorHex;
      chip.appendChild(swatch);

      // textContent, never innerHTML: a tag name is the user's text and must
      // never be parsed as markup on its way onto the page.
      const label = document.createElement('span');
      label.textContent = tag.name;
      chip.appendChild(label);

      container.appendChild(chip);
    }

    return container;
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
