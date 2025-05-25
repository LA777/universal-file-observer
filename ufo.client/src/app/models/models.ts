export interface FsItem {
  id: string;
  name: string;
  size?: number;
  sha256Hash: string;
  isFile: boolean;
  fullPath: string;
  isHidden: boolean;
  fileExtension: string;
  parentFolder?: Folder;
}

export interface File extends FsItem {
}

export interface Folder extends FsItem  {
  files: File[];
  childFolders: Folder[];
}

export interface Volume {
  id: string;
  driveLetter: string;
  volumeName: string;
  description: string;
  volumeSerialNumber: string;
  volumeSize: number;
  storageDrive: StorageDrive;
}

export interface StorageDrive {
  id: string;
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
  id: string;
  name: string;
}

export interface Snapshot {
  id: string;
  timestamp: string;
  rootFolder: Folder;
  volumeInfo: VolumeInfo;
}

export interface VolumeInfo {
  id: string;
  freeSpace: number;
  driveStatus: string;
  volumeId: string;
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
