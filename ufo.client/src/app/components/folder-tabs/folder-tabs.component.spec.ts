import { FolderTabsComponent } from './folder-tabs.component';
import { FolderTab } from '../../models/models';

describe('FolderTabsComponent', () => {
  function tabFor(name: string, isLocked = false): FolderTab {
    return {
      id: name,
      folderPath: `/library/${name}`,
      name,
      isLocked,
      history: [`/library/${name}`],
      historyIndex: 0,
    };
  }

  function createComponent(...tabs: FolderTab[]): FolderTabsComponent {
    const component = new FolderTabsComponent();
    component.tabs = tabs;
    component.activeTabId = tabs[0]?.id ?? '';

    return component;
  }

  describe('canClose', () => {
    it('allows closing an ordinary tab when there is more than one', () => {
      const component = createComponent(tabFor('documents'), tabFor('backup'));

      expect(component.canClose(component.tabs[0])).toBeTrue();
    });

    it('refuses to close a locked tab', () => {
      // Locking is how the user said this one is worth keeping. A close button
      // beside that invites throwing it away with one mis-click; unlocking first
      // is the way out, and it is one click.
      const component = createComponent(tabFor('documents', true), tabFor('backup'));

      expect(component.canClose(component.tabs[0])).toBeFalse();
    });

    it('refuses to close the only tab', () => {
      // A panel with no tabs has nowhere to be.
      const component = createComponent(tabFor('documents'));

      expect(component.canClose(component.tabs[0])).toBeFalse();
    });
  });

  describe('lockTitle', () => {
    it('says what the padlock will do, not what it is', () => {
      const component = createComponent(tabFor('documents'));

      expect(component.lockTitle(component.tabs[0])).toContain('Lock');
      expect(component.lockTitle(tabFor('documents', true))).toContain('Unlock');
    });
  });

  describe('events', () => {
    it('reports which tab was acted on', () => {
      const component = createComponent(tabFor('documents'), tabFor('backup'));
      const selected: string[] = [];
      const locked: string[] = [];
      const closed: string[] = [];

      component.tabSelected.subscribe(id => selected.push(id));
      component.lockToggled.subscribe(id => locked.push(id));
      component.tabClosed.subscribe(id => closed.push(id));

      component.tabSelected.emit('backup');
      component.lockToggled.emit('documents');
      component.tabClosed.emit('backup');

      expect(selected).toEqual(['backup']);
      expect(locked).toEqual(['documents']);
      expect(closed).toEqual(['backup']);
    });
  });
});
