import { SimpleChange, SimpleChanges } from '@angular/core';
import { GetRowIdParams } from 'ag-grid-community';
import { FolderDetailsComponent } from './folder-details.component';
import { FsItemUi } from '../../models/models';

/**
 * What keeps a selection alive across a listing being replaced.
 *
 * The grid throws every row away and builds new ones when its row data changes,
 * and the selection goes with them unless it can recognise a row it has seen
 * before. That is what cost the selection whenever a flag or rating was set.
 */
describe('FolderDetailsComponent row identity', () => {
  function itemFor(name: string, isFile = true): FsItemUi {
    return {
      id: '',
      name,
      fileExtension: isFile ? '.txt' : '<DIR>',
      isFile,
      sha256Hash: '',
      createdAt: '',
      updatedAt: '',
      isHidden: false,
      fullPath: `/library/${name}`,
      hasParent: true,
      parentFolderPath: '/library',
    };
  }

  function createComponent(): FolderDetailsComponent {
    // The theme service is only read by the gridTheme getter, which these tests
    // do not touch, so a stub is honest here.
    return new FolderDetailsComponent({ currentTheme: 'dark' } as never);
  }

  describe('getRowId', () => {
    it('identifies a row by its path', () => {
      const component = createComponent();
      const item = itemFor('report');

      const rowId = component.getRowId({ data: item } as GetRowIdParams<FsItemUi>);

      expect(rowId).toBe('/library/report');
    });

    it('gives the same row the same id across a re-read of the folder', () => {
      // The point of it. Two objects describing one file - which is exactly what
      // a reload produces - must be recognised as the same row, or the selection
      // is lost every time the listing is rebuilt.
      const component = createComponent();

      const before = component.getRowId({ data: itemFor('report') } as GetRowIdParams<FsItemUi>);
      const after = component.getRowId({ data: itemFor('report') } as GetRowIdParams<FsItemUi>);

      expect(after).toBe(before);
    });

    it('tells two different files apart', () => {
      const component = createComponent();

      const first = component.getRowId({ data: itemFor('one') } as GetRowIdParams<FsItemUi>);
      const second = component.getRowId({ data: itemFor('two') } as GetRowIdParams<FsItemUi>);

      expect(first).not.toBe(second);
    });
  });

  describe('when the listing is replaced', () => {
    function changeFor(isFirstChange: boolean): SimpleChanges {
      return {
        folderData: new SimpleChange(undefined, [itemFor('report')], isFirstChange),
      } as unknown as SimpleChanges;
    }

    it('re-announces the selection, so the panel cannot drift out of step', done => {
      // The grid keeps the selection; the panel had cleared its own copy while
      // rebuilding the listing. Without this the buttons sit disabled over rows
      // that plainly look selected.
      const component = createComponent();
      const selected = [itemFor('report')];

      // Stand in for the grid, which these tests do not create.
      (component as unknown as { gridApi: unknown }).gridApi = {
        getSelectedRows: () => selected,
      };

      component.selectionChanged.subscribe(items => {
        expect(items).toEqual(selected);
        done();
      });

      component.ngOnChanges(changeFor(false));
    });

    it('says nothing on the very first listing', () => {
      // Nothing can have been selected yet, and announcing an empty selection
      // before the panel has finished starting up is noise.
      const component = createComponent();
      let emitted = false;

      component.selectionChanged.subscribe(() => (emitted = true));
      component.ngOnChanges(changeFor(true));

      expect(emitted).toBeFalse();
    });
  });
});
