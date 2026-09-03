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
   * Replaces one panel's locked tabs. An empty list is how the last one is
   * unlocked, so it is sent rather than skipped.
   */
  save(panelId: string, folderPaths: string[]): Observable<void> {
    // What the panels hold has moved on already; the next reader should ask
    // rather than be handed a list from before this save.
    this.request = undefined;

    return this.http.put<void>('/api/foldertabs', { panelId, folderPaths });
  }
}
