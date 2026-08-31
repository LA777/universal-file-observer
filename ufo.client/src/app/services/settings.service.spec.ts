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

  it('reads the certificate from its own path, not the settings root', () => {
    let received: unknown;
    service.getCertificate().subscribe(certificate => (received = certificate));

    const request = httpMock.expectOne('/api/settings/certificate');
    expect(request.request.method).toBe('GET');
    request.flush({ isConfigured: true, subject: 'CN=box' });

    expect((received as { subject: string }).subject).toBe('CN=box');
  });

  it('uploads a certificate with PUT and sends the archive and passphrase', () => {
    service.uploadCertificate('QkFTRTY0', 'hunter2').subscribe();

    const request = httpMock.expectOne('/api/settings/certificate');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ pfxBase64: 'QkFTRTY0', passphrase: 'hunter2' });
    request.flush({});
  });

  it('sends an empty passphrase rather than omitting it for an unprotected archive', () => {
    service.uploadCertificate('QkFTRTY0', '').subscribe();

    const request = httpMock.expectOne('/api/settings/certificate');
    // The server treats null and empty alike, but keeping the field present
    // makes the request shape the same either way.
    expect(request.request.body).toEqual({ pfxBase64: 'QkFTRTY0', passphrase: '' });
    request.flush({});
  });

  it('requests a self-signed certificate with POST', () => {
    service.generateSelfSignedCertificate().subscribe();

    const request = httpMock.expectOne('/api/settings/certificate/self-signed');
    expect(request.request.method).toBe('POST');
    request.flush({});
  });
});
