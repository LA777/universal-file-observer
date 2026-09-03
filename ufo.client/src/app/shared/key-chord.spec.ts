import { chordFromEvent, eventMatchesChord, describeChord } from './key-chord';

/**
 * The capture box and the panes both go through here. If they disagreed by so
 * much as the order of the modifiers, a shortcut would record perfectly on the
 * Settings page and then never fire, which is the one failure that would look
 * like the whole feature not working.
 */
describe('key chords', () => {
  function keyDown(key: string, modifiers: Partial<KeyboardEventInit> = {}): KeyboardEvent {
    return new KeyboardEvent('keydown', { key, ...modifiers });
  }

  describe('chordFromEvent', () => {
    it('reads a bare key', () => {
      expect(chordFromEvent(keyDown('F5'))).toBe('F5');
      expect(chordFromEvent(keyDown('Delete'))).toBe('Delete');
    });

    it('writes modifiers in one fixed order', () => {
      // The order is arbitrary but must be the same on both sides, so it is
      // pinned here rather than left to whatever the event happens to report.
      const chord = chordFromEvent(keyDown('S', { ctrlKey: true, shiftKey: true, altKey: true }));

      expect(chord).toBe('Ctrl+Alt+Shift+S');
    });

    it('upper-cases a letter so Shift is recorded once, not twice', () => {
      // The Shift is already a modifier. Recording it in the key as well would
      // produce a chord that can never match anything.
      expect(chordFromEvent(keyDown('a', { ctrlKey: true }))).toBe('Ctrl+A');
      expect(chordFromEvent(keyDown('A', { ctrlKey: true, shiftKey: true }))).toBe('Ctrl+Shift+A');
    });

    it('names the space bar rather than storing a blank', () => {
      expect(chordFromEvent(keyDown(' '))).toBe('Space');
    });

    it('refuses a modifier held on its own', () => {
      // Bound to Shift alone, an action would fire on every capital letter typed.
      expect(chordFromEvent(keyDown('Shift', { shiftKey: true }))).toBeNull();
      expect(chordFromEvent(keyDown('Control', { ctrlKey: true }))).toBeNull();
      expect(chordFromEvent(keyDown('Alt', { altKey: true }))).toBeNull();
      expect(chordFromEvent(keyDown('Meta', { metaKey: true }))).toBeNull();
    });
  });

  describe('eventMatchesChord', () => {
    it('matches what capture produced', () => {
      expect(eventMatchesChord(keyDown('F5'), 'F5')).toBeTrue();
      expect(eventMatchesChord(keyDown('C', { ctrlKey: true }), 'Ctrl+C')).toBeTrue();
    });

    it('does not match when a modifier is missing or extra', () => {
      // F5 and Ctrl+F5 are different shortcuts, and treating them as one would
      // fire Copy on a browser hard-reload.
      expect(eventMatchesChord(keyDown('F5', { ctrlKey: true }), 'F5')).toBeFalse();
      expect(eventMatchesChord(keyDown('F5'), 'Ctrl+F5')).toBeFalse();
    });

    it('never matches an empty chord', () => {
      // An unset slot must not swallow keypresses.
      expect(eventMatchesChord(keyDown('F5'), '')).toBeFalse();
    });

    it('ignores case, so a stored chord survives being written either way', () => {
      expect(eventMatchesChord(keyDown('C', { ctrlKey: true }), 'ctrl+c')).toBeTrue();
    });
  });

  describe('describeChord', () => {
    it('says None for an unset slot', () => {
      expect(describeChord('')).toBe('None');
    });

    it('shortens the names people do not write out', () => {
      expect(describeChord('Delete')).toBe('Del');
      expect(describeChord('Escape')).toBe('Esc');
      expect(describeChord('ArrowUp')).toBe('↑');
      expect(describeChord('Alt+ArrowLeft')).toBe('Alt+←');
    });

    it('leaves an ordinary key alone', () => {
      expect(describeChord('F5')).toBe('F5');
      expect(describeChord('Ctrl+Shift+N')).toBe('Ctrl+Shift+N');
    });
  });

  it('round-trips every default in the shipped set', () => {
    // The defaults are the one set guaranteed to be in use, so a chord among
    // them that capture cannot reproduce would break a shortcut nobody touched.
    const shippedDefaults = ['F2', 'F5', 'F6', 'F7', 'F8', 'Delete', 'Alt+ArrowLeft', 'Alt+ArrowRight', 'Alt+ArrowUp'];

    for (const chord of shippedDefaults) {
      const parts = chord.split('+');
      const event = keyDown(parts[parts.length - 1], {
        altKey: parts.includes('Alt'),
        ctrlKey: parts.includes('Ctrl'),
        shiftKey: parts.includes('Shift'),
      });

      expect(eventMatchesChord(event, chord)).withContext(chord).toBeTrue();
    }
  });
});
