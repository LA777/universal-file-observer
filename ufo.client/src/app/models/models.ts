export interface FsItem {
  fileExtension: string;
  size?: number;
  sha256Hash: string;
  guid: string;
  name: string;
  isFile: boolean;
  fullPath: string;
  isHidden: boolean;
  parentFolder?: Folder;
}

export interface File extends FsItem {
}

export interface Folder extends FsItem  {
  files: File[];
  childFolders: Folder[];
}

export interface Volume {
  guid: string;
  driveLetter: string;
  volumeName: string;
  description: string;
  volumeSerialNumber: string;
  volumeSize: number;
  storageDrive: StorageDrive;
}

export interface StorageDrive {
  guid: string;
  name: string;
  deviceId: string;
  serialNumber: string;
  totalSize: number;
  description: string;
  mediaType: string;
  interfaceType: string;
  pcs: Pc[]
}

export interface Pc {
  guid: string;
  name: string;
}

export interface Snapshot {
  guid: string;
  timestamp: string;
  rootFolder: Folder;
  volumeInfo: VolumeInfo;
}

export interface VolumeInfo {
  guid: string;
  freeSpace: number;
  driveStatus: string;
  volumeGuid: string;
  volume: Volume;
};

export interface SnapshotData {
  snapshotEntity: Snapshot;
}

export interface FileSystemRoot {
  drives: string[];
  folder: Folder
}

export interface DialogData {
  title: string;
  message: string;
}
