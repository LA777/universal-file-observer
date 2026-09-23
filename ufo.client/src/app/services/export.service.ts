import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

/** The four buttons of the Settings page's Export section, as the server names them. */
export type ExportScope = 'user' | 'filesystem' | 'snapshots' | 'all';

/**
 * The Settings page's Export section: the user's data as a zip archive holding
 * one JSON file. Every call is scoped by the server to the signed-in user.
 */
@Injectable({ providedIn: 'root' })
export class ExportService {
  private apiUrl = '/api/export';

  constructor(private http: HttpClient) {}

  /** The archive's bytes. The caller names the file; see `exportFileName`. */
  export(scope: ExportScope): Observable<Blob> {
    return this.http.get(`${this.apiUrl}/${scope}`, { responseType: 'blob' });
  }
}
