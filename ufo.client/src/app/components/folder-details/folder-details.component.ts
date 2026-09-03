import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, ChangeDetectionStrategy } from '@angular/core';
import { AgGridAngular } from 'ag-grid-angular';
import {
  CellEditingStoppedEvent,
  ColDef,
  EditableCallbackParams,
  GridApi,
  GridReadyEvent,
  ICellEditorParams,
  ICellRendererParams,
  IRowNode,
  RowDoubleClickedEvent,
  RowHeightParams,
  RowSelectionOptions,
} from 'ag-grid-community';
import { FileNameRules, FsItemUi } from '../../models/models';
import { gridThemeFor } from '../../shared/grid-theme';
import { ThemeService } from '../../services/theme.service';
import { NameCellEditorComponent, NameCellEditorParams } from '../name-cell-editor/name-cell-editor.component';
import { STRICT_FILE_NAME_RULES } from '../../shared/file-name-validation';
import { fileExtensionOf, fullNameOf, isParentRow } from '../../shared/fs-item';

/** A name the user typed over an existing entry's. */
export interface RenameRequest {
  item: FsItemUi;
  /** The new value of the Name cell - a stem for a file, a whole name for a folder. */
  newName: string;
}

/** The name typed into the blank row, and what it should become. */
export interface DraftCommit {
  name: string;
  isFile: boolean;
}

@Component({
  selector: 'app-folder-details',
  standalone: true,
  templateUrl: './folder-details.component.html',
  styleUrl: './folder-details.component.css',
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [AgGridAngular],
})
export class FolderDetailsComponent implements OnChanges {
  @Input() folderData: FsItemUi[] = [];
  @Input() isFilesView: boolean = true;
  @Input() isVideosView: boolean = false;
  @Input() isImagesView: boolean = false;
  /** The host's naming rules, passed down so the name box can apply them. */
  @Input() nameRules: FileNameRules = STRICT_FILE_NAME_RULES;
  /**
   * The blank row waiting for a name, or null when there is not one. Pinned above
   * the listing rather than mixed into it, so that sorting cannot carry a
   * half-typed name off the screen.
   */
  @Input() draftItem: FsItemUi | null = null;

  @Output() rowDoubleClick = new EventEmitter<FsItemUi>();
  @Output() renameRequested = new EventEmitter<RenameRequest>();
  @Output() draftCommitted = new EventEmitter<DraftCommit>();
  /** The blank row was closed with nothing in it: nothing is created. */
  @Output() draftCancelled = new EventEmitter<void>();
  @Output() selectionChanged = new EventEmitter<FsItemUi[]>();

  private gridApi?: GridApi<FsItemUi>;

  constructor(private themeService: ThemeService) {}

  /**
   * The pinned row, as the single-element array the grid wants.
   *
   * A field rebuilt on change rather than a getter: a getter hands the grid a new
   * array on every change-detection pass, and the grid answers by rebuilding the
   * pinned row - which destroys the name box the user is typing into.
   */
  draftRows: FsItemUi[] = [];

  /**
   * Click to select, Ctrl and Shift to extend - what a file browser does, and
   * what Copy, Move and Delete read to find out what they are acting on.
   */
  readonly rowSelection: RowSelectionOptions<FsItemUi> = {
    mode: 'multiRow',
    // The checkbox column would cost width on every row to say what the
    // highlight already says.
    checkboxes: false,
    headerCheckbox: false,
    enableClickSelection: true,
    isRowSelectable: (node: IRowNode<FsItemUi>) => !isParentRow(node.data),
  };

  private videoExtensions = ['.mp4', '.webm', '.ogg', '.mov', '.avi', '.flv', '.mkv', '.m4v'];
  private imageExtensions = ['.jpg', '.jpeg', '.png', '.gif', '.bmp', '.svg', '.webp'];

  /** Compact text row height; media rows are sized per-row in getRowHeight. */
  readonly textRowHeight = 28;

  // A getter, not a field: the grid then follows the active theme without
  // this component having to subscribe. gridThemeFor returns one of two
  // module-level constants, so the binding only actually changes on a switch.
  get gridTheme() { return gridThemeFor(this.themeService.currentTheme); }

  readonly defaultColDef: ColDef<FsItemUi> = {
    sortable: true,
    resizable: true,
    suppressMovable: true,
  };

