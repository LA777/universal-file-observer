import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { SettingsComponent } from './settings.component';
import { ThemeService } from '../../services/theme.service';

describe('SettingsComponent', () => {
  let fixture: ComponentFixture<SettingsComponent>;
  let component: SettingsComponent;
  let httpMock: HttpTestingController;
  let themeService: ThemeService;

  beforeEach(() => {
    localStorage.clear();
    document.documentElement.classList.remove('theme-light', 'theme-dark');

    TestBed.configureTestingModule({
      imports: [SettingsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations()
      ]
    });
    httpMock = TestBed.inject(HttpTestingController);
    themeService = TestBed.inject(ThemeService);
    fixture = TestBed.createComponent(SettingsComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    localStorage.clear();
    document.documentElement.classList.remove('theme-light', 'theme-dark');
  });

  /** Answers the load that ngOnInit issues. */
  const flushLoad = (theme: 'light' | 'dark' = 'dark') =>
    httpMock.expectOne('/api/settings').flush({ id: 'x', theme });

  it('offers exactly the themes the server accepts', () => {
    expect(component.themeChoices.map(choice => choice.value)).toEqual(['light', 'dark']);
    httpMock.expectNone('/api/settings');
  });

  it('shows the saved theme as selected on load', () => {
    component.ngOnInit();
    flushLoad('light');

    expect(component.selectedTheme).toBe('light');
    expect(component.errorMessage).toBe('');
    httpMock.verify();
  });

  // Regression: this branch was unreachable while loadFromServer swallowed errors.
  it('reports a failed load', () => {
    component.ngOnInit();
    httpMock.expectOne('/api/settings').flush('boom', { status: 500, statusText: 'Server Error' });

    expect(component.errorMessage).toBe('Could not load your settings.');
    httpMock.verify();
  });

  it('saves the chosen theme and confirms it', () => {
    component.ngOnInit();
    flushLoad('dark');

    component.selectTheme('light');

    // Applied under the click, before the request is answered.
    expect(themeService.currentTheme).toBe('light');
    expect(component.isSaving).toBeTrue();

    const request = httpMock.expectOne('/api/settings');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ theme: 'light' });
    request.flush({});

    expect(component.isSaving).toBeFalse();
    expect(component.savedMessage).toBe('Saved.');
    expect(component.errorMessage).toBe('');
    httpMock.verify();
  });

  it('rolls the theme back when the save fails', () => {
    component.ngOnInit();
    flushLoad('dark');

    component.selectTheme('light');
    httpMock.expectOne('/api/settings').flush('boom', { status: 500, statusText: 'Server Error' });

    // The optimistic switch must not survive a failed write.
    expect(component.selectedTheme).toBe('dark');
    expect(themeService.currentTheme).toBe('dark');
    expect(component.isSaving).toBeFalse();
    expect(component.savedMessage).toBe('');
    expect(component.errorMessage).toBe('Could not save your settings. Please try again.');
    httpMock.verify();
  });

  it('does not re-save the theme that is already selected', () => {
    component.ngOnInit();
    flushLoad('light');

    component.selectTheme('light');

    httpMock.expectNone('/api/settings');
    httpMock.verify();
  });

  it('lets the user retry the theme they are already on after a failure', () => {
    component.ngOnInit();
    flushLoad('dark');

    component.selectTheme('light');
    httpMock.expectOne('/api/settings').flush('boom', { status: 500, statusText: 'Server Error' });
    // The failed save rolled the selection back, so the user is on 'dark' again.
    expect(component.selectedTheme).toBe('dark');
    expect(component.errorMessage).not.toBe('');

    // Re-picking the selected theme is normally a no-op; while an error is
    // showing it has to go through, or a failed save would be unretryable.
    component.selectTheme('dark');

    const request = httpMock.expectOne('/api/settings');
    expect(request.request.body).toEqual({ theme: 'dark' });
    request.flush({});

    expect(component.savedMessage).toBe('Saved.');
    expect(component.errorMessage).toBe('');
    httpMock.verify();
  });

  it('goes back to the dashboard', () => {
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    component.goBack();

    expect(navigate).toHaveBeenCalledWith(['/dashboard']);
    httpMock.expectNone('/api/settings');
  });
});
