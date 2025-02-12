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
      'accept': 'text/plain'
    });

  constructor(private http: HttpClient) { }

  setSnapshot(snapshot: Snapshot) {
    this.snapshotSubject.next(snapshot);
    console.log("setSnapshot SnapshotService - " + snapshot.guid);
  }

  createSnapshot(path: string): Observable <string> {
    return this.http.post<string>('/api/filesystem/folder', { path: path }, { headers: this.httpHeaders });
  }

  getAllSnapshots(): Observable <Snapshot[]> {
    return this.http.get<Snapshot[]>('/api/snapshot/all');
  }
}
