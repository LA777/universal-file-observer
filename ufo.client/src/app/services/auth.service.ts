import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, BehaviorSubject } from 'rxjs';
import { map } from 'rxjs/operators';

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

  constructor(private http: HttpClient) {
    this.currentUserSubject = new BehaviorSubject<string | null>(this.getStoredUsername());
    this.currentUser$ = this.currentUserSubject.asObservable();
  }

  public get currentUserValue(): string | null {
    return this.currentUserSubject.value;
  }

  public get isAuthenticated(): boolean {
    return !!this.getToken();
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
    return this.http.post<AuthResponse>(`${this.apiUrl}/auth/login`, request)
      .pipe(
        map(response => {
          // Store token and username
          localStorage.setItem(this.tokenKey, response.token);
          localStorage.setItem('username', response.username);
          this.currentUserSubject.next(response.username);
          return response;
        })
      );
  }

  logout(): void {
    localStorage.removeItem(this.tokenKey);
    localStorage.removeItem('username');
    this.currentUserSubject.next(null);
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

  private getStoredUsername(): string | null {
    return localStorage.getItem('username');
  }
}
