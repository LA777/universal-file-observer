import { Injectable } from '@angular/core';
import { Subject, Observable } from 'rxjs';
import { Snapshot } from '../models/models'
import { HttpClient, HttpHeaders } from '@angular/common/http';

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

  constructor(private http: HttpClient) { }

  setSnapshot(snapshot: Snapshot) {
    this.snapshotSubject.next(snapshot);
    console.log("setSnapshot SnapshotService - " + snapshot.id);
  }

  createSnapshot(path: string): Observable <string> {
    return this.http.post<string>('/api/snapshot/create', { path: path }, { headers: this.httpHeaders });
  }

  getAllSnapshots(): Observable <Snapshot[]> {
    return this.http.get<Snapshot[]>('/api/snapshot/all');
  }

  getSnapshotById(id: string): Observable <Snapshot> {
    return this.http.get<Snapshot>(`/api/snapshot/${id}`);
  }

  getLatestSnapshot(): Observable <Snapshot> {
    return this.http.get<Snapshot>('/api/snapshot/latest');
  }

  deleteSnapshot(id: string): Observable <string> {
    return this.http.delete<string>(`/api/snapshot/delete/${id}`);
  }
}
