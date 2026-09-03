export interface FsItem {
  id: string;
  name: string;
  size?: number;
  sha256Hash: string;
  // userId: string; - not needed in client models, as we will only be dealing with the current user's data
  createdAt: string;
  updatedAt: string;
  isHidden: boolean;
  fullPath: string;
  hasParent: boolean;
  parentFolderPath: string;
}

export interface FsItemUi extends FsItem {
  fileExtension: string;
  isFile: boolean;
  /**
   * A row that is not on disk yet: the blank line New File / New Folder puts at
   * the top of the listing, which becomes a file only once a name is typed into
   * it. Nothing else in the panel treats a draft as an item - it cannot be
   * selected, copied, or navigated into.
   */
  isDraft?: boolean;
}


export interface File extends FsItem {
  fileExtension: string;
  //  snapshots: SnapshotSummary[]; - not needed in client models, as we will get the snapshots for a file when we click on it, not when we load the folder tree
}

export interface Folder extends FsItem {
  files: File[];
  childFolders: Folder[];
  // snapshots: SnapshotSummary[]; - not needed in client models, as we will get the snapshots for a folder when we click on it, not when we load the folder tree
}

/** Mirrors Ufo.Abstractions.UiThemes — these strings are also CSS class suffixes. */
export type Theme = 'light' | 'dark';

export interface UserSettings {
  id: string;
  // userId: string; - not needed in client models, as we will only be dealing with the current user's data
  theme: Theme;
}

/** Mirrors Ufo.Abstractions.CertificateSources. */
export type CertificateSource = 'self-signed' | 'user-supplied';

/**
 * The TLS certificate the server is presenting. Server-wide rather than
 * per-user: one certificate is served to everybody, so only an administrator may
 * read or replace it - there is no flag for that, because obtaining one of these
 * at all means the server already agreed.
 */
export interface ServerCertificate {
  /** False on a deployment that terminates TLS upstream and serves plain HTTP. */
  isConfigured: boolean;
  subject: string;
  thumbprint: string;
  /** Round-trip formatted UTC instants, or empty when nothing is configured. */
  notBefore: string;
  notAfter: string;
  source: CertificateSource | '';
  /** Computed on the server, so it does not depend on the browser's clock. */
  isExpired: boolean;
  updatedAt: string;
}

export interface Label {
  id: string;
  name: string;
  // userId: string; - not needed in client models, as we will only be dealing with the current user's data
  colorHex: string;
  snapshotIds: string[];
}

export interface Pc {
  id: string;
  name: string;
  // userId: string; - not needed in client models, as we will only be dealing with the current user's data
  machineId: string;
  hardwareUuid: string;
  hardwareSerialNumber: string;
}

export interface StorageDrive {
  id: string;
  name: string;
  // userId: string; - not needed in client models, as we will only be dealing with the current user's data
  deviceId: string;
  serialNumber: string;
  totalSize: number;
  description: string;
  mediaType: string;
  interfaceType: string;
  pcs: Pc[];
}

export interface Volume {
  id: string;
  // userId: string; - not needed in client models, as we will only be dealing with the current user's data
  driveLetter: string;
  volumeName: string;
  description: string;
  volumeSerialNumber: string;
  volumeSize: number;
  storageDrive?: StorageDrive;
}

export interface VolumeInfo {
  id: string;
  // userId: string; - not needed in client models, as we will only be dealing with the current user's data
  freeSpace: number;
  driveStatus: string;
  volume?: Volume;
}

export interface SnapshotSummary {
  id: string;
  // userId: string; - not needed in client models, as we will only be dealing with the current user's data
  timestamp: string;
  description?: string;
  labels: Label[];
  volumeInfo?: VolumeInfo;
  rootOnlyFolder?: Folder;
}

export interface Snapshot extends SnapshotSummary {
  rootFolder?: Folder;
}

export interface SnapshotData {
  snapshot: Snapshot;
}

