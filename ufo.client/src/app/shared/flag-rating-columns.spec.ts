import { ColDef, ValueGetterParams } from 'ag-grid-community';
import {
  FolderDetailsComponent,
  MARK_COLUMN_IDS,
} from '../components/folder-details/folder-details.component';
import { SnapshotComponent } from '../components/snapshot/snapshot.component';

/**
 * The Flag and Rating columns, in both grids that show them.
 *
 * One spec for the two because the requirement is that they agree: the Files
 * panes and the snapshot view should present and order these the same way, and
 * the easiest way for that to drift is for only one of them to be changed.
 *
 * Worth testing despite being column configuration, because the sorting rests on
 * something easy to omit. Both cells are drawn by a renderer and neither column
 * has a `field`, so without a `valueGetter` there is no value to order by and
 * clicking the header does nothing at all - silently, which is the worst way for
 * it to be wrong.
 */
describe('Flag and Rating columns', () => {
  /** Neither grid's column definitions touch its services, so stubs will do. */
  const grids: ReadonlyArray<{ name: string; columns: ColDef<never>[] }> = [
    {
      name: 'Files panes',
      columns: new FolderDetailsComponent({ currentTheme: 'dark' } as never)
        .columnDefs as unknown as ColDef<never>[],
    },
    {
      name: 'Snapshot contents',
      columns: new SnapshotComponent({} as never, { currentTheme: 'dark' } as never)
        .columnDefs as unknown as ColDef<never>[],
    },
  ];

  function columnOf(columns: ColDef<never>[], colId: string): ColDef<never> {
    const column = columns.find(candidate => candidate.colId === colId);
    expect(column).withContext(`column '${colId}'`).toBeDefined();

    return column!;
  }

  /** Runs a column's valueGetter against one row, the way the grid would. */
  function valueFor(column: ColDef<never>, data: unknown): unknown {
    const valueGetter = column.valueGetter;
    expect(typeof valueGetter).withContext('valueGetter must be a function').toBe('function');

    return (valueGetter as (params: ValueGetterParams<never>) => unknown)({
      data,
    } as ValueGetterParams<never>);
  }

  describe('the repainted set', () => {
    // Setting a mark updates the row in place and repaints only these columns.
    // A column left out of the list still has its value changed and simply never
    // redraws, so the marker appears only after a reload rebuilds the listing -
    // which is exactly what happened when the tags column was added.
    const paneColumns = grids[0].columns;

    it('covers every column a mark can change', () => {
      expect(MARK_COLUMN_IDS).toContain('flag');
      expect(MARK_COLUMN_IDS).toContain('rating');
      expect(MARK_COLUMN_IDS).toContain('tags');
    });

    it('names only columns the grid actually has', () => {
      // A stale id repaints nothing and says nothing about it.
      const existingIds = paneColumns.map(column => column.colId).filter(Boolean);

      for (const markColumnId of MARK_COLUMN_IDS) {
        expect(existingIds).withContext(markColumnId).toContain(markColumnId);
      }
    });
  });

  for (const grid of grids) {
    describe(grid.name, () => {
      it('has a dedicated, labelled column for each', () => {
        // A blank header is neither dedicated nor clickable, and the sort
        // indicator has nowhere to appear.
        expect(columnOf(grid.columns, 'flag').headerName).toBe('Flag');
        expect(columnOf(grid.columns, 'rating').headerName).toBe('Rating');
      });

      it('places them before Ext and Size', () => {
        const order = grid.columns.map(column => column.colId ?? column.field ?? column.headerName);

        const flagIndex = order.indexOf('flag');
        const ratingIndex = order.indexOf('rating');
        const extIndex = order.findIndex((_, index) => grid.columns[index].headerName === 'Ext');
        const sizeIndex = order.findIndex((_, index) => grid.columns[index].headerName === 'Size');

        expect(flagIndex).toBeLessThan(extIndex);
        expect(ratingIndex).toBeLessThan(extIndex);
        expect(flagIndex).toBeLessThan(sizeIndex);
        expect(ratingIndex).toBeLessThan(sizeIndex);
        // Flag then Rating, so the two grids read alike.
        expect(flagIndex).toBeLessThan(ratingIndex);
      });

      it('can be sorted by', () => {
        expect(columnOf(grid.columns, 'flag').sortable).toBeTrue();
        expect(columnOf(grid.columns, 'rating').sortable).toBeTrue();
      });

      it('sorts the marked ones to the top on the first click', () => {
        // Ascending first would lead with everything the user did not mark,
        // which is never what they opened the column for.
        expect(columnOf(grid.columns, 'flag').sortingOrder?.[0]).toBe('desc');
        expect(columnOf(grid.columns, 'rating').sortingOrder?.[0]).toBe('desc');
      });

      it('gives the flag column something to order by', () => {
        const flagColumn = columnOf(grid.columns, 'flag');

        expect(valueFor(flagColumn, { isFlagEnabled: true })).toBe(1);
        expect(valueFor(flagColumn, { isFlagEnabled: false })).toBe(0);
        // Absent rather than false: the field is optional on the model, and a
        // listing built before flags loaded simply has nothing there.
        expect(valueFor(flagColumn, {})).toBe(0);
        expect(valueFor(flagColumn, undefined)).toBe(0);
      });

      it('orders unrated items as zero rather than as a missing value', () => {
        const ratingColumn = columnOf(grid.columns, 'rating');

        expect(valueFor(ratingColumn, { rating: 7 })).toBe(7);
        // So they gather at one end of the sort instead of scattering through it.
        expect(valueFor(ratingColumn, { rating: 0 })).toBe(0);
        expect(valueFor(ratingColumn, {})).toBe(0);
        expect(valueFor(ratingColumn, undefined)).toBe(0);
      });
    });
  }
});
