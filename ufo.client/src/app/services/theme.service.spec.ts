import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ThemeService } from './theme.service';

describe('ThemeService', () => {
  let httpMock: HttpTestingController;

  /** ThemeService reads the cache in its constructor, so it is built per test. */
  const createService = () => TestBed.inject(ThemeService);

  const rootClasses = () => Array.from(document.documentElement.classList)
    .filter(name => name.startsWith('theme-'));

  const configure = () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    httpMock = TestBed.inject(HttpTestingController);
  };

  beforeEach(() => {
    localStorage.removeItem('ufo_theme');
    document.documentElement.classList.remove('theme-light', 'theme-dark');
    configure();
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.removeItem('ufo_theme');
    document.documentElement.classList.remove('theme-light', 'theme-dark');
  });

  it('starts on the default theme when nothing is cached', () => {
    const service = createService();

    expect(service.currentTheme).toBe('dark');
    expect(rootClasses()).toEqual(['theme-dark']);
  });

  it('paints the cached theme immediately, before any request', () => {
    localStorage.setItem('ufo_theme', 'light');

    const service = createService();

    // No HTTP has happened yet — this is what stops a flash of the wrong palette.
    expect(service.currentTheme).toBe('light');
    expect(rootClasses()).toEqual(['theme-light']);
  });

  it('ignores a cached value that is not a known theme', () => {
    localStorage.setItem('ufo_theme', 'solarized');

    expect(createService().currentTheme).toBe('dark');
  });

  it('applies and caches the theme the server returns', () => {
    const service = createService();

    service.loadFromServer().subscribe();
    httpMock.expectOne('/api/settings').flush({ id: 'x', theme: 'light' });

    expect(service.currentTheme).toBe('light');
    expect(rootClasses()).toEqual(['theme-light']);
    expect(localStorage.getItem('ufo_theme')).toBe('light');
  });

  it('lets the server override a stale cached theme', () => {
    localStorage.setItem('ufo_theme', 'light');
    const service = createService();

    service.loadFromServer().subscribe();
    httpMock.expectOne('/api/settings').flush({ id: 'x', theme: 'dark' });

    expect(service.currentTheme).toBe('dark');
  });

  // Regression: loadFromServer used to swallow failures with a catchError, which
  // made the Settings page's error branch unreachable.
  it('surfaces a failed load to the caller instead of swallowing it', () => {
    const service = createService();
    let sawError = false;
    let sawValue = false;

    service.loadFromServer().subscribe({
      next: () => (sawValue = true),
      error: () => (sawError = true)
    });
    httpMock.expectOne('/api/settings').flush('boom', { status: 500, statusText: 'Server Error' });

    expect(sawError).toBeTrue();
    expect(sawValue).toBeFalse();
  });

  it('leaves the applied theme untouched when the load fails', () => {
    localStorage.setItem('ufo_theme', 'light');
    const service = createService();

    service.loadFromServer().subscribe({ error: () => undefined });
    httpMock.expectOne('/api/settings').flush('boom', { status: 500, statusText: 'Server Error' });

    // A readable page in the previous palette beats an unstyled one.
    expect(service.currentTheme).toBe('light');
    expect(rootClasses()).toEqual(['theme-light']);
  });

  it('applies the theme before the save request completes', () => {
    const service = createService();

    service.saveTheme('light').subscribe();

    expect(service.currentTheme).toBe('light');
    httpMock.expectOne('/api/settings').flush({});
  });

  it('publishes theme changes to subscribers', () => {
    const service = createService();
    const seen: string[] = [];
    service.currentTheme$.subscribe(theme => seen.push(theme));

    service.applyTheme('light');
    service.applyTheme('light');
    service.applyTheme('dark');

    // The repeat is collapsed rather than re-emitted.
    expect(seen).toEqual(['dark', 'light', 'dark']);
  });

  it('swaps the document class rather than accumulating them', () => {
    const service = createService();

    service.applyTheme('light');
    service.applyTheme('dark');

    expect(rootClasses()).toEqual(['theme-dark']);
  });

  // Regression: the cache is one key for the whole browser and was never cleared
  // on sign-out, so the next user started in the previous user's theme.
  it('resetToDefault clears the cache and returns to the default theme', () => {
    const service = createService();
    service.applyTheme('light');

    service.resetToDefault();

    expect(localStorage.getItem('ufo_theme')).toBeNull();
    expect(service.currentTheme).toBe('dark');
    expect(rootClasses()).toEqual(['theme-dark']);
  });

  it('does not leak a theme to the next user of the same browser', () => {
    const first = createService();
    first.applyTheme('light');
    first.resetToDefault();

    // A fresh service is what a reload after sign-out builds.
    httpMock.verify();
    TestBed.resetTestingModule();
    configure();

    expect(TestBed.inject(ThemeService).currentTheme).toBe('dark');
  });
});
