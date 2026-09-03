import { FsItemUi } from '../models/models';

/** The name of the row that walks up a level, which is not an entry on disk. */
export const PARENT_ROW_NAME = '..';

/** What the Ext column shows for a folder, which has no extension of its own. */
export const FOLDER_EXTENSION_LABEL = '<DIR>';

/**
 * Whether a row is the shortcut to the folder above rather than something in
 * this one. It is not on disk, so nothing may rename, copy, move, or delete it.
 */
export function isParentRow(item: FsItemUi | undefined | null): boolean {
  return !!item && !item.isFile && item.name === PARENT_ROW_NAME;
}

/**
 * The extension that belongs to an entry, or an empty string when none does.
 *
 * The listing puts a label rather than an extension in a folder's Ext column, so
 * reading the field on its own would append "&lt;DIR&gt;" to folder names.
 */
export function fileExtensionOf(item: FsItemUi | undefined | null): string {
  return item?.isFile ? (item.fileExtension ?? '') : '';
}

/**
 * The entry's name as the file system holds it.
 *
 * The server splits a file's extension into its own column, so `name` alone is
 * "report" for "report.pdf" - fine to show in a column, wrong for anything that
 * has to match what is on disk.
 */
export function fullNameOf(item: FsItemUi): string {
  return item.name + fileExtensionOf(item);
}
