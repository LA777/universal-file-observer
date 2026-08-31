import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Router } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { AppComponent } from './app.component';
import { AuthService } from './services/auth.service';
import { ThemeService } from './services/theme.service';

describe('AppComponent', () => {
  let httpMock: HttpTestingController;

  const configure = () => {
    TestBed.configureTestingModule({
      // AppComponent is standalone, so it is imported rather than declared.
      imports: [AppComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations()
      ]
    });
    httpMock = TestBed.inject(HttpTestingController);
  };

  beforeEach(() => {
    localStorage.clear();
    document.documentElement.classList.remove('theme-light', 'theme-dark');
    configure();
  });

  afterEach(() => {
    localStorage.clear();
    document.documentElement.classList.remove('theme-light', 'theme-dark');
  });

  it('creates the app', () => {
    const fixture = TestBed.createComponent(AppComponent);

    expect(fixture.componentInstance).toBeTruthy();
    httpMock.verify();
  });

  it('does not ask for settings when nobody is signed in', () => {
    const fixture = TestBed.createComponent(AppComponent);

    fixture.componentInstance.ngOnInit();

    // /api/settings is authenticated; asking without a token would only 401.
    httpMock.expectNone('/api/settings');
    httpMock.verify();
  });

  it('loads and applies the signed-in user\'s theme on startup', () => {
    localStorage.setItem('auth_token', 'a-token');
    localStorage.setItem('username', 'alex');
    const fixture = TestBed.createComponent(AppComponent);

    fixture.componentInstance.ngOnInit();

    httpMock.expectOne('/api/settings').flush({ id: 'x', theme: 'light' });
    expect(TestBed.inject(ThemeService).currentTheme).toBe('light');
    httpMock.verify();
  });

  it('stays usable when the settings request fails on startup', () => {
    localStorage.setItem('auth_token', 'a-token');
    localStorage.setItem('username', 'alex');
    const fixture = TestBed.createComponent(AppComponent);

    // An unhandled error here would surface as a console error in every session
    // where the API is briefly unavailable.
    expect(() => {
      fixture.componentInstance.ngOnInit();
      httpMock.expectOne('/api/settings')
        .flush('boom', { status: 500, statusText: 'Server Error' });
    }).not.toThrow();

    expect(TestBed.inject(ThemeService).currentTheme).toBe('dark');
    httpMock.verify();
  });

  it('navigates to the settings page from the header', () => {
    const router = TestBed.inject(Router);
    const navigate = spyOn(router, 'navigate').and.resolveTo(true);
    const fixture = TestBed.createComponent(AppComponent);

    fixture.componentInstance.openSettings();

    expect(navigate).toHaveBeenCalledWith(['/settings']);
    httpMock.verify();
  });

  // Regression: the theme cache is one key for the whole browser, so signing out
  // has to drop it or the next user starts in this user's theme.
  it('clears the cached theme on logout', () => {
    const router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    const themeService = TestBed.inject(ThemeService);
    themeService.applyTheme('light');
    const fixture = TestBed.createComponent(AppComponent);

    fixture.componentInstance.logout();

    expect(localStorage.getItem('ufo_theme')).toBeNull();
    expect(themeService.currentTheme).toBe('dark');
    expect(TestBed.inject(AuthService).isAuthenticated).toBeFalse();
    httpMock.verify();
  });
});
