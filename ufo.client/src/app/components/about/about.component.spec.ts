import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AboutComponent } from './about.component';

describe('AboutComponent', () => {
  let fixture: ComponentFixture<AboutComponent>;
  let component: AboutComponent;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AboutComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AboutComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => httpMock.verify());

  it('shows the version the server reports', () => {
    fixture.detectChanges();
    httpMock.expectOne('/api/version').flush({ version: '1.2.5' });
    fixture.detectChanges();

    expect(component.version).toBe('1.2.5');
    expect(component.error).toBe('');
    expect(fixture.nativeElement.textContent).toContain('1.2.5');
  });

  it('says so when the server does not answer, rather than showing a blank', () => {
    fixture.detectChanges();
    httpMock.expectOne('/api/version').flush('boom', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(component.version).toBe('');
    expect(component.isLoading).toBeFalse();
    expect(component.error).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('Unavailable');
  });

  it('asks the server exactly once', () => {
    fixture.detectChanges();

    httpMock.expectOne('/api/version').flush({ version: '1.0.0' });
    httpMock.expectNone('/api/version');
  });
});
