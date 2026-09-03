import { FileNameRules } from '../models/models';

/**
 * The rules to apply before the server has said what its own are.
 *
 * Only reached in the window between a panel being created and its first
 * /api/filesystem/root answer arriving, which is before any name box can be
 * opened. It is deliberately the strict, Windows-shaped set: the cost of being
 * wrong here is refusing a name the host would have accepted, which the user sees
 * and can react to, rather than accepting one it will not, which surfaces later as
 * a failed request.
 */
export const STRICT_FILE_NAME_RULES: FileNameRules = {
  invalidCharacters: '\\/:*?"<>|',
  reservedNames: [],
  maximumLength: 255,
  rejectsTrailingDotOrSpace: true,
  isCaseSensitive: false,
};

/** The names a new one must not collide with, and the one it is allowed to keep. */
export interface FileNameContext {
  /** Every name already in the folder, including the entry being renamed. */
  existingNames: string[];
  /**
   * The name the entry has now. Renaming something to what it is already called
   * is not a collision, and neither is correcting only its capitalisation on a
   * case-insensitive host.
   */
  currentName?: string;
}

/** Control characters, which a paste can carry into an otherwise ordinary name. */
const controlCharacterPattern = /[\u0000-\u001f\u007f]/;

/**
 * Why a name cannot be used, or null when it can.
 *
 * Mirrors FileNameValidator on the server, plus the collision check - the one
 * question the client can answer better, because it is holding the folder listing
 * the server would have to go back to disk for. The server still re-checks
 * everything: this exists so the answer arrives while the user is still typing.
 */
export function validateFileName(
  rawName: string,
  rules: FileNameRules,
  context: FileNameContext,
): string | null {
  const name = rawName.trim();

  if (!name) {
    return 'A name is required.';
  }

  if (name.length > rules.maximumLength) {
    return `A name may be at most ${rules.maximumLength} characters long.`;
  }

  if (name === '.' || name === '..') {
    return "'.' and '..' are reserved - they mean this folder and the one above it.";
  }

  const offendingCharacter = findOffendingCharacter(name, rules.invalidCharacters);
  if (offendingCharacter !== null) {
    return `A name may not contain the character '${offendingCharacter}'.`;
  }

  // Not part of the invalid-character list, because that list is printable so it
  // can be shown back to the user; these have to be caught on their own.
  if (controlCharacterPattern.test(name)) {
    return 'A name may not contain control characters.';
  }

  if (rules.rejectsTrailingDotOrSpace && (name.endsWith('.') || name.endsWith(' '))) {
    return 'A name may not end with a dot or a space.';
  }

  if (isReservedName(name, rules.reservedNames)) {
    return `'${name}' is a name Windows reserves for a device.`;
  }

  if (collidesWithSibling(name, rules, context)) {
    return `'${name}' already exists in this folder.`;
  }

  return null;
}

function findOffendingCharacter(name: string, invalidCharacters: string): string | null {
  for (const character of name) {
    if (invalidCharacters.includes(character)) {
      return character;
    }
  }

  return null;
}

/**
 * Whether the name is one the host reserves. Windows resolves these ahead of the
 * file system whatever extension follows, so only the part before the first dot
 * is compared - "NUL.txt" is as impossible as "NUL".
 */
function isReservedName(name: string, reservedNames: string[]): boolean {
  if (reservedNames.length === 0) {
    return false;
  }

  const dotIndex = name.indexOf('.');
  const stem = (dotIndex < 0 ? name : name.substring(0, dotIndex)).toUpperCase();

  return reservedNames.some(reservedName => reservedName.toUpperCase() === stem);
}

function collidesWithSibling(name: string, rules: FileNameRules, context: FileNameContext): boolean {
  const normalise = (value: string) => (rules.isCaseSensitive ? value : value.toLocaleUpperCase());
  const candidate = normalise(name);

  if (context.currentName !== undefined && normalise(context.currentName) === candidate) {
    return false;
  }

  return context.existingNames.some(existingName => normalise(existingName) === candidate);
}
