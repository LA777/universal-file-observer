import { Component, Inject, ChangeDetectionStrategy, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatDialog, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { Observable } from 'rxjs';
import { Tag } from '../../models/models';
import { TagsService } from '../../services/tags.service';
import { describeHttpError } from '../../shared/http-error';

/** What the dialog is acting on: the selected paths, and what they are called. */
export interface TagDialogData {
  fullPaths: string[];
  /** For the heading, so the user can see what they are tagging. */
  description: string;
}

/** Whether every, some, or none of the selection carries a tag. */
type TagCoverage = 'all' | 'some' | 'none';

interface TagRow {
  tag: Tag;
  coverage: TagCoverage;
}

/**
 * The tag popup: put tags on a selection, take them off, or make a new one.
 *
 * Deliberately not a tag manager. Renaming, recolouring and deleting a tag reach
 * every item that carries it, which is a different and more destructive job than
 * marking the files in front of you - and it belongs behind its own confirmation
 * rather than one click away from the thing you actually came here to do.
 */
@Component({
  selector: 'app-tag-dialog',
  standalone: true,
  imports: [CommonModule, MatIconModule],
  templateUrl: './tag-dialog.component.html',
  styleUrl: './tag-dialog.component.css',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class TagDialogComponent {
  /** Redrawn from the service after every change, so the rows follow the truth. */
  readonly rows = computed<TagRow[]>(() =>
    this.tagsService.tags().map(tag => ({ tag, coverage: this.coverageOf(tag) })),
  );

  readonly newTagName = signal('');

  /**
   * The colour a new tag gets. Seeded from a small palette rather than left
   * black, so a tag made in a hurry is still distinguishable from the next one.
   */
  readonly newTagColor = signal(TagDialogComponent.suggestColor());

  readonly isSaving = signal(false);
  readonly errorMessage = signal('');

  /** The palette offered for a new tag; any colour is still reachable by typing. */
  readonly paletteColors: readonly string[] = [
    '#e53935', '#fb8c00', '#fdd835', '#43a047',
    '#00acc1', '#1e88e5', '#8e24aa', '#6d4c41',
  ];

  constructor(
    public dialogRef: MatDialogRef<TagDialogComponent>,
    @Inject(MAT_DIALOG_DATA) public data: TagDialogData,
    private tagsService: TagsService,
  ) {}

  get canCreate(): boolean {
    return this.newTagName().trim().length > 0 && !this.isSaving();
  }

  /**
   * Clicking a row applies the tag to everything selected, unless everything
   * already has it - in which case it comes off. One control, and the obvious
   * outcome: a partly-tagged selection becomes fully tagged rather than being
   * half cleared.
   */
  toggle(row: TagRow): void {
    this.apply(row.tag.id, row.coverage !== 'all');
  }

  createAndApply(): void {
    const name = this.newTagName().trim();

    if (!name || this.isSaving()) {
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set('');

    this.tagsService.createTag(name, this.newTagColor()).subscribe({
      next: tag => {
        this.newTagName.set('');
        this.newTagColor.set(TagDialogComponent.suggestColor());
        // Made and put on in one action: the user opened this to tag something,
        // not to add a word to a list.
        this.apply(tag.id, true);
      },
      error: (error: unknown) => this.fail(error, 'create the tag'),
    });
  }

  close(): void {
    this.dialogRef.close();
  }

  private apply(tagId: string, isApplied: boolean): void {
    this.isSaving.set(true);
    this.errorMessage.set('');

    this.tagsService.setTag(tagId, this.data.fullPaths, isApplied).subscribe({
      next: () => this.isSaving.set(false),
      error: (error: unknown) => this.fail(error, isApplied ? 'apply the tag' : 'remove the tag'),
    });
  }

  private fail(error: unknown, action: string): void {
    this.isSaving.set(false);
    this.errorMessage.set(describeHttpError(error, { action }).message);
  }

  private coverageOf(tag: Tag): TagCoverage {
    const carrying = this.data.fullPaths.filter(fullPath =>
      this.tagsService.tagsFor(fullPath).some(candidate => candidate.id === tag.id),
    ).length;

    if (carrying === 0) {
      return 'none';
    }

    return carrying === this.data.fullPaths.length ? 'all' : 'some';
  }

  /** A colour from the palette, so two tags made in a row do not match. */
  private static suggestColor(): string {
    const palette = ['#e53935', '#fb8c00', '#fdd835', '#43a047', '#00acc1', '#1e88e5', '#8e24aa', '#6d4c41'];

    return palette[Math.floor(Math.random() * palette.length)];
  }
}

/**
 * Opens the tag popup with the theming every caller wants, the way
 * openMessageDialog does for the message box.
 */
export function openTagDialog(
  dialog: MatDialog,
  data: TagDialogData,
): Observable<unknown> {
  return dialog
    .open(TagDialogComponent, {
      data,
      panelClass: 'dark-dialog-panel',
      autoFocus: true,
      maxWidth: '90vw',
    })
    .afterClosed();
}
