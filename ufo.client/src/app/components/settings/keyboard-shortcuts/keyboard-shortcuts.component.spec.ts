import { KeyboardShortcutsComponent } from './keyboard-shortcuts.component';
import { KeyBinding } from '../../../models/models';

/**
 * Driven directly rather than through TestBed: the capture rules are all
 * component state, and none of them needs an injector to exercise.
 */
describe('KeyboardShortcutsComponent', () => {
  function bindingFor(actionId: string, primaryKey: string, secondaryKey = ''): KeyBinding {
    return {
      actionId,
      label: actionId,
      group: 'File operations',
      primaryKey,
      secondaryKey,
      defaultPrimaryKey: primaryKey,
      defaultSecondaryKey: secondaryKey,
      isDefault: true,
    };
  }

  function createComponent(...bindings: KeyBinding[]): KeyboardShortcutsComponent {
    // The service is only touched by ngOnInit and save(), neither of which these
    // tests call, so a bare cast is honest here rather than a stub pretending to
    // be more.
    const component = new KeyboardShortcutsComponent({} as never);
    component.bindings.set(bindings);

    return component;
  }

  function keyDown(key: string, modifiers: Partial<KeyboardEventInit> = {}): KeyboardEvent {
    return new KeyboardEvent('keydown', { key, cancelable: true, ...modifiers });
  }

  /** A click carrying an element, since arming focuses whatever it came from. */
  function clickOn(element: HTMLElement): Event {
    const event = new MouseEvent('click');
    Object.defineProperty(event, 'currentTarget', { value: element });

    return event;
  }

  describe('capture', () => {
    it('records a chord into the armed slot', () => {
      const component = createComponent(bindingFor('files.copy', 'F5'));

      component.startCapturing(clickOn(document.createElement('button')), 'files.copy', 'primaryKey');
      component.onSlotKeyDown(keyDown('C', { ctrlKey: true }), 'files.copy', 'primaryKey');

      expect(component.bindings()[0].primaryKey).toBe('Ctrl+C');
      // One key is one recording; the slot stops listening afterwards.
      expect(component.isCapturing('files.copy', 'primaryKey')).toBeFalse();
    });

    it('ignores keys once the slot has stopped listening', () => {
      // The slot keeps focus after recording. Swallowing everything would rebind
      // the action to Tab the moment the user tried to leave the table - and
      // there would be no way out of it at all.
      const component = createComponent(bindingFor('files.copy', 'F5'));

      component.startCapturing(clickOn(document.createElement('button')), 'files.copy', 'primaryKey');
      component.onSlotKeyDown(keyDown('C', { ctrlKey: true }), 'files.copy', 'primaryKey');

      const tabEvent = keyDown('Tab');
      component.onSlotKeyDown(tabEvent, 'files.copy', 'primaryKey');

      expect(component.bindings()[0].primaryKey).toBe('Ctrl+C');
      // Left to the browser, so focus can move on.
      expect(tabEvent.defaultPrevented).toBeFalse();
    });

    it('does not record into a slot that is not the armed one', () => {
      const component = createComponent(bindingFor('files.copy', 'F5', 'F9'));

      component.startCapturing(clickOn(document.createElement('button')), 'files.copy', 'primaryKey');
      component.onSlotKeyDown(keyDown('X'), 'files.copy', 'secondaryKey');

      expect(component.bindings()[0].secondaryKey).toBe('F9');
    });

    it('swallows the keys the browser has its own ideas about', () => {
      // A shortcuts page that let F5 through would reload itself instead of
      // recording it.
      const component = createComponent(bindingFor('files.copy', ''));
      component.startCapturing(clickOn(document.createElement('button')), 'files.copy', 'primaryKey');

      const event = keyDown('F5');
      component.onSlotKeyDown(event, 'files.copy', 'primaryKey');

      expect(event.defaultPrevented).toBeTrue();
      expect(component.bindings()[0].primaryKey).toBe('F5');
    });

    it('clears a slot on Backspace and cancels on Escape', () => {
      const component = createComponent(bindingFor('files.copy', 'F5'));

      component.startCapturing(clickOn(document.createElement('button')), 'files.copy', 'primaryKey');
      component.onSlotKeyDown(keyDown('Backspace'), 'files.copy', 'primaryKey');
      expect(component.bindings()[0].primaryKey).toBe('');

      component.startCapturing(clickOn(document.createElement('button')), 'files.copy', 'primaryKey');
      component.onSlotKeyDown(keyDown('Escape'), 'files.copy', 'primaryKey');
      expect(component.bindings()[0].primaryKey).toBe('');
      expect(component.isCapturing('files.copy', 'primaryKey')).toBeFalse();
    });

    it('keeps listening while only a modifier is held', () => {
      const component = createComponent(bindingFor('files.copy', 'F5'));
      component.startCapturing(clickOn(document.createElement('button')), 'files.copy', 'primaryKey');

      component.onSlotKeyDown(keyDown('Shift', { shiftKey: true }), 'files.copy', 'primaryKey');

      // The user is still reaching for the key that goes with it.
      expect(component.isCapturing('files.copy', 'primaryKey')).toBeTrue();
      expect(component.bindings()[0].primaryKey).toBe('F5');
    });

    it('takes focus when armed, since a click alone does not give it everywhere', () => {
      // Safari and Firefox on macOS do not focus a button when it is clicked, and
      // an armed slot that never receives a keydown records nothing at all.
      const component = createComponent(bindingFor('files.copy', 'F5'));
      const button = document.createElement('button');
      document.body.appendChild(button);

      component.startCapturing(clickOn(button), 'files.copy', 'primaryKey');

      expect(document.activeElement).toBe(button);
      button.remove();
    });
  });

  describe('conflicts', () => {
    it('flags a chord doing two jobs', () => {
      const component = createComponent(
        bindingFor('files.copy', 'F5'),
        bindingFor('files.delete', 'F5'),
      );

      expect(component.hasConflicts()).toBeTrue();
      expect(component.isConflicting('F5')).toBeTrue();
    });

    it('does not flag two actions that simply have no key', () => {
      const component = createComponent(bindingFor('files.copy', ''), bindingFor('files.move', ''));

      expect(component.hasConflicts()).toBeFalse();
    });

    it('clears once the clash is resolved from either side', () => {
      const component = createComponent(
        bindingFor('files.copy', 'F5'),
        bindingFor('files.delete', 'F5'),
      );

      component.startCapturing(clickOn(document.createElement('button')), 'files.delete', 'primaryKey');
      component.onSlotKeyDown(keyDown('F8'), 'files.delete', 'primaryKey');

      expect(component.hasConflicts()).toBeFalse();
    });

    it('drops a duplicate rather than calling it a clash with itself', () => {
      const component = createComponent(bindingFor('files.copy', 'F5', ''));

      component.startCapturing(clickOn(document.createElement('button')), 'files.copy', 'secondaryKey');
      component.onSlotKeyDown(keyDown('F5'), 'files.copy', 'secondaryKey');

      // The same chord in both slots is one binding, not two.
      expect(component.bindings()[0].primaryKey).toBe('');
      expect(component.bindings()[0].secondaryKey).toBe('F5');
      expect(component.hasConflicts()).toBeFalse();
    });
  });

  describe('reset', () => {
    it('puts one action back to what the build ships with', () => {
      const component = createComponent({ ...bindingFor('files.copy', 'F5'), primaryKey: 'Ctrl+C' });

      component.resetAction('files.copy');

      expect(component.bindings()[0].primaryKey).toBe('F5');
    });
  });
});