  readonly columnDefs: ColDef<FsItemUi>[] = [
    {
      headerName: 'Name',
      field: 'name',
      flex: 1,
      minWidth: 240,
      cellRenderer: (params: ICellRendererParams<FsItemUi>) => this.renderNameCell(params),
      editable: (params: EditableCallbackParams<FsItemUi>) => this.isNameEditable(params.node, params.data),
      cellEditor: NameCellEditorComponent,
      // Rendered in the grid's own overlay rather than inside the cell. The cell
      // is 28px tall and clips what it contains, so an inline editor would cut
      // the reason a name was refused off at the row boundary - which is the one
      // part of the name box the user actually has to read.
      cellEditorPopup: true,
      cellEditorPopupPosition: 'over',
      cellEditorParams: (params: ICellEditorParams<FsItemUi>): NameCellEditorParams => ({
        rules: this.nameRules,
        siblingNames: () => this.siblingNames(),
        nameSuffix: fileExtensionOf(params.data),
      }),
      // The grid must not write the typed name into the row. Nothing is renamed
      // until the server says it was, and until then the listing should go on
      // showing what is actually on disk.
      valueSetter: () => false,
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
    },
  ];

  readonly getRowHeight = (params: RowHeightParams<FsItemUi>): number => {
    const item = params.data as FsItemUi | undefined;
    if (!item) return this.textRowHeight;
    if (this.isImageFile(item)) return 316; // 300px thumbnail + padding
    if (this.isVideoFile(item)) return 256; // 240px video player + padding
    return this.textRowHeight;
  };

  ngOnChanges(changes: SimpleChanges): void {
    // The Name cell content and row heights both depend on the active view.
    if (changes['isFilesView'] || changes['isVideosView'] || changes['isImagesView']) {
      this.gridApi?.redrawRows();
      this.gridApi?.resetRowHeights();
    }

    // A blank row that is not being typed into is just an empty line the user has
    // to work out what to do with, so it opens its own name box the moment it
    // appears - after the grid has had a frame to actually put the row on screen.
    if (changes['draftItem']) {
      this.draftRows = this.draftItem ? [this.draftItem] : [];

      if (this.draftItem) {
        setTimeout(() => this.beginEditingDraft());
      }
    }
  }

  onGridReady(event: GridReadyEvent<FsItemUi>): void {
    this.gridApi = event.api;
  }

  onRowDoubleClicked(event: RowDoubleClickedEvent<FsItemUi>): void {
    // A draft is not somewhere to navigate to, and it has no path to open.
    if (event.data && !event.data.isDraft) {
      this.rowDoubleClick.emit(event.data as FsItemUi);
    }
  }

  onSelectionChanged(): void {
    this.selectionChanged.emit(this.gridApi?.getSelectedRows() ?? []);
  }

  /**
   * The name box closed. What that means depends on the row: for the pinned draft
   * it is the answer to whether anything gets created at all, and for an ordinary
   * row it is a rename - or, just as often, a click somewhere else that changed
   * nothing.
   */
  onCellEditingStopped(event: CellEditingStoppedEvent<FsItemUi>): void {
    // The editor answers null for a name it would not let through: an empty
    // draft, text that failed validation, or an Escape.
    const typedName = typeof event.newValue === 'string' ? event.newValue.trim() : '';

    if (event.node.rowPinned === 'top') {
      if (typedName) {
        this.draftCommitted.emit({ name: typedName, isFile: this.draftItem?.isFile === true });
      } else {
        this.draftCancelled.emit();
      }

      return;
    }

    const item = event.data;

    if (item && typedName && typedName !== item.name) {
      this.renameRequested.emit({ item, newName: typedName });
    }
  }

  /**
   * Opens the name box on the one selected row - what F2 and the Rename button
   * reach. Does nothing unless exactly one ordinary row is selected, because
   * there is no such thing as renaming two entries to the same name.
   */
  startRenamingSelected(): void {
    const selectedNodes = (this.gridApi?.getSelectedNodes() ?? [])
      .filter(node => this.isNameEditable(node, node.data));

    const [onlySelectedNode] = selectedNodes;

    if (selectedNodes.length !== 1 || onlySelectedNode?.rowIndex === null || onlySelectedNode?.rowIndex === undefined) {
      return;
    }

    this.gridApi?.startEditingCell({ rowIndex: onlySelectedNode.rowIndex, colKey: 'name' });
  }

