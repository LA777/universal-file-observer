import { AllCommunityModule, ModuleRegistry, themeQuartz } from 'ag-grid-community';

ModuleRegistry.registerModules([AllCommunityModule]);

/**
 * Shared dark grey AG Grid theme: even rows (odd zero-based index) slightly
 * lighter, indigo selection/hover matching the app accent color.
 */
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
