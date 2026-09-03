import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, shareReplay, catchError, throwError } from 'rxjs';
import { PersistedFolderTab } from '../models/models';

/**
 * The folder tabs a user has locked.
 *
 * Both panes are answered from one request: they load together, and two calls
 * racing for the same rows on startup is a request wasted on an answer that was
 * already on its way.
 */
@Injectable({ providedIn: 'root' })
export class FolderTabsService {
  private request?: Observable<PersistedFolderTab[]>;

  constructor(private http: HttpClient) {}

  load(): Observable<PersistedFolderTab[]> {
    this.request ??= this.http.get<PersistedFolderTab[]>('/api/foldertabs').pipe(
      catchError((error: unknown) => {
        // The cache is dropped before the failure is passed on, so one bad
        // moment on startup does not become the answer for the whole session.
        this.request = undefined;

        return throwError(() => error);
      }),
      shareReplay({ bufferSize: 1, refCount: false }),
    );

    return this.request;
  }

  /**
   * Locks one tab.
   *
   * One tab per call. An endpoint that replaced the panel's whole set would be
   * driven by what this client believes the other tabs to be - and a client
   * whose load failed believes there are none, so the next padlock click would
   * delete every tab the user had kept.
   */
  lock(panelId: string, folderPath: string): Observable<void> {
    this.request = undefined;

    return this.http.post<void>('/api/foldertabs/lock', { panelId, folderPath });
  }

  unlock(panelId: string, folderPath: string): Observable<void> {
    this.request = undefined;

    return this.http.post<void>('/api/foldertabs/unlock', { panelId, folderPath });
  }
}
