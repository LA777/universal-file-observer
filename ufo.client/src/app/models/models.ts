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
  drives: string[];
  folder: Folder
}

export interface DialogData {
  title: string;
  message: string;
}
