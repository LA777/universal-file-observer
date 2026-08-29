import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, ChangeDetectionStrategy } from '@angular/core';
import { AgGridAngular } from 'ag-grid-angular';
import {
  ColDef,
  GridApi,
  GridReadyEvent,
  ICellRendererParams,
  RowDoubleClickedEvent,
  RowHeightParams,
} from 'ag-grid-community';
import { FsItemUi } from '../../models/models';
import { darkGridTheme } from '../../shared/grid-theme';

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
  @Output() rowDoubleClick = new EventEmitter<FsItemUi>();

  private gridApi?: GridApi<FsItemUi>;

  private videoExtensions = ['.mp4', '.webm', '.ogg', '.mov', '.avi', '.flv', '.mkv', '.m4v'];
  private imageExtensions = ['.jpg', '.jpeg', '.png', '.gif', '.bmp', '.svg', '.webp'];

  /** Compact text row height; media rows are sized per-row in getRowHeight. */
  readonly textRowHeight = 28;

  readonly gridTheme = darkGridTheme;

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
  }

  onGridReady(event: GridReadyEvent<FsItemUi>): void {
    this.gridApi = event.api;
  }

  onRowDoubleClicked(event: RowDoubleClickedEvent<FsItemUi>): void {
    if (event.data) {
      this.rowDoubleClick.emit(event.data as FsItemUi);
    }
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
