import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

/** What the server answers a deletion with: the count is in the message. */
export interface UserDataDeletionResult {
  message?: string;
}

/**
 * The Settings page's danger zone: deleting one kind of the user's data at a
 * time, or all of it. Every call is scoped by the server to the signed-in user.
 */
@Injectable({ providedIn: 'root' })
export class UserDataService {
  private apiUrl = '/api/userdata';

  constructor(private http: HttpClient) {}

  /** Every snapshot with its tree and machine identity. Labels stay. */
  deleteSnapshots(): Observable<UserDataDeletionResult> {
    return this.http.delete<UserDataDeletionResult>(`${this.apiUrl}/snapshots`);
  }

  /** Every flag, rating and tag on files and folders on disk. The files stay. */
  deleteFileSystemData(): Observable<UserDataDeletionResult> {
    return this.http.delete<UserDataDeletionResult>(`${this.apiUrl}/filesystem`);
  }

  /** The theme, the rebound shortcuts and the locked folder tabs. */
  deleteSettings(): Observable<UserDataDeletionResult> {
    return this.http.delete<UserDataDeletionResult>(`${this.apiUrl}/settings`);
  }

  /** Everything above and the labels, in one transaction. The account remains. */
  deleteAll(): Observable<UserDataDeletionResult> {
    return this.http.delete<UserDataDeletionResult>(`${this.apiUrl}/all`);
  }
}
