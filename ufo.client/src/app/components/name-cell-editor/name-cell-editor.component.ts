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
   * The extension the typed text is put back together with.
   *
   * The Name column holds a file's stem, because the server splits the extension
   * off into its own column - so the box shows "report" for "report.pdf". The
   * suffix is what makes the difference invisible: the user edits the stem, and
   * everything judged here is judged against the whole name it will become.
   */
  nameSuffix: string;
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
  private nameSuffix = '';
  private originalName = '';
  private isCancelled = false;

  agInit(params: ICellEditorParams<FsItemUi> & NameCellEditorParams): void {
    this.rules = params.rules ?? STRICT_FILE_NAME_RULES;
    this.siblingNames = params.siblingNames ?? (() => []);
    this.nameSuffix = params.nameSuffix ?? '';
    this.isDraft.set(params.data?.isDraft === true);
    this.originalName = params.data?.isDraft ? '' : (params.value ?? '');

    this.name.set(this.originalName);
    this.revalidate();
  }

  /**
   * The grid reads this once editing stops. It is the stem the user typed, not
   * the whole name - reattaching the extension is the caller's job, since only
   * the caller knows which entry it belonged to.
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

    // A file's extension is already outside the box, so the whole of what is in
    // it is what a rename means to change. A folder with a dot in its name is the
    // one case worth being careful about: "v1.2 backup" selects down to the dot,
    // the way VS Code does, rather than losing the rest to the first keystroke.
    const lastDotIndex = this.nameSuffix ? -1 : this.originalName.lastIndexOf('.');

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

    // Judged as the name it will become on disk, extension and all: "report"
    // typed over "notes" in a folder that already holds "report.pdf" is a
    // collision, and the stems alone do not show that.
    this.validationError.set(
      validateFileName(typedName + this.nameSuffix, this.rules, {
        existingNames: this.siblingNames(),
        currentName: this.isDraft() ? undefined : this.originalName + this.nameSuffix,
      }),
    );
  }
}
