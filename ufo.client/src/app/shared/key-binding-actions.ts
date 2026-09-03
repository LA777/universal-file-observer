/**
 * The action ids the server publishes, as named constants.
 *
 * Not a copy of the catalogue - the labels, groups and default keys all arrive
 * from the server and are never restated here. These are only the ids, which the
 * panes need in order to say which action a piece of code performs. An id that
 * this build does not know about is simply an action no pane offers to run.
 *
 * Kept in step with KeyBindingActions on the server.
 */
export const KeyBindingActions = {
  rename: 'files.rename',
  createFile: 'files.createFile',
  createFolder: 'files.createFolder',
  copy: 'files.copy',
  move: 'files.move',
  delete: 'files.delete',
  navigateBackward: 'files.navigateBackward',
  navigateForward: 'files.navigateForward',
  navigateUpward: 'files.navigateUpward',
} as const;
