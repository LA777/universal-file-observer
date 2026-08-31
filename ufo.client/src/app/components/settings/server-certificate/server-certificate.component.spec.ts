import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ServerCertificateComponent } from './server-certificate.component';
import { ServerCertificate } from '../../../models/models';

describe('ServerCertificateComponent', () => {
  let fixture: ComponentFixture<ServerCertificateComponent>;
  let component: ServerCertificateComponent;
  let httpMock: HttpTestingController;

  const CERTIFICATE_URL = '/api/settings/certificate';
  const SELF_SIGNED_URL = '/api/settings/certificate/self-signed';

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ServerCertificateComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations()]
    });
    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ServerCertificateComponent);
    component = fixture.componentInstance;
  });

  const aCertificate = (overrides: Partial<ServerCertificate> = {}): ServerCertificate => ({
    isConfigured: true,
    subject: 'CN=ufo-host',
    thumbprint: 'AA11BB22',
    notBefore: '2026-01-01T00:00:00.0000000Z',
    notAfter: '2027-01-01T00:00:00.0000000Z',
    source: 'self-signed',
    isExpired: false,
    updatedAt: '2026-01-01T00:00:00.0000000Z',
    canManage: true,
    ...overrides
  });

  /** Answers the load that ngOnInit issues. */
  const flushLoad = (overrides: Partial<ServerCertificate> = {}) =>
    httpMock.expectOne(CERTIFICATE_URL).flush(aCertificate(overrides));

  /** A stand-in .pfx; the server, not the client, decides whether it parses. */
  const aCertificateFile = (name = 'server.pfx', sizeInBytes = 1024) =>
    new File([new Uint8Array(sizeInBytes)], name, { type: 'application/x-pkcs12' });

  const selectFile = (file: File) =>
    component.onFileSelected({ target: { files: [file] } } as unknown as Event);

  it('loads the certificate on init', () => {
    component.ngOnInit();
    flushLoad({ subject: 'CN=box.lan', source: 'user-supplied' });

    expect(component.certificate?.subject).toBe('CN=box.lan');
    expect(component.certificate?.source).toBe('user-supplied');
    httpMock.verify();
  });

  it('reports a host that is not serving TLS rather than treating it as an error', () => {
    component.ngOnInit();
    flushLoad({ isConfigured: false, subject: '', thumbprint: '' });

    // A deployment behind a TLS-terminating proxy is valid, not broken.
    expect(component.certificate?.isConfigured).toBeFalse();
    expect(component.errorMessage).toBe('');
    httpMock.verify();
  });

  it('says so when the certificate cannot be read', () => {
    component.ngOnInit();
    httpMock.expectOne(CERTIFICATE_URL).flush('boom', { status: 500, statusText: 'Server Error' });

    expect(component.certificate).toBeNull();
    expect(component.errorMessage).toBe('Could not read the server certificate.');
    httpMock.verify();
  });

  it('rejects an oversize file without contacting the server', () => {
    component.ngOnInit();
    flushLoad();

    selectFile(aCertificateFile('huge.pfx', 300 * 1024));

    expect(component.errorMessage).toContain('not a PKCS#12 archive');
    httpMock.expectNone(CERTIFICATE_URL);
    httpMock.verify();
  });

  it('uploads the chosen file as base64 and forgets the passphrase', async () => {
    component.ngOnInit();
    flushLoad();

    selectFile(aCertificateFile());
    component.certificatePassphrase = 'letmein';

    // The file is read asynchronously, so the request is only in flight once
    // that has resolved.
    await component.upload();

    const request = httpMock.expectOne(CERTIFICATE_URL);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body.passphrase).toBe('letmein');
    expect(typeof request.request.body.pfxBase64).toBe('string');
    expect(request.request.body.pfxBase64.length).toBeGreaterThan(0);
    // Must be the payload only, never the "data:...;base64," wrapper.
    expect(request.request.body.pfxBase64).not.toContain(',');
    request.flush({});

    // Re-read after a successful install.
    flushLoad({ source: 'user-supplied' });

    expect(component.isWorking).toBeFalse();
    expect(component.message).toContain('Certificate installed');
    // Left in the page it would show up in a screenshot or over a shoulder.
    expect(component.certificatePassphrase).toBe('');
    expect(component.certificateFileName).toBe('');
    httpMock.verify();
  });

  it('surfaces the reason the server gave for refusing a certificate', async () => {
    component.ngOnInit();
    flushLoad();

    selectFile(aCertificateFile());
    await component.upload();

    httpMock.expectOne(CERTIFICATE_URL).flush(
      { message: 'The certificate expired on 2020-01-01.' },
      { status: 400, statusText: 'Bad Request' });

    // The server's message is specific and actionable; a generic one would
    // throw that away.
    expect(component.errorMessage).toBe('The certificate expired on 2020-01-01.');
    expect(component.isWorking).toBeFalse();
    httpMock.verify();
  });

  it('does nothing when asked to upload with no file chosen', async () => {
    component.ngOnInit();
    flushLoad();

    await component.upload();

    httpMock.expectNone(CERTIFICATE_URL);
    httpMock.verify();
  });

  it('generates a self-signed certificate and reloads it', () => {
    component.ngOnInit();
    flushLoad({ source: 'user-supplied' });

    component.generateSelfSigned();

    const request = httpMock.expectOne(SELF_SIGNED_URL);
    expect(request.request.method).toBe('POST');
    request.flush({});

    flushLoad({ source: 'self-signed' });

    expect(component.certificate?.source).toBe('self-signed');
    expect(component.message).toContain('self-signed');
    expect(component.isWorking).toBeFalse();
    httpMock.verify();
  });

  it('reports a refused self-signed generation', () => {
    component.ngOnInit();
    flushLoad();

    component.generateSelfSigned();
    httpMock.expectOne(SELF_SIGNED_URL).flush(
      { message: 'Only an administrator can change the server certificate.' },
      { status: 400, statusText: 'Bad Request' });

    expect(component.errorMessage).toBe('Only an administrator can change the server certificate.');
    httpMock.verify();
  });

  it('renders the certificate and the management controls for an administrator', () => {
    fixture.detectChanges();
    flushLoad({ subject: 'CN=box.lan', thumbprint: 'FEED1234', canManage: true });
    fixture.detectChanges();

    const rendered = fixture.nativeElement as HTMLElement;
    expect(rendered.textContent).toContain('CN=box.lan');
    expect(rendered.textContent).toContain('FEED1234');
    expect(rendered.querySelector('input[type="file"]')).not.toBeNull();
    httpMock.verify();
  });

  it('offers no management controls to a non-administrator', () => {
    fixture.detectChanges();
    flushLoad({ canManage: false });
    fixture.detectChanges();

    const rendered = fixture.nativeElement as HTMLElement;
    // The server refuses the write regardless; hiding it keeps the page honest
    // about what this user can do.
    expect(rendered.querySelector('input[type="file"]')).toBeNull();
    expect(rendered.textContent).toContain('Only an administrator');
    httpMock.verify();
  });
});
