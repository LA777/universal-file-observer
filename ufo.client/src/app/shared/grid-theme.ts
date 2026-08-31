import { AllCommunityModule, ModuleRegistry, themeQuartz } from 'ag-grid-community';
import { Theme } from '../models/models';

ModuleRegistry.registerModules([AllCommunityModule]);

/**
 * AG Grid paints through its own theming API rather than CSS, so the palette in
 * styles.css cannot reach it — these two objects mirror those tokens by hand.
 * Keep them in step with --ufo-* when either side changes.
 */

/** Dark grey grid: even rows (odd zero-based index) slightly lighter, indigo selection/hover. */
export const darkGridTheme = themeQuartz.withParams({
  backgroundColor: '#2e2e2e',
  foregroundColor: '#e0e0e0',
  headerBackgroundColor: '#242424',
  headerTextColor: '#ffffff',
  oddRowBackgroundColor: '#3a3a3a',
  rowHoverColor: '#454f5b',
  selectedRowBackgroundColor: 'rgba(92, 107, 192, 0.35)',
  borderColor: '#4d4d4d',
});

/** The same structure on a light ground. */
export const lightGridTheme = themeQuartz.withParams({
  backgroundColor: '#fafbfc',
  foregroundColor: '#2f333d',
  headerBackgroundColor: '#f1f3f7',
  headerTextColor: '#1a1c22',
  oddRowBackgroundColor: '#f1f3f7',
  rowHoverColor: '#e4e9f3',
  selectedRowBackgroundColor: 'rgba(63, 81, 181, 0.18)',
  borderColor: '#d4d8e0',
});

export const gridThemeFor = (theme: Theme) => (theme === 'light' ? lightGridTheme : darkGridTheme);
