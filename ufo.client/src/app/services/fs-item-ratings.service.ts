import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, shareReplay, tap, catchError, throwError } from 'rxjs';

/** The top of the scale. Zero is unrated rather than the bottom of it. */
export const MAXIMUM_RATING = 10;

/** Every value the picker offers, 1 to 10. Zero is offered separately, as "clear". */
export const RATING_CHOICES: readonly number[] =
  Array.from({ length: MAXIMUM_RATING }, (_, index) => index + 1);

/**
 * The ratings a user has given files and folders.
 *
 * Held as one map from path to rating, for the same reason flags are held as one
 * set: both panes and every folder they visit ask the same question, and the
 * answer is only ever as large as what the user deliberately rated.
 */
@Injectable({ providedIn: 'root' })
export class FsItemRatingsService {
  /** Ratings by path, for the panes to consult while rendering a listing. */
  readonly ratingsByPath = signal<ReadonlyMap<string, number>>(new Map());

  private request?: Observable<Record<string, number>>;

  constructor(private http: HttpClient) {}

  load(): Observable<Record<string, number>> {
    this.request ??= this.http.get<Record<string, number>>('/api/fsitemratings').pipe(
      tap(ratings => this.ratingsByPath.set(new Map(Object.entries(ratings ?? {})))),
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
   * Rates a set of paths, or clears them with zero.
   *
   * The map held here is updated from what was asked for rather than re-read, so
   * both panes agree the moment the server accepts it.
   */
  setRatings(fullPaths: string[], rating: number): Observable<unknown> {
    return this.http.post('/api/fsitemratings', { fullPaths, rating }).pipe(
      tap(() => {
        const updated = new Map(this.ratingsByPath());

        for (const fullPath of fullPaths) {
          if (rating > 0) {
            updated.set(fullPath, rating);
          } else {
            updated.delete(fullPath);
          }
        }

        this.ratingsByPath.set(updated);
      }),
    );
  }

  /** The rating for a path, or 0 when it has none. */
  /**
   * Forgets everything held here, so the next load asks the server again.
   * Called after the user deletes their file-system data.
   */
  reset(): void {
    this.request = undefined;
    this.ratingsByPath.set(new Map());
  }

  ratingFor(fullPath: string): number {
    return this.ratingsByPath().get(fullPath) ?? 0;
  }
}
