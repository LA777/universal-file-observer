import { Injectable } from '@angular/core';
import { Subject } from 'rxjs';
import { Snapshot } from '../models/models'

@Injectable({
  providedIn: 'root',
})

export class SnapshotService {
  private snapshotSubject = new Subject<Snapshot>();
  snapshot$ = this.snapshotSubject.asObservable();

  setSnapshot(snapshot: Snapshot) {
    this.snapshotSubject.next(snapshot);
    console.log("setSnapshot SnapshotService - " + snapshot.guid);
  }
}
