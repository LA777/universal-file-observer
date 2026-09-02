import { Injectable } from '@angular/core';
import { Subject, Observable } from 'rxjs';
import { Snapshot, SnapshotSummary } from '../models/models'
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { AuthService } from './auth.service';

@Injectable({
  providedIn: 'root',
})

export class SnapshotService {
  private snapshotSubject = new Subject<Snapshot>();
  snapshot$ = this.snapshotSubject.asObservable();
  private readonly httpHeaders = new HttpHeaders({
      'Content-Type': 'application/json',
      'accept': '*/*'
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

  setSnapshot(snapshot: Snapshot) {
    this.snapshotSubject.next(snapshot);
    console.log("setSnapshot SnapshotService - " + snapshot.id);
  }

  createSnapshot(path: string): Observable <SnapshotSummary> {
    return this.http.post<SnapshotSummary>(
      '/api/snapshot/create',
      { path: path },
      { headers: this.getAuthHeaders() }
    );
  }

  getAllSnapshots(): Observable <Snapshot[]> {
    return this.http.get<Snapshot[]>(
      '/api/snapshot/all/summary',
      { headers: this.getAuthHeaders() }
    );
  }

  getSnapshotById(id: string): Observable <Snapshot> {
    return this.http.get<Snapshot>(
      `/api/snapshot/${id}`,
      { headers: this.getAuthHeaders() }
    );
  }

  getLatestSnapshot(): Observable <Snapshot> {
    return this.http.get<Snapshot>(
      '/api/snapshot/latest',
      { headers: this.getAuthHeaders() }
    );
  }

  deleteSnapshot(id: string): Observable <string> {
    return this.http.delete<string>(
      `/api/snapshot/delete/${id}`,
      { headers: this.getAuthHeaders() }
    );
  }
}
