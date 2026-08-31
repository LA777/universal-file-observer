import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';
import { map, tap } from 'rxjs/operators';
import { Theme } from '../models/models';
import { SettingsService } from './settings.service';

/**
 * Owns the active theme: which one is applied to the document, and keeping the
 * database copy in step.
 *
 * The database is the record; localStorage is only a cache so a reload paints
 * the right palette immediately instead of flashing the default while
 * /api/settings is in flight. Whatever the server returns wins over the cache.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  /** Matches :root.theme-light in styles.css. */
  private static readonly themeClassPrefix = 'theme-';
  private static readonly defaultTheme: Theme = 'dark';
  private readonly cacheKey = 'ufo_theme';

  private readonly currentThemeSubject: BehaviorSubject<Theme>;
  public readonly currentTheme$: Observable<Theme>;

  constructor(private settingsService: SettingsService) {
    const initialTheme = this.readCachedTheme() ?? ThemeService.defaultTheme;
    this.currentThemeSubject = new BehaviorSubject<Theme>(initialTheme);
    this.currentTheme$ = this.currentThemeSubject.asObservable();
    this.applyToDocument(initialTheme);
  }

  public get currentTheme(): Theme {
    return this.currentThemeSubject.value;
  }

  /**
   * Pulls the saved theme and applies it. Called once the user is authenticated.
   * A failure is passed on to the caller rather than swallowed here, so the
   * Settings page can say the load failed; because the applying happens in the
   * tap, a failure simply leaves whatever is already applied in place — a
   * readable page in the wrong palette beats an unstyled one.
   */
  loadFromServer(): Observable<Theme> {
    return this.settingsService.getSettings().pipe(
      map(settings => settings.theme),
      tap(theme => this.applyTheme(theme))
    );
  }

  /** Applies a theme immediately, then persists it. */
  saveTheme(theme: Theme): Observable<unknown> {
    this.applyTheme(theme);
    return this.settingsService.saveSettings(theme);
  }

  /**
   * Drops the cached theme and returns to the default. Called on sign-out: the
   * cache is one key for the whole browser, so without this the next user to
   * sign in on the same machine paints in the previous user's theme until (and
   * unless) their own settings arrive from the server.
   */
  resetToDefault(): void {
    try {
      localStorage.removeItem(this.cacheKey);
    } catch {
      // Nothing cached is exactly the state we were after.
    }

    if (ThemeService.defaultTheme !== this.currentThemeSubject.value) {
      this.currentThemeSubject.next(ThemeService.defaultTheme);
    }

    this.applyToDocument(ThemeService.defaultTheme);
  }

  /** Applies a theme locally without touching the server. */
  applyTheme(theme: Theme): void {
    if (theme !== this.currentThemeSubject.value) {
      this.currentThemeSubject.next(theme);
    }

    this.applyToDocument(theme);
    this.writeCachedTheme(theme);
  }

  private applyToDocument(theme: Theme): void {
    const rootElement = document.documentElement;
    rootElement.classList.remove(
      `${ThemeService.themeClassPrefix}light`,
      `${ThemeService.themeClassPrefix}dark`);
    rootElement.classList.add(`${ThemeService.themeClassPrefix}${theme}`);
  }

  private readCachedTheme(): Theme | null {
    // Private-browsing modes can throw on access rather than return null.
    try {
      const cachedTheme = localStorage.getItem(this.cacheKey);
      return cachedTheme === 'light' || cachedTheme === 'dark' ? cachedTheme : null;
    } catch {
      return null;
    }
  }

  private writeCachedTheme(theme: Theme): void {
    try {
      localStorage.setItem(this.cacheKey, theme);
    } catch {
      // A missing cache only costs a brief flash on the next reload.
    }
  }
}
