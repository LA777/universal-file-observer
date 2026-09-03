import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { MatDialog, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { DialogData } from '../../models/models';
import { CommonModule } from '@angular/common';
import { Observable, map } from 'rxjs';

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

  /** A popup with a confirm label is a question; without one it is a statement. */
  get isQuestion(): boolean {
    return !!this.data.confirmLabel;
  }

  get cancelLabel(): string {
    return this.data.cancelLabel ?? 'Cancel';
  }

  /** Declining, and the answer for the close button and the backdrop alike. */
  closeDialog(): void {
    this.dialogRef.close(false);
  }

  confirm(): void {
    this.dialogRef.close(true);
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

/**
 * Asks a yes/no question and answers with the choice.
 *
 * The stream emits false for every way of declining - the Cancel button, the
 * close button, Escape, a click on the backdrop - so a caller only ever has to
 * check for true before doing something irreversible.
 *
 * Focus starts on the confirm button rather than being suppressed: the answer to
 * "delete these 12 items?" should be reachable from the keyboard, and Escape is
 * always the way out.
 */
export function openConfirmDialog(dialog: MatDialog, data: DialogData): Observable<boolean> {
  const dialogRef = dialog.open(DialogComponent, {
    data,
    panelClass: 'dark-dialog-panel',
    autoFocus: true,
    maxWidth: '90vw',
  });

  return dialogRef.afterClosed().pipe(map(answer => answer === true));
}
