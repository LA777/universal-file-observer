import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, shareReplay, tap, catchError, throwError } from 'rxjs';
import { KeyBinding, KeyBindingUpdate } from '../models/models';
import { eventMatchesChord } from '../shared/key-chord';

/**
 * The user's keyboard shortcuts: fetched once, shared by every pane, and
 * consulted on each keypress.
 *
 * Held here rather than in the panes because there are two of them and they must
 * answer to the same keys. The list is also the Settings page's source of truth,
 * so rebinding a key takes effect without a reload.
 */
@Injectable({ providedIn: 'root' })
export class KeyBindingsService {
  /**
   * The bindings in force. Empty until the first load resolves, which is why
   * every lookup below treats "no match" as "do nothing" rather than as an error
   * - a keypress that arrives first should be ignored, not guessed at.
   */
  readonly keyBindings = signal<KeyBinding[]>([]);

  private request?: Observable<KeyBinding[]>;

  constructor(private http: HttpClient) {}

  /**
   * The shortcuts, loading them once and sharing that one answer.
   *
   * Both panes ask on startup, and without the replay that is two identical
   * requests racing to populate the same list.
   */
  load(): Observable<KeyBinding[]> {
    this.request ??= this.http.get<KeyBinding[]>('/api/settings/shortcuts').pipe(
      tap(keyBindings => this.keyBindings.set(keyBindings ?? [])),
      catchError((error: unknown) => {
        // The cache is dropped before the failure is passed on. A replayed
        // observable would otherwise hand every later caller the same failure
        // for the rest of the session: one bad moment on startup and no
        // shortcut works again until the page is reloaded - with F5 hard
        // reloading the SPA rather than copying anything, because that is
        // exactly the binding that stopped resolving.
        this.request = undefined;

        return throwError(() => error);
      }),
      shareReplay({ bufferSize: 1, refCount: false }),
    );

    return this.request;
  }

  /**
   * Saves the whole table, then re-reads it so the panes pick the new keys up
   * without a reload.
   *
   * The answer is the save's own result, not the list: the server stores only
   * what differs from its defaults, so what it will hand out next is not always
   * what was sent, and the re-read is what settles that.
   */
  save(updates: KeyBindingUpdate[]): Observable<void> {
    return this.http.put<void>('/api/settings/shortcuts', { bindings: updates }).pipe(
      tap(() => this.reload()),
    );
  }

  /** Discards the cached list so the next load asks the server again. */
  reload(): void {
    this.request = undefined;
    // The caller of save() reports its own failure; this is the refresh behind
    // it, and an error here must not surface as an unhandled rejection.
    this.load().subscribe({ error: () => undefined });
  }

  /**
   * The action a keypress triggers, or null when it triggers none.
   *
   * Either slot counts: the two are alternatives, which is the whole point of
   * there being two.
   */
  actionFor(event: KeyboardEvent): string | null {
    const match = this.keyBindings().find(
      keyBinding =>
        eventMatchesChord(event, keyBinding.primaryKey) || eventMatchesChord(event, keyBinding.secondaryKey),
    );

    return match?.actionId ?? null;
  }
}
