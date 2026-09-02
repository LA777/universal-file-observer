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
}

/** Answer from GET /api/version - the running build, as major.minor.patch. */
export interface ApplicationVersion {
  version: string;
}
