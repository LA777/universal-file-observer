import { validateFileName, STRICT_FILE_NAME_RULES } from './file-name-validation';
import { FileNameRules } from '../models/models';

/**
 * The client half of the naming rules. It exists to answer while the user is
 * still typing, so what matters is that it agrees with FileNameValidator on the
 * server - a rule only one of them enforces is a name that is accepted in the box
 * and refused by the request, or the other way round.
 */
describe('validateFileName', () => {
  /** A Linux host: almost nothing is reserved, and case tells names apart. */
  const posixRules: FileNameRules = {
    invalidCharacters: '\\/',
    reservedNames: [],
    maximumLength: 255,
    rejectsTrailingDotOrSpace: false,
    isCaseSensitive: true,
  };

  /** A Windows host, as the server reports it. */
  const windowsRules: FileNameRules = {
    invalidCharacters: '\\/:*?"<>|',
    reservedNames: ['CON', 'NUL', 'LPT1'],
    maximumLength: 255,
    rejectsTrailingDotOrSpace: true,
    isCaseSensitive: false,
  };

  const noSiblings = { existingNames: [] };

  it('accepts an ordinary name', () => {
    expect(validateFileName('notes.txt', posixRules, noSiblings)).toBeNull();
    expect(validateFileName('report.final.v2.pdf', windowsRules, noSiblings)).toBeNull();
    expect(validateFileName('.gitignore', windowsRules, noSiblings)).toBeNull();
  });

  it('requires a name', () => {
    expect(validateFileName('', posixRules, noSiblings)).toBe('A name is required.');
    expect(validateFileName('   ', posixRules, noSiblings)).toBe('A name is required.');
  });

  it('trims before judging, so trailing spaces are not a name of their own', () => {
    expect(validateFileName('  notes.txt  ', posixRules, noSiblings)).toBeNull();
  });

  it('refuses the relative segments', () => {
    expect(validateFileName('.', posixRules, noSiblings)).toContain('reserved');
    expect(validateFileName('..', posixRules, noSiblings)).toContain('reserved');
  });

  it('refuses a name carrying a path separator', () => {
    // The one check that keeps a name a name: everything downstream joins it to a
    // folder the server has already approved.
    expect(validateFileName('nested/child.txt', posixRules, noSiblings)).toContain("'/'");
    expect(validateFileName('..\\escaped.txt', windowsRules, noSiblings)).toContain("'\\'");
  });

  it('names the character it objected to', () => {
    expect(validateFileName('what?.txt', windowsRules, noSiblings))
      .toBe("A name may not contain the character '?'.");
  });

  it('refuses control characters wherever the host stands on them', () => {
    // Not in either invalid-character list - that list is printable so it can be
    // shown back - so this is the check that catches them.
    expect(validateFileName('bell\u0007name', posixRules, noSiblings))
      .toBe('A name may not contain control characters.');
  });

  it('applies the host rules rather than a lowest common denominator', () => {
    // A colon is an ordinary character on Linux and an impossible one on Windows.
    expect(validateFileName('12:30 notes.txt', posixRules, noSiblings)).toBeNull();
    expect(validateFileName('12:30 notes.txt', windowsRules, noSiblings)).toContain("':'");

    expect(validateFileName('NUL', posixRules, noSiblings)).toBeNull();
    expect(validateFileName('NUL', windowsRules, noSiblings)).toContain('reserves');
    expect(validateFileName('nul.txt', windowsRules, noSiblings)).toContain('reserves');

    expect(validateFileName('trailing.', posixRules, noSiblings)).toBeNull();
    expect(validateFileName('trailing.', windowsRules, noSiblings))
      .toBe('A name may not end with a dot or a space.');
  });

  it('enforces the maximum length', () => {
    expect(validateFileName('a'.repeat(255), posixRules, noSiblings)).toBeNull();
    expect(validateFileName('a'.repeat(256), posixRules, noSiblings)).toContain('255');
  });

  describe('collisions', () => {
    const siblings = { existingNames: ['notes.txt', 'README.md', 'reports'] };

    it('refuses a name already in the folder', () => {
      expect(validateFileName('notes.txt', posixRules, siblings))
        .toBe("'notes.txt' already exists in this folder.");
      expect(validateFileName('reports', posixRules, siblings)).toContain('already exists');
    });

    it('allows a name the entry already has', () => {
      // Otherwise opening the box and pressing Enter would report the entry as
      // colliding with itself.
      expect(validateFileName('notes.txt', posixRules, { ...siblings, currentName: 'notes.txt' }))
        .toBeNull();
    });

    it('follows the host on whether case tells two names apart', () => {
      expect(validateFileName('readme.md', posixRules, siblings)).toBeNull();
      expect(validateFileName('readme.md', windowsRules, siblings)).toContain('already exists');
    });

    it('allows a rename that only changes capitalisation on a case-insensitive host', () => {
      expect(validateFileName('Notes.txt', windowsRules, { ...siblings, currentName: 'notes.txt' }))
        .toBeNull();
    });
  });

  it('falls back to the strict rules before the server has spoken', () => {
    // Wrong in the safe direction: it refuses names some hosts would accept,
    // which the user sees and can react to, rather than accepting names that
    // will fail as a request later.
    expect(validateFileName('what?.txt', STRICT_FILE_NAME_RULES, noSiblings)).toContain("'?'");
    expect(validateFileName('notes.txt', STRICT_FILE_NAME_RULES, noSiblings)).toBeNull();
  });
});
