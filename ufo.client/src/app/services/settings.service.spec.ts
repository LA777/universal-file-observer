import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SettingsService } from './settings.service';

describe('SettingsService', () => {
  let service: SettingsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(SettingsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('reads settings with GET /api/settings', () => {
    let received: unknown;
    service.getSettings().subscribe(settings => (received = settings));

    const request = httpMock.expectOne('/api/settings');
    expect(request.request.method).toBe('GET');
    request.flush({ id: 'abc', theme: 'light' });

    expect(received).toEqual({ id: 'abc', theme: 'light' } as any);
  });

  it('saves with PUT and sends the theme in the body', () => {
    service.saveSettings('light').subscribe();

    const request = httpMock.expectOne('/api/settings');
    expect(request.request.method).toBe('PUT');
    // The server requires the field to be present — an empty body is a 400.
    expect(request.request.body).toEqual({ theme: 'light' });
    request.flush({});
  });

  it('passes a failed read on to the caller', () => {
    let status = 0;
    service.getSettings().subscribe({ error: error => (status = error.status) });

    httpMock.expectOne('/api/settings').flush('boom', { status: 500, statusText: 'Server Error' });

    expect(status).toBe(500);
  });
});
