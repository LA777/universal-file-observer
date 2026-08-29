import { Component, Inject, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { forkJoin } from 'rxjs';
import { Label } from '../../models/models';
import { LabelService } from '../../services/label.service';

export interface LabelDialogData {
  snapshotId: string;
  snapshotName: string;
}

interface LabelRow extends Label {
  assigned: boolean;
  editing: boolean;
  editName: string;
  editColor: string;
}

/** Preset palette; free hex input can be added later if needed. */
export const LABEL_COLORS = [
  '#e57373', '#f06292', '#ba68c8', '#7986cb', '#4fc3f7', '#4db6ac',
  '#81c784', '#dce775', '#ffd54f', '#ffb74d', '#a1887f', '#90a4ae',
];

@Component({
  selector: 'app-label-dialog',
  templateUrl: './label-dialog.component.html',
  styleUrl: './label-dialog.component.css',
  standalone: true,
  imports: [FormsModule, MatIconModule, MatDialogModule],
})
export class LabelDialogComponent implements OnInit {
  readonly palette = LABEL_COLORS;

  labels: LabelRow[] = [];
  newName = '';
  newColor = LABEL_COLORS[3];
  loading = true;
  error = '';

  /** True once anything changed, so the caller knows to refresh. */
  private changed = false;

  constructor(
    private labelService: LabelService,
    private dialogRef: MatDialogRef<LabelDialogComponent, boolean>,
    @Inject(MAT_DIALOG_DATA) public data: LabelDialogData,
  ) {}

  ngOnInit() {
    this.load();
  }

  private load() {
    this.loading = true;
    forkJoin({
      all: this.labelService.getAllLabels(),
      assigned: this.labelService.getLabelsBySnapshot(this.data.snapshotId),
    }).subscribe({
      next: ({ all, assigned }) => {
        const assignedIds = new Set(assigned.map((l) => l.id));
        this.labels = all.map((l) => ({
          ...l,
          assigned: assignedIds.has(l.id),
          editing: false,
          editName: l.name,
          editColor: l.colorHex || LABEL_COLORS[3],
        }));
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.error = 'Failed to load labels.';
      },
    });
  }

  createLabel() {
    const name = this.newName.trim();
    if (!name) return;
    this.error = '';
    this.labelService.createLabel(name, this.newColor, [this.data.snapshotId]).subscribe({
      next: () => {
        this.newName = '';
        this.changed = true;
        this.load();
      },
      error: (e) => {
        this.error = e?.error?.[0]?.message ?? 'Failed to create label.';
      },
    });
  }

  toggleAssigned(row: LabelRow) {
    this.error = '';
    const request = row.assigned
      ? this.labelService.removeFromSnapshot(row.id, this.data.snapshotId)
      : this.labelService.addToSnapshot(row.id, this.data.snapshotId);
    request.subscribe({
      next: () => {
        row.assigned = !row.assigned;
        this.changed = true;
      },
      error: () => {
        this.error = `Failed to update assignment for "${row.name}".`;
      },
    });
  }

  startEdit(row: LabelRow) {
    row.editing = true;
    row.editName = row.name;
    row.editColor = row.colorHex || LABEL_COLORS[3];
  }

  saveEdit(row: LabelRow) {
    const name = row.editName.trim();
    if (!name) return;
    this.error = '';
    this.labelService.updateLabel({ id: row.id, name, colorHex: row.editColor }).subscribe({
      next: () => {
        row.name = name;
        row.colorHex = row.editColor;
        row.editing = false;
        this.changed = true;
      },
      error: () => {
        this.error = `Failed to update "${row.name}".`;
      },
    });
  }

  cancelEdit(row: LabelRow) {
    row.editing = false;
  }

  deleteLabel(row: LabelRow) {
    this.error = '';
    this.labelService.deleteLabel(row.id).subscribe({
      next: () => {
        this.changed = true;
        this.load();
      },
      error: () => {
        this.error = `Failed to delete "${row.name}".`;
      },
    });
  }

  close() {
    this.dialogRef.close(this.changed);
  }
}
