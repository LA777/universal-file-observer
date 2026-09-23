import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ExportScope, ExportService } from './export.service';

describe('ExportService', () => {
  let service: ExportService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(ExportService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  (['user', 'filesystem', 'snapshots', 'all'] as ExportScope[]).forEach(scope => {
    it(`fetches the ${scope} export as a blob from GET /api/export/${scope}`, () => {
      let received: Blob | undefined;
      service.export(scope).subscribe(blob => (received = blob));

      const request = httpMock.expectOne(`/api/export/${scope}`);
      expect(request.request.method).toBe('GET');
      // Bytes, not JSON: the answer is a zip and must not be parsed.
      expect(request.request.responseType).toBe('blob');
      request.flush(new Blob(['PK'], { type: 'application/zip' }));

      expect(received).toBeInstanceOf(Blob);
      expect(received!.size).toBe(2);
    });
  });

  it('passes a failed export on to the caller', () => {
    let status = 0;
    service.export('all').subscribe({ error: error => (status = error.status) });

    httpMock.expectOne('/api/export/all').flush(new Blob(), { status: 500, statusText: 'Server Error' });

    expect(status).toBe(500);
  });
});
