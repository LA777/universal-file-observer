import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { MatDialog, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { DialogData } from '../../models/models';
import { CommonModule } from '@angular/common';

@Component({
    selector: 'app-dialog',
    templateUrl: './dialog.component.html',
    styleUrls: ['./dialog.component.css'],
    standalone: true,
    changeDetection: ChangeDetectionStrategy.Eager,
    imports: [CommonModule, MatIconModule]
})
export class DialogComponent {
  /** Technical detail starts collapsed: it is for a bug report, not for reading. */
  showDetails = false;

  constructor(
    public dialogRef: MatDialogRef<DialogComponent>,
    @Inject(MAT_DIALOG_DATA) public data: DialogData
  ) { }

  get isError(): boolean {
    return this.data.severity !== 'info';
  }

  closeDialog(): void {
    this.dialogRef.close();
  }
}

/**
 * Opens the message popup with the theming every caller wants.
 *
 * Material renders a dialog on its own prebuilt surface, which stays light whatever
 * theme the app is in; `dark-dialog-panel` is what hands the surface over to the
 * theme tokens. Going through here keeps that off every call site.
 */
export function openMessageDialog(dialog: MatDialog, data: DialogData): MatDialogRef<DialogComponent> {
  return dialog.open(DialogComponent, {
    data,
    panelClass: 'dark-dialog-panel',
    autoFocus: false,
    maxWidth: '90vw',
  });
}
