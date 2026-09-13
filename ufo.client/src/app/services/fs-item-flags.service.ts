import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, shareReplay, tap, catchError, throwError } from 'rxjs';

/**
 * The files and folders the user has flagged.
 *
 * Held as one set of paths rather than per-listing, because both panes and every
 * folder they visit are asking the same question and the answer is small - only
 * what the user deliberately marked is in it.
 */
@Injectable({ providedIn: 'root' })
export class FsItemFlagsService {
  /** Flagged paths, for the panes to consult while rendering a listing. */
  readonly flaggedPaths = signal<ReadonlySet<string>>(new Set());

  private request?: Observable<string[]>;

  constructor(private http: HttpClient) {}

  load(): Observable<string[]> {
    this.request ??= this.http.get<string[]>('/api/fsitemflags').pipe(
      tap(paths => this.flaggedPaths.set(new Set(paths ?? []))),
      catchError((error: unknown) => {
        // Dropped before the failure is passed on, so one bad moment does not
        // become the answer for the rest of the session.
        this.request = undefined;

        return throwError(() => error);
      }),
      shareReplay({ bufferSize: 1, refCount: false }),
    );

    return this.request;
  }

  /**
   * Flags or clears a set of paths.
   *
   * Only the paths named change. The set held here is updated from the answer
   * rather than optimistically, so what the panes draw is what the server has.
   */
  setFlags(fullPaths: string[], isFlagEnabled: boolean): Observable<unknown> {
    return this.http.post('/api/fsitemflags', { fullPaths, isFlagEnabled }).pipe(
      tap(() => {
        const updated = new Set(this.flaggedPaths());

        for (const fullPath of fullPaths) {
          if (isFlagEnabled) {
            updated.add(fullPath);
          } else {
            updated.delete(fullPath);
          }
        }

        this.flaggedPaths.set(updated);
      }),
    );
  }

  isFlagged(fullPath: string): boolean {
    return this.flaggedPaths().has(fullPath);
  }

  /**
   * Forgets everything held here, so the next load asks the server again.
   * Called after the user deletes their file-system data: what the panes
   * would otherwise keep drawing no longer exists.
   */
  reset(): void {
    this.request = undefined;
    this.flaggedPaths.set(new Set());
  }
}
