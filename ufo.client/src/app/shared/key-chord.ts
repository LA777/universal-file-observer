/**
 * Turning a keypress into the string a binding is stored as, and back into
 * something worth showing a user.
 *
 * One spelling, used by the capture box on the Settings page and by the panes
 * that match a keypress against what was saved. If those two disagreed by so
 * much as the order of the modifiers, a shortcut would record perfectly and then
 * never fire.
 */

/** Modifiers, always written in this order so one chord has one spelling. */
const MODIFIER_ORDER: ReadonlyArray<{ readonly name: string; readonly isHeld: (event: KeyboardEvent) => boolean }> = [
  { name: 'Ctrl', isHeld: event => event.ctrlKey },
  { name: 'Alt', isHeld: event => event.altKey },
  { name: 'Shift', isHeld: event => event.shiftKey },
  { name: 'Meta', isHeld: event => event.metaKey },
];

/** The keys that are only ever part of a chord, never the whole of one. */
const MODIFIER_KEYS = new Set(['Control', 'Alt', 'Shift', 'Meta', 'AltGraph', 'OS']);

/** How a chord is spelled for a reader, where that differs from how it is stored. */
const DISPLAY_NAMES: Readonly<Record<string, string>> = {
  Delete: 'Del',
  Escape: 'Esc',
  ArrowLeft: '←',
  ArrowRight: '→',
  ArrowUp: '↑',
  ArrowDown: '↓',
  ' ': 'Space',
  Space: 'Space',
};

/**
 * The chord a keypress represents, or null when the press is not one yet.
 *
 * Null for a bare modifier: holding Shift is not a shortcut, and capturing it as
 * one would bind an action to every capital letter the user ever types.
 */
export function chordFromEvent(event: KeyboardEvent): string | null {
  if (MODIFIER_KEYS.has(event.key)) {
    return null;
  }

  const parts = MODIFIER_ORDER.filter(modifier => modifier.isHeld(event)).map(modifier => modifier.name);

  parts.push(normaliseKey(event.key));

  return parts.join('+');
}

/**
 * Whether a keypress is the chord in question.
 *
 * Compared as whole strings rather than key-by-key, so the ordering rule above
 * is the only thing either side has to agree on.
 */
export function eventMatchesChord(event: KeyboardEvent, chord: string): boolean {
  if (!chord) {
    return false;
  }

  const pressed = chordFromEvent(event);

  return pressed !== null && pressed.toLowerCase() === chord.toLowerCase();
}

/** The chord as it should read on screen: 'Del', 'Ctrl+↑', or 'None' for empty. */
export function describeChord(chord: string): string {
  if (!chord) {
    return 'None';
  }

  const parts = chord.split('+');
  const finalKey = parts[parts.length - 1];

  return [...parts.slice(0, -1), DISPLAY_NAMES[finalKey] ?? finalKey].join('+');
}

/**
 * The stored spelling of a single key.
 *
 * A letter is upper-cased so that "a" and Shift+"A" do not become two different
 * bindings - the Shift is already recorded as a modifier, and recording it twice
 * would mean a chord that can never match.
 */
function normaliseKey(key: string): string {
  if (key === ' ') {
    return 'Space';
  }

  return key.length === 1 ? key.toUpperCase() : key;
}