  private beginEditingDraft(): void {
    this.gridApi?.startEditingCell({ rowIndex: 0, rowPinned: 'top', colKey: 'name' });
  }

  /**
   * Which rows carry a name worth editing. The parent-folder shortcut is not an
   * entry on disk, and the media views put a picture where the name would be
   * typed - neither is somewhere a name box belongs.
   */
  private isNameEditable(node: IRowNode<FsItemUi> | undefined, item: FsItemUi | undefined): boolean {
    if (!item || isParentRow(item)) {
      return false;
    }

    return node?.rowPinned === 'top' || this.isFilesView;
  }

  /**
   * The full names already in the folder, for the collision check. The row being
   * edited is left in: the editor knows its own name and excludes it, which is
   * what lets a rename that only changes capitalisation through.
   */
  private siblingNames(): string[] {
    return this.folderData
      .filter(item => !isParentRow(item) && !item.isDraft)
      .map(fullNameOf);
  }

  /**
   * DOM-based renderer so file names are never parsed as HTML.
   * Files view: folder/file icon + name. Media views: inline thumbnail or player.
   */
  private renderNameCell(params: ICellRendererParams<FsItemUi>): HTMLElement {
    const item = params.data as FsItemUi;
    const container = document.createElement('div');
    container.className = 'name-cell';

    if (this.isImageFile(item)) {
      const img = document.createElement('img');
      img.src = item.fullPath;
      img.alt = item.name;
      img.className = 'thumbnail-image';
      container.appendChild(img);
    } else if (this.isVideoFile(item)) {
      const video = document.createElement('video');
      video.width = 320;
      video.height = 240;
      video.controls = true;
      video.preload = 'metadata';
      const source = document.createElement('source');
      source.src = this.getVideoFileUri(item);
      source.type = this.getVideoMimeType(item);
      video.appendChild(source);
      container.appendChild(video);
    } else if (!item.isFile || this.isFilesView) {
      const icon = document.createElement('span');
      icon.className = 'material-icons name-icon';
      icon.textContent = item.isFile ? 'description' : 'folder';
      if (item.isFile) {
        icon.style.color = item.isHidden ? '#8a8a8a' : '#e0e0e0';
      } else {
        // Windows Explorer-style yellow folders; hidden ones dimmed.
        icon.style.color = item.isHidden ? '#8a7a45' : '#ffd04c';
      }
      container.appendChild(icon);
    }

    const label = document.createElement('span');
    label.className = 'name-label';
    label.textContent = item.name;
    container.appendChild(label);
    return container;
  }

  isVideoFile(fsItem: FsItemUi): boolean {
    if (!this.isVideosView) return false;
    if (fsItem.fileExtension) {
      return this.videoExtensions.includes(fsItem.fileExtension.toLowerCase());
    }
    return false;
  }

  isImageFile(fsItem: FsItemUi): boolean {
    if (!this.isImagesView) return false;
    if (fsItem.fileExtension) {
      return this.imageExtensions.includes(fsItem.fileExtension.toLowerCase());
    }
    return false;
  }

  getVideoFileUri(fsItem: FsItemUi): string {
    // Relative URL: proxied to the backend in dev, same-origin in production.
    return `/api/video?filePath=${encodeURIComponent(fsItem.fullPath)}`;
  }

  getVideoMimeType(fsItem: FsItemUi): string {
    const lastDotIndex = fsItem.fullPath.lastIndexOf('.');
    if (lastDotIndex === -1) {
      return 'application/octet-stream';
    }
    const extension = fsItem.fullPath.substring(lastDotIndex).toLowerCase();
    switch (extension) {
      case '.3gp': return 'video/3gp2';
      case '.avi': return 'video/x-msvideo';
      case '.mpg':
      case '.mpeg': return 'video/mpeg';
      case '.mp4':
      case '.m4v':
      case '.m4p': return 'video/mp4';
      case '.ogg':
      case '.ogv': return 'video/ogg';
      case '.mov': return 'video/quicktime';
      case '.mkv':
      case '.webm': return 'video/webm';
      default:
        console.warn(`Unknown video extension: ${extension}.`);
        return 'application/octet-stream';
    }
  }
}
