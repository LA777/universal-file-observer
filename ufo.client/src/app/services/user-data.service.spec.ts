import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { UserDataService } from './user-data.service';

describe('UserDataService', () => {
  let service: UserDataService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(UserDataService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('deletes snapshots with DELETE /api/userdata/snapshots', () => {
    let received: unknown;
    service.deleteSnapshots().subscribe(result => (received = result));

    const request = httpMock.expectOne('/api/userdata/snapshots');
    expect(request.request.method).toBe('DELETE');
    request.flush({ message: 'Deleted 2 snapshot(s).' });

    expect(received).toEqual({ message: 'Deleted 2 snapshot(s).' });
  });

  it('deletes file system data with DELETE /api/userdata/filesystem', () => {
    service.deleteFileSystemData().subscribe();

    const request = httpMock.expectOne('/api/userdata/filesystem');
    expect(request.request.method).toBe('DELETE');
    request.flush({});
  });

  it('deletes settings with DELETE /api/userdata/settings', () => {
    service.deleteSettings().subscribe();

    const request = httpMock.expectOne('/api/userdata/settings');
    expect(request.request.method).toBe('DELETE');
    request.flush({});
  });

  it('deletes all data with DELETE /api/userdata/all', () => {
    service.deleteAll().subscribe();

    const request = httpMock.expectOne('/api/userdata/all');
    expect(request.request.method).toBe('DELETE');
    request.flush({});
  });

  it('passes a failed deletion on to the caller', () => {
    let status = 0;
    service.deleteSettings().subscribe({ error: error => (status = error.status) });

    httpMock.expectOne('/api/userdata/settings').flush('boom', { status: 500, statusText: 'Server Error' });

    expect(status).toBe(500);
  });
});
