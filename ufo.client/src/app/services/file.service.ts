import { Injectable } from '@angular/core';
import { Subject, Observable } from 'rxjs';
import { FileSystemRoot, Folder } from '../models/models'
import { HttpClient, HttpHeaders } from '@angular/common/http';

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

  constructor(private http: HttpClient) { }

  getRoot(): Observable <FileSystemRoot> {
    return this.http.get<FileSystemRoot>('/api/filesystem/root');
  }

  getFolder(path?: string): Observable <Folder> {
        return this.http.post<Folder>('/api/filesystem/folder', { path: path }, { headers: this.httpHeaders });
  }
}
