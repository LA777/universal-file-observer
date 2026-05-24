import { Injectable } from '@angular/core';
import { Subject, Observable } from 'rxjs';
import { FileSystemRoot, Folder } from '../models/models'
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
}
