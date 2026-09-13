import { Injectable, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, shareReplay, tap, catchError, throwError, map } from 'rxjs';
import { FsItemTags, Tag } from '../models/models';

/**
 * The user's tags, and which files and folders carry them.
 *
 * One service for both halves because they are useless apart: an assignment is a
 * tag id, and an id without its name and colour cannot be drawn.
 */
@Injectable({ providedIn: 'root' })
export class TagsService {
  /** The vocabulary: every tag the user has defined, by name. */
  readonly tags = signal<readonly Tag[]>([]);

  /** Tag ids by path, for the paths that carry any. */
  readonly tagIdsByPath = signal<ReadonlyMap<string, readonly string[]>>(new Map());

  private readonly tagsById = computed(() => new Map(this.tags().map(tag => [tag.id, tag])));

  private request?: Observable<FsItemTags>;

  constructor(private http: HttpClient) {}

  load(): Observable<FsItemTags> {
    this.request ??= this.http.get<FsItemTags>('/api/tags').pipe(
      tap(result => this.adopt(result)),
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
   * Creates a tag, or hands back the one that already has that name.
   *
   * A name that is taken is not an error: a tag is known by its name, and two
   * called "Important" in different colours would be a puzzle, not a feature.
   */
  createTag(name: string, colorHex: string): Observable<Tag> {
    return this.http.post<Tag>('/api/tags', { name, colorHex }).pipe(
      tap(tag => {
        if (!this.tags().some(existing => existing.id === tag.id)) {
          this.tags.set([...this.tags(), tag].sort((left, right) => left.name.localeCompare(right.name)));
        }
      }),
    );
  }

  /** Puts one tag on, or takes it off, a set of paths. Only those paths change. */
  setTag(tagId: string, fullPaths: string[], isApplied: boolean): Observable<void> {
    return this.http.post<void>('/api/tags/assign', { tagId, fullPaths, isApplied }).pipe(
      tap(() => {
        const updated = new Map(this.tagIdsByPath());

        for (const fullPath of fullPaths) {
          const current = new Set(updated.get(fullPath) ?? []);

          if (isApplied) {
            current.add(tagId);
          } else {
            current.delete(tagId);
          }

          if (current.size > 0) {
            updated.set(fullPath, [...current]);
          } else {
            updated.delete(fullPath);
          }
        }

        this.tagIdsByPath.set(updated);
      }),
      map(() => undefined),
    );
  }

  /** The tags on one path, resolved from ids to something drawable. */
  tagsFor(fullPath: string): Tag[] {
    const ids = this.tagIdsByPath().get(fullPath);

    if (!ids?.length) {
      return [];
    }

    const byId = this.tagsById();

    // An id with no tag behind it is dropped rather than drawn as a blank chip:
    // it means the vocabulary and the assignments arrived out of step, which the
    // single request is meant to prevent but a stale cache could still produce.
    return ids.map(id => byId.get(id)).filter((tag): tag is Tag => tag !== undefined);
  }

  /**
   * Forgets everything held here, so the next load asks the server again.
   * Called after the user deletes their file-system data, which takes the
   * vocabulary and every assignment with it.
   */
  reset(): void {
    this.request = undefined;
    this.tags.set([]);
    this.tagIdsByPath.set(new Map());
  }

  private adopt(result: FsItemTags): void {
    this.tags.set(result?.tags ?? []);
    this.tagIdsByPath.set(new Map(Object.entries(result?.tagIdsByPath ?? {})));
  }
}
