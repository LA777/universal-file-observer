import { ICellEditorParams } from 'ag-grid-community';
import { NameCellEditorComponent, NameCellEditorParams } from './name-cell-editor.component';
import { FileNameRules, FsItemUi } from '../../models/models';

/**
 * Driven directly rather than through TestBed: everything worth asserting here
 * is `agInit` in, `getValue` out, and the component needs no injector to do it.
 * The one part that does need a DOM - the initial selection - is covered by the
 * separate suite below, which only reads what the component decided.
 */
describe('NameCellEditorComponent', () => {
  const rules: FileNameRules = {
    invalidCharacters: '\\/:*?"<>|',
    reservedNames: [],
    maximumLength: 255,
    rejectsTrailingDotOrSpace: false,
    isCaseSensitive: true,
  };

  function itemFor(name: string, fileExtension: string, isFile: boolean): FsItemUi {
    return {
      id: '',
      name,
      fileExtension,
      isFile,
      sha256Hash: '',
      createdAt: '',
      updatedAt: '',
      isHidden: false,
      fullPath: `/library/${name}${isFile ? fileExtension : ''}`,
      hasParent: true,
      parentFolderPath: '/library',
    };
  }

  /**
   * What the grid hands the editor. `value` is the cell's own contents - a file's
   * stem - while `fullName` is the name on disk; the gap between the two is the
   * whole point of this suite.
   */
  function open(
    item: FsItemUi,
    fullName: string,
    siblingNames: string[] = [],
  ): NameCellEditorComponent {
    const editor = new NameCellEditorComponent();

    editor.agInit({
      data: item,
      value: item.name,
      rules,
      fullName,
      siblingNames: () => siblingNames,
    } as unknown as ICellEditorParams<FsItemUi> & NameCellEditorParams);

    return editor;
  }

  describe('a file', () => {
    it('opens with the extension in the box, not just the stem', () => {
      // The reported bug: the cell shows "report", so the box used to as well,
      // and the extension was not there to be changed.
      const editor = open(itemFor('report', '.pdf', true), 'report.pdf');

      expect(editor.name()).toBe('report.pdf');
    });

    it('gives back the whole name, so the extension can be changed', () => {
      const editor = open(itemFor('notes', '.txt', true), 'notes.txt');

      editor.onInput('notes.md');

      expect(editor.getValue()).toBe('notes.md');
    });

    it('lets the extension be dropped entirely', () => {
      const editor = open(itemFor('LICENCE', '.txt', true), 'LICENCE.txt');

      editor.onInput('LICENCE');

      expect(editor.getValue()).toBe('LICENCE');
    });

    it('checks collisions against the whole name', () => {
      const editor = open(itemFor('notes', '.txt', true), 'notes.txt', ['notes.txt', 'notes.md']);

      // Its own name is not a collision...
      expect(editor.validationError()).toBeNull();

      // ...but the sibling it would become is.
      editor.onInput('notes.md');
      expect(editor.validationError()).toContain('already exists');
    });

    it('still refuses a name the host cannot store', () => {
      const editor = open(itemFor('notes', '.txt', true), 'notes.txt');

      editor.onInput('notes/archived.txt');

      expect(editor.validationError()).toContain("'/'");
      // Nothing is handed back, so nothing upstream asks for the rename.
      expect(editor.getValue()).toBeNull();
    });
  });

  describe('a folder', () => {
    it('opens with its whole name and gives it back unchanged in shape', () => {
      const editor = open(itemFor('reports', '<DIR>', false), 'reports');

      expect(editor.name()).toBe('reports');

      editor.onInput('archived reports');
      expect(editor.getValue()).toBe('archived reports');
    });

    it('does not treat the listing label as an extension', () => {
      // fileExtension carries "<DIR>" for a folder; appending it would produce a
      // name full of characters no host accepts.
      const editor = open(itemFor('v1.2 backup', '<DIR>', false), 'v1.2 backup');

      expect(editor.name()).toBe('v1.2 backup');
      expect(editor.validationError()).toBeNull();
    });
  });

  describe('the blank row', () => {
    function openDraft(isFile: boolean): NameCellEditorComponent {
      const draft = { ...itemFor('', isFile ? '' : '<DIR>', isFile), isDraft: true };

      return open(draft, '');
    }

    it('starts empty and asks for nothing while it stays that way', () => {
      const editor = openDraft(true);

      expect(editor.name()).toBe('');
      // Closing an untouched blank row is how the user says "never mind", so it
      // is not an error to complain about.
      expect(editor.validationError()).toBeNull();
      expect(editor.getValue()).toBeNull();
    });

    it('takes the whole name including any extension typed into it', () => {
      const editor = openDraft(true);

      editor.onInput('notes.txt');

      expect(editor.getValue()).toBe('notes.txt');
    });
  });

  it('gives back nothing after Escape, whatever was typed', () => {
    const editor = open(itemFor('notes', '.txt', true), 'notes.txt');

    editor.onInput('renamed.txt');
    editor.onKeyDown(new KeyboardEvent('keydown', { key: 'Escape' }));

    expect(editor.getValue()).toBeNull();
  });

  it('swallows Enter while the name is bad, so the box stays open', () => {
    const editor = open(itemFor('notes', '.txt', true), 'notes.txt');
    editor.onInput('bad/name.txt');

    const enterEvent = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true });
    editor.onKeyDown(enterEvent);

    expect(enterEvent.defaultPrevented).toBeTrue();
  });

  it('lets Enter through once the name is usable', () => {
    const editor = open(itemFor('notes', '.txt', true), 'notes.txt');
    editor.onInput('good-name.txt');

    const enterEvent = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true });
    editor.onKeyDown(enterEvent);

    expect(enterEvent.defaultPrevented).toBeFalse();
  });
});
