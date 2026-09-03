import { Injectable } from '@angular/core';
import { Subject, Observable } from 'rxjs';
import {
  FileSystemRoot,
  Folder,
  FileSystemOperationResult,
  FsBatchResult,
} from '../models/models'
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { AuthService } from './auth.service';

@Injectable({
  providedIn: 'root',
})

export class FileService {
  private fileSystemRootSubject = new Subject<FileSystemRoot>();
  fileSystemRoot$ = this.fileSystemRootSubject.asObservable();
  private readonly httpHeaders = new HttpHeaders({
    'Content-Type': 'application/json',
    'accept': 'text/plain'
  });

  constructor(
    private http: HttpClient,
    private authService: AuthService
  ) { }

  /**
   * Get the headers with authorization token
   */
  private getAuthHeaders(): HttpHeaders {
    const token = this.authService.getToken();
    let headers = this.httpHeaders;
    if (token) {
      headers = headers.set('Authorization', `Bearer ${token}`);
    }
    return headers;
  }

  getRoot(): Observable <FileSystemRoot> {
    return this.http.get<FileSystemRoot>('/api/filesystem/root', { 
      headers: this.getAuthHeaders() 
    });
  }

  getFolder(path?: string): Observable <Folder> {
    return this.http.post<Folder>(
      '/api/filesystem/folder',
      { path: path },
      { headers: this.getAuthHeaders() }
    );
  }

  /**
   * Creates one empty file or folder. `name` is a single name segment, never a
   * path - the server rejects anything carrying a separator rather than quietly
   * creating a tree.
   */
  createEntry(parentPath: string, name: string, isFile: boolean): Observable<FileSystemOperationResult> {
    return this.http.post<FileSystemOperationResult>(
      '/api/filesystem/create',
      { parentPath, name, isFile },
      { headers: this.getAuthHeaders() }
    );
  }

  /** Renames one entry, leaving it in the folder it is already in. */
  renameEntry(path: string, newName: string): Observable<FileSystemOperationResult> {
    return this.http.post<FileSystemOperationResult>(
      '/api/filesystem/rename',
      { path, newName },
      { headers: this.getAuthHeaders() }
    );
  }

  /**
   * Copies entries into another folder, each keeping its own name.
   *
   * Answers with a 200 even when some entries failed, because copying twenty
   * files and having one locked is not a failed request - the result says how
   * many landed and names the ones that did not. Left `overwrite` false, an
   * entry already at the destination comes back as a conflict for the caller to
   * put to the user.
   */
  copyEntries(paths: string[], destinationFolderPath: string, overwrite = false): Observable<FsBatchResult> {
    return this.http.post<FsBatchResult>(
      '/api/filesystem/copy',
      { paths, destinationFolderPath, overwrite },
      { headers: this.getAuthHeaders() }
    );
  }

  /** Moves entries into another folder. Reports partial failure exactly as copy does. */
  moveEntries(paths: string[], destinationFolderPath: string, overwrite = false): Observable<FsBatchResult> {
    return this.http.post<FsBatchResult>(
      '/api/filesystem/move',
      { paths, destinationFolderPath, overwrite },
      { headers: this.getAuthHeaders() }
    );
  }

  /**
   * Deletes entries permanently, folders with everything inside them. There is
   * no recycle bin behind this, so the caller confirms before calling.
   */
  deleteEntries(paths: string[]): Observable<FsBatchResult> {
    return this.http.post<FsBatchResult>(
      '/api/filesystem/delete',
      { paths },
      { headers: this.getAuthHeaders() }
    );
  }
}