export interface FileSystemRoot {
  /**
   * Top-level locations the user can jump to. Drive letters on Windows
   * ("C:\\"), the configured allowed roots when the server restricts access,
   * and "/" on an unrestricted Linux or macOS host.
   */
  roots: string[];
  folder: Folder
  /** The host's naming rules, so a bad name is caught while it is being typed. */
  nameRules: FileNameRules;
}

/**
 * What the server will accept as a file or folder name.
 *
 * These come from the host rather than being hard-coded here, because they
 * genuinely differ: a colon is a legal character in a Linux file name and an
 * impossible one on Windows, and guessing either way is wrong on the other. The
 * server re-checks every name it is sent - this only exists so the user finds out
 * before they press Enter.
 */
export interface FileNameRules {
  /** Characters a name may not contain, as one string to scan. */
  invalidCharacters: string;
  /** Whole names the host reserves - the Windows device names, or nothing. */
  reservedNames: string[];
  maximumLength: number;
  rejectsTrailingDotOrSpace: boolean;
  /** False where "README.md" and "readme.md" are the same entry. */
  isCaseSensitive: boolean;
}

/** Answer from create and rename: where the entry ended up. */
export interface FileSystemOperationResult {
  path?: string;
  message?: string;
}

/** One entry a copy, move, or delete could not handle. */
export interface FsItemFailure {
  path: string;
  /** The entry's own name, so a message reads without the full path. */
  name: string;
  reason: string;
  /**
   * True when the only obstacle was something already at the destination, which
   * is the one failure the user can resolve by answering a question.
   */
  isConflict: boolean;
}

/**
 * The answer to a copy, move, or delete over several entries.
 *
 * A partial failure is not a failed request - nineteen files copied and one was
 * locked - so this arrives with a 200 and the panel reports the difference.
 */
export interface FsBatchResult {
  succeededCount: number;
  failures: FsItemFailure[];
}

/** Criteria for POST /api/search (indexed snapshot data). */
export interface SearchCriteria {
  query: string;
  includeFiles: boolean;
  includeFolders: boolean;
  extension?: string;
  minSize?: number;
  maxSize?: number;
  dateFrom?: string;
  dateTo?: string;
  snapshotIds?: string[];
  labelIds?: string[];
}

/** One file/folder hit from the indexed search. */
export interface IndexedSearchItem {
  id: string;
  name: string;
  size?: number;
  fileExtension?: string;
  fullPath?: string;
  snapshots?: SnapshotSummary[];
}

export interface IndexedSearchResponse {
  files: IndexedSearchItem[];
  folders: IndexedSearchItem[];
}

/** Criteria for POST /api/filesystem/search (live disk search). */
export interface FileSystemSearchCriteria {
  path: string;
  query: string;
  includeFiles: boolean;
  includeFolders: boolean;
  extension?: string;
  minSize?: number;
  maxSize?: number;
  dateFrom?: string;
  dateTo?: string;
  maxResults?: number;
}

export interface FsSearchResult {
  name: string;
  fullPath: string;
  isFile: boolean;
  size?: number;
  fileExtension?: string;
  modifiedAt: string;
  isHidden: boolean;
}

/** Chooses the popup's icon and accent: a failure reads red, anything else neutral. */
export type DialogSeverity = 'error' | 'info';

export interface DialogData {
  title: string;
  /** The one sentence explaining what happened, in the user's terms. */
  message: string;
  /** What to do about it, when there is something to do. */
  hint?: string;
  /** Technical text (status, URL, raw server response), hidden behind a toggle. */
  details?: string;
  severity?: DialogSeverity;
  /**
   * Set to turn the popup into a question: the label of the button that goes
   * ahead ('Delete', 'Copy'). Left unset, the popup is a statement with one OK.
   */
  confirmLabel?: string;
  /** The label declining it. Defaults to 'Cancel' whenever a question is asked. */
  cancelLabel?: string;
  /** Draws the confirm button as the destructive choice. */
  isDestructive?: boolean;
}

/** Answer from GET /api/version - the running build, as major.minor.patch. */
export interface ApplicationVersion {
  version: string;
}
