import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, BehaviorSubject, throwError, of } from 'rxjs';
import { catchError, finalize, map, shareReplay } from 'rxjs/operators';

export interface RegisterRequest {
  username: string;
  password: string;
}

export interface LoginRequest {
  username: string;
  password: string;
}

export interface AuthResponse {
  token: string;
  username: string;
}

export interface UserExistsResponse {
  isCreated: boolean;
}

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private apiUrl = '/api';
  private currentUserSubject: BehaviorSubject<string | null>;
  public currentUser$: Observable<string | null>;
  private tokenKey = 'auth_token';
  private usernameKey = 'username';

  /**
   * The renewal currently in flight, shared by everyone who asks for one.
   *
   * A lapsed access token is normally discovered by several requests at once -
   * the dashboard opens with a handful in parallel - and each rotation invalidates
   * the token the others are holding. Refreshing in parallel would therefore have
   * them invalidate each other and look exactly like a stolen token being
   * replayed, which the server answers by ending every session for the user. One
   * shared request means there is only ever one rotation to win.
   */
  private refreshInFlight: Observable<string> | null = null;

  /**
   * Bumped every time a session ends. A renewal that was already in flight then
   * belongs to the session the user just left, so its answer is discarded rather
   * than written back - otherwise signing out during a refresh would restore the
   * token and username a moment after clearing them.
   */
  private sessionGeneration = 0;

  constructor(private http: HttpClient) {
    // The two stored keys part company as soon as a token expires: the username
    // is a plain string that nothing ages out. A leftover one used to be enough
    // to open the app, because AuthGuard asked this subject and this subject
    // asked localStorage. Anything without a live token behind it is dropped
    // here, so a lapsed session starts signed out rather than half signed in.
    if (!this.isAuthenticated) {
      this.clearStoredSession();
    }

    this.currentUserSubject = new BehaviorSubject<string | null>(this.getStoredUsername());
    this.currentUser$ = this.currentUserSubject.asObservable();
  }

  public get currentUserValue(): string | null {
    return this.currentUserSubject.value;
  }

  /**
   * Whether a usable token is in hand: present, readable, and not yet past the
   * `exp` it carries.
   *
   * Advisory, in the same way `isAdmin` is. The signature is not checked here -
   * it cannot be, without the signing key - and the deadline is read against the
   * browser's own clock, so this decides what the app offers and never what the
   * server allows. A clock running fast sends someone to sign in while their
   * token is still good; one running slow admits them to a page whose first
   * request 401s, and JwtInterceptor signs them out on that. Both land the user
   * at the login form, which is where an unusable session belongs.
   */
  public get isAuthenticated(): boolean {
    const expiry = this.readTokenExpiry();

    return expiry !== null && expiry > Date.now();
  }

  checkIfUserExists(): Observable<boolean> {
    return this.http.get<UserExistsResponse>(`${this.apiUrl}/user/is-created`)
      .pipe(
        map(response => response.isCreated)
      );
  }

  register(request: RegisterRequest): Observable<any> {
    return this.http.post(`${this.apiUrl}/auth/signup`, request);
  }

  login(request: LoginRequest): Observable<AuthResponse> {
    // withCredentials so the refresh cookie the server sets is kept. It is
    // redundant while the SPA is served from the same origin as the API, and it
    // is not when `ng serve` is reached directly rather than through its proxy.
    return this.http.post<AuthResponse>(`${this.apiUrl}/auth/login`, request, { withCredentials: true })
      .pipe(
        map(response => {
          this.storeSession(response);
          return response;
        })
      );
  }

  /**
   * Trades the refresh cookie for a new access token.
   *
   * The cookie is HttpOnly, so nothing here can see whether one exists - asking
   * and being refused is the only way to find out, which is why this is called
   * speculatively by the guards as well as after a 401.
   *
   * Callers share one request: see {@link refreshInFlight}.
   */
  refreshSession(): Observable<string> {
    if (!this.refreshInFlight) {
      const generation = this.sessionGeneration;

      this.refreshInFlight = this.http
        .post<AuthResponse>(`${this.apiUrl}/auth/refresh`, {}, { withCredentials: true })
        .pipe(
          map(response => {
            if (generation !== this.sessionGeneration) {
              // Signed out while this was in flight. The token that came back is
              // for a session the user has ended, so it is dropped and the caller
              // is told the renewal failed - which for a signed-out user it did.
              throw new Error('The session ended while it was being renewed.');
            }

            this.storeSession(response);
            return response.token;
          }),
          catchError(error => {
            // A refused refresh is the end of the session, not a failed request
            // to retry: the server has already cleared the cookie.
            this.logout();
            return throwError(() => error);
          }),
          // Cleared before the value reaches subscribers, so the next lapse
          // starts a new request rather than replaying this one's token.
          finalize(() => { this.refreshInFlight = null; }),
          shareReplay({ bufferSize: 1, refCount: false })
        );
    }

    return this.refreshInFlight;
  }

  /**
   * Ends the session at both ends: the refresh token is revoked server-side, so
   * signing out means more than this browser forgetting.
   *
   * Local state goes immediately rather than when the response arrives - the
   * cookie is the server's to clear, and a signed-out user should not be left
   * looking at their data while a request hangs. The returned observable reports
   * the revocation, which callers are free to ignore.
   */
  signOut(): Observable<void> {
    const revocation = this.http
      .post<void>(`${this.apiUrl}/auth/logout`, {}, { withCredentials: true })
      .pipe(
        // Nothing here can act on a failed revocation, and the user is signed out
        // regardless; the token expires on its own within the hour.
        catchError(() => of(undefined)),
        map(() => undefined)
      );

    this.logout();

    return revocation;
  }

  /** Drops this browser's half of the session. */
  logout(): void {
    // Ends the current generation first: a renewal already on the wire must not
    // write its result back over what is being cleared here.
    this.sessionGeneration++;
    this.refreshInFlight = null;
    this.clearStoredSession();

    // Only on a real transition: subscribers treat null as "the session ended",
    // and a page that was already signed out has ended nothing.
    if (this.currentUserSubject.value !== null) {
      this.currentUserSubject.next(null);
    }
  }

  getToken(): string | null {
    return localStorage.getItem(this.tokenKey);
  }

  /**
   * Whether the signed-in user administers this installation, read from the
   * token's `ufo:is_admin` claim.
   *
   * Advisory only. It decides what the page offers, never what the server
   * allows: the signature is not checked here - it cannot be, without the
   * signing key - and a token outlives a demotion by up to its expiry. Every
   * server-scoped endpoint re-reads the flag from the database, so a tampered
   * or stale claim buys nothing beyond seeing a section whose requests are then
   * refused.
   */
  public get isAdmin(): boolean {
    return this.readTokenClaims()?.['ufo:is_admin'] === 'true';
  }

  private readTokenClaims(): Record<string, unknown> | null {
    const token = this.getToken();
    const payload = token?.split('.')[1];

    if (!payload) {
      return null;
    }

    try {
      // JWT payloads are base64url and unpadded; atob wants base64 with padding.
      const base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
      const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=');

      return JSON.parse(atob(padded)) as Record<string, unknown>;
    } catch {
      // A token we cannot read is one we cannot claim anything from.
      return null;
    }
  }

  /**
   * `exp` as milliseconds since the epoch, or null when there is no token, it
   * cannot be read, or it carries no expiry. A token whose deadline cannot be
   * established counts as no session at all rather than an endless one.
   */
  private readTokenExpiry(): number | null {
    // RFC 7519 puts exp in seconds; Date.now() is in milliseconds.
    const expiryInSeconds = this.readTokenClaims()?.['exp'];

    return typeof expiryInSeconds === 'number' ? expiryInSeconds * 1000 : null;
  }

  private storeSession(response: AuthResponse): void {
    localStorage.setItem(this.tokenKey, response.token);
    localStorage.setItem(this.usernameKey, response.username);
    this.currentUserSubject.next(response.username);
  }

  private clearStoredSession(): void {
    localStorage.removeItem(this.tokenKey);
    localStorage.removeItem(this.usernameKey);
  }

  private getStoredUsername(): string | null {
    return localStorage.getItem(this.usernameKey);
  }
}
