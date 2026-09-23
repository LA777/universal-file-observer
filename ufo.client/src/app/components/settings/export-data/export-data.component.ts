import { Component, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ExportScope, ExportService } from '../../../services/export.service';
import { FileDownloadService } from '../../../services/file-download.service';
import { describeHttpError } from '../../../shared/http-error';

/** One button of the Export section: what it exports and how it is labelled. */
export interface ExportChoice {
  scope: ExportScope;
  label: string;
  /** What is in the file, in the user's terms, shown beside the button. */
  description: string;
}

/**
 * The name the archive is saved under: the scope and the local moment, so two
 * exports in a row do not overwrite each other in a downloads folder.
 *
 * Local time rather than the server's stamp: the file lands on the user's
 * machine, where their clock is the one they will read it against. The moment
 * inside the file (`exportedAt`) is UTC and is the one to trust.
 */
export function exportFileName(scope: ExportScope, moment: Date): string {
  const pad = (value: number) => String(value).padStart(2, '0');
  const date = `${moment.getFullYear()}${pad(moment.getMonth() + 1)}${pad(moment.getDate())}`;
  const time = `${pad(moment.getHours())}${pad(moment.getMinutes())}${pad(moment.getSeconds())}`;

  return `ufo-export-${scope}-${date}-${time}.zip`;
}

/**
 * The Export section of the Settings page: four buttons, each downloading a
 * zip archive holding one JSON file of the user's data.
 *
 * Its own component for the same reason the danger zone is: one block, one
 * style, and SettingsComponent kept to the settings themselves.
 */
@Component({
  selector: 'app-export-data',
  standalone: true,
  imports: [CommonModule, MatIconModule, MatTooltipModule],
  templateUrl: './export-data.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './export-data.component.css'
})
export class ExportDataComponent {
  readonly choices: ExportChoice[] = [
    {
      scope: 'user',
      label: 'Export user data',
      description: 'Your account, theme, keyboard shortcuts and locked folder tabs.'
    },
    {
      scope: 'filesystem',
      label: 'Export file system data',
      description: 'Every flag, rating and tag you have put on files and folders.'
    },
    {
      scope: 'snapshots',
      label: 'Export snapshots',
      description: 'Every snapshot with its whole folder tree and file hashes, and every label.'
    },
    {
      scope: 'all',
      label: 'Export all',
      description: 'All of the above in one file.'
    }
  ];

  /** The export in flight, or null when none is. One at a time. */
  workingScope: ExportScope | null = null;
  message = '';
  errorMessage = '';

  constructor(
    private exportService: ExportService,
    private fileDownloadService: FileDownloadService,
    private changeDetectorRef: ChangeDetectorRef
  ) {}

  get isWorking(): boolean {
    return this.workingScope !== null;
  }

  requestExport(choice: ExportChoice): void {
    if (this.isWorking) {
      return;
    }

    this.workingScope = choice.scope;
    this.message = '';
    this.errorMessage = '';

    this.exportService.export(choice.scope).subscribe({
      next: blob => {
        const fileName = exportFileName(choice.scope, new Date());
        this.fileDownloadService.save(blob, fileName);
        this.workingScope = null;
        this.message = `Saved ${fileName}.`;
        this.changeDetectorRef.markForCheck();
      },
      error: (error: unknown) => {
        this.workingScope = null;
        this.errorMessage = describeHttpError(error, { action: choice.label.toLowerCase() }).message;
        this.changeDetectorRef.markForCheck();
      }
    });
  }
}
