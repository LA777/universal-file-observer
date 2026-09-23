import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ExportDataComponent, exportFileName } from './export-data.component';
import { ExportScope } from '../../../services/export.service';
import { FileDownloadService } from '../../../services/file-download.service';

describe('exportFileName', () => {
  it('carries the scope and the local date and time, zero-padded', () => {
    const moment = new Date(2026, 8, 3, 7, 5, 9); // 3 September 2026, 07:05:09 local

    expect(exportFileName('snapshots', moment)).toBe('ufo-export-snapshots-20260903-070509.zip');
  });

  it('differs between two moments, so exports never overwrite each other', () => {
    const first = exportFileName('all', new Date(2026, 8, 13, 17, 3, 11));
    const second = exportFileName('all', new Date(2026, 8, 13, 17, 3, 12));

    expect(first).not.toBe(second);
  });
});

describe('ExportDataComponent', () => {
  let fixture: ComponentFixture<ExportDataComponent>;
  let component: ExportDataComponent;
  let httpMock: HttpTestingController;
  let save: jasmine.Spy;

  beforeEach(() => {
    save = jasmine.createSpy('save');

    TestBed.configureTestingModule({
      imports: [ExportDataComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        { provide: FileDownloadService, useValue: { save } }
      ]
    });
    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ExportDataComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => httpMock.verify());

  const choiceOf = (scope: ExportScope) => component.choices.find(choice => choice.scope === scope)!;

  it('offers exactly the four exports, in the order of the page', () => {
    expect(component.choices.map(choice => choice.scope)).toEqual(['user', 'filesystem', 'snapshots', 'all']);
    expect(component.choices.map(choice => choice.label)).toEqual([
      'Export user data',
      'Export file system data',
      'Export snapshots',
      'Export all'
    ]);
  });

  it('renders one button per export', () => {
    fixture.detectChanges();

    const buttons = fixture.nativeElement.querySelectorAll('button.export-button') as NodeListOf<HTMLButtonElement>;
    expect(Array.from(buttons).map(button => button.dataset['scope'])).toEqual(['user', 'filesystem', 'snapshots', 'all']);
  });

  (['user', 'filesystem', 'snapshots', 'all'] as ExportScope[]).forEach(scope => {
    it(`downloads the ${scope} export as a zip named with the scope and the moment`, () => {
      jasmine.clock().install();
      jasmine.clock().mockDate(new Date(2026, 8, 13, 17, 3, 11));

      try {
        component.requestExport(choiceOf(scope));

        expect(component.workingScope).toBe(scope);
        const request = httpMock.expectOne(`/api/export/${scope}`);
        expect(request.request.method).toBe('GET');
        const archive = new Blob(['PK'], { type: 'application/zip' });
        request.flush(archive);

        expect(save).toHaveBeenCalledOnceWith(archive, `ufo-export-${scope}-20260913-170311.zip`);
        expect(component.workingScope).toBeNull();
        expect(component.message).toBe(`Saved ufo-export-${scope}-20260913-170311.zip.`);
        expect(component.errorMessage).toBe('');
      } finally {
        jasmine.clock().uninstall();
      }
    });
  });

  it('reports a failed export, saves nothing, and lets the user try again', () => {
    component.requestExport(choiceOf('snapshots'));
    httpMock.expectOne('/api/export/snapshots').flush(new Blob(), { status: 500, statusText: 'Server Error' });

    expect(save).not.toHaveBeenCalled();
    expect(component.workingScope).toBeNull();
    expect(component.message).toBe('');
    expect(component.errorMessage).not.toBe('');

    component.requestExport(choiceOf('snapshots'));
    httpMock.expectOne('/api/export/snapshots').flush(new Blob(['PK']));

    expect(save).toHaveBeenCalledTimes(1);
    expect(component.errorMessage).toBe('');
  });

  it('ignores a second click while an export is in flight', () => {
    component.requestExport(choiceOf('all'));
    expect(component.isWorking).toBeTrue();

    component.requestExport(choiceOf('user'));

    httpMock.expectNone('/api/export/user');
    httpMock.expectOne('/api/export/all').flush(new Blob(['PK']));
    expect(save).toHaveBeenCalledTimes(1);
  });
});
