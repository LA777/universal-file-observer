import { Component, ChangeDetectionStrategy, ElementRef, ViewChild, signal } from '@angular/core';
import { ICellEditorAngularComp } from 'ag-grid-angular';
import { ICellEditorParams } from 'ag-grid-community';
import { FileNameRules, FsItemUi } from '../../models/models';
import { validateFileName, STRICT_FILE_NAME_RULES } from '../../shared/file-name-validation';

/** What the grid hands the editor beyond the value it is editing. */
export interface NameCellEditorParams {
  /** The host's naming rules, as the panel last heard them from the server. */
  rules: FileNameRules;
  /**
   * The full names already in the folder. A function rather than an array because
   * the listing is replaced wholesale on every navigation, and the editor is
   * built from a column definition that outlives all of them.
   */
  siblingNames: () => string[];
  /**
   * The name to edit, as the file system holds it.
   *
   * Not the cell's own value: the Name column shows a file's stem, because the
   * server splits the extension off into its own column, so the cell says
   * "report" for "report.pdf". The box has to show the whole thing - an
   * extension the user cannot see is an extension they cannot change, and
   * renaming "notes.txt" to "notes.md" is an ordinary thing to want.
   */
  fullName: string;
}

/**
 * The name field of a row, while it is being edited.
 *
 * Named after the thing it edits rather than the grid concept, and kept apart
 * from FolderDetailsComponent because it owns something that component does not:
 * the half-typed name, which is not a file name yet and may never become one.
 *
 * Validation runs on every keystroke and Enter does nothing while the name is
 * bad, which is VS Code's behaviour - the alternative, letting the edit close and
 * reporting the problem afterwards, throws away what the user typed and makes
 * them start again.
 */
@Component({
  selector: 'app-name-cell-editor',
  standalone: true,
  templateUrl: './name-cell-editor.component.html',
  styleUrl: './name-cell-editor.component.css',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class NameCellEditorComponent implements ICellEditorAngularComp {
  @ViewChild('nameInput') private nameInput?: ElementRef<HTMLInputElement>;

  /** Live text, kept as a signal so the message under it follows every keystroke. */
  readonly name = signal('');

  /** Why the current text cannot be used, or null while it can. */
  readonly validationError = signal<string | null>(null);

  /** True for the blank row New File / New Folder opened, which has no old name. */
  readonly isDraft = signal(false);

  private rules: FileNameRules = STRICT_FILE_NAME_RULES;
  private siblingNames: () => string[] = () => [];
  private originalName = '';
  private isFile = false;
  private isCancelled = false;

  agInit(params: ICellEditorParams<FsItemUi> & NameCellEditorParams): void {
    this.rules = params.rules ?? STRICT_FILE_NAME_RULES;
    this.siblingNames = params.siblingNames ?? (() => []);
    this.isDraft.set(params.data?.isDraft === true);
    this.isFile = params.data?.isFile === true;
    this.originalName = params.data?.isDraft ? '' : (params.fullName ?? '');

    this.name.set(this.originalName);
    this.revalidate();
  }

  /**
   * The grid reads this once editing stops. It is the whole name, extension and
   * all, ready to be sent as it stands.
   *
   * Null for anything that must not become a name - an empty draft, or text that
   * failed validation - because the callers upstream treat a null as "nothing was
   * asked for" and leave the file system alone.
   */
  getValue(): string | null {
    if (this.isCancelled) {
      return null;
    }

    const name = this.name().trim();

    return name && !this.validationError() ? name : null;
  }

  /** Focus, and select the part of the name a rename usually changes. */
  afterGuiAttached(): void {
    const input = this.nameInput?.nativeElement;

    if (!input) {
      return;
    }

    input.focus();

    if (this.isDraft()) {
      return;
    }

    // The extension is in the box now, but a rename usually means changing the
    // part before it, so that is what starts selected - typing replaces the stem
    // and leaves ".pdf" alone, while the extension is still right there to edit.
    // This is VS Code's rule: stem for a file, whole name for a folder, and a
    // dot-file like ".gitignore" has no stem so it is selected entire.
    const lastDotIndex = this.isFile ? this.originalName.lastIndexOf('.') : -1;

    if (lastDotIndex > 0) {
      input.setSelectionRange(0, lastDotIndex);
    } else {
      input.select();
    }
  }

  onInput(value: string): void {
    this.name.set(value);
    this.revalidate();
  }

  /**
   * Keys the grid must not act on while the name box is open.
   *
   * The grid listens above this input, so without stopping these it would commit
   * on an Enter the name is not ready for, or move the selection off the row on
   * an arrow key that was meant to move the caret.
   */
  onKeyDown(event: KeyboardEvent): void {
    switch (event.key) {
      case 'Enter':
        // Refusing to close on an invalid name is the whole point: the user keeps
        // what they typed and the message stays on screen telling them what to fix.
        if (this.validationError()) {
          event.preventDefault();
          event.stopPropagation();
        }
        break;

      case 'Escape':
        // Marked here rather than inferred later, because the grid's own cancel
        // path and a blur that happens to carry an empty box are the same event
        // by the time getValue runs.
        this.isCancelled = true;
        break;

      case 'ArrowLeft':
      case 'ArrowRight':
      case 'Home':
      case 'End':
        event.stopPropagation();
        break;
    }
  }

  private revalidate(): void {
    const typedName = this.name().trim();

    // An empty draft is how the user says "never mind" - it closes with nothing
    // created, so it is not an error to complain about while they are still typing.
    if (this.isDraft() && !typedName) {
      this.validationError.set(null);
      return;
    }

    // Judged as the whole name, which is also what is in the box: "report.pdf"
    // typed into a folder that already holds one is a collision, and the stem
    // on its own would not show that.
    this.validationError.set(
      validateFileName(typedName, this.rules, {
        existingNames: this.siblingNames(),
        currentName: this.isDraft() ? undefined : this.originalName,
      }),
    );
  }
}
