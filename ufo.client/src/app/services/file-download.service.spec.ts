import { TestBed } from '@angular/core/testing';
import { FileDownloadService } from './file-download.service';

describe('FileDownloadService', () => {
  let service: FileDownloadService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(FileDownloadService);
  });

  it('clicks a hidden anchor carrying the file name and an object URL for the blob', () => {
    const blob = new Blob(['PK'], { type: 'application/zip' });
    spyOn(URL, 'createObjectURL').and.returnValue('blob:ufo/archive');
    const revoke = spyOn(URL, 'revokeObjectURL');
    let clicked: HTMLAnchorElement | undefined;
    spyOn(HTMLAnchorElement.prototype, 'click').and.callFake(function (this: HTMLAnchorElement) {
      clicked = this;
    });
    jasmine.clock().install();

    try {
      service.save(blob, 'ufo-export-all-20260913-170311.zip');

      expect(URL.createObjectURL).toHaveBeenCalledOnceWith(blob);
      expect(clicked).toBeDefined();
      expect(clicked!.download).toBe('ufo-export-all-20260913-170311.zip');
      expect(clicked!.href).toBe('blob:ufo/archive');
      // Not left behind in the document once clicked.
      expect(document.body.contains(clicked!)).toBeFalse();
      // Revoked later rather than at once, which can cancel the download.
      expect(revoke).not.toHaveBeenCalled();
      jasmine.clock().tick(1000);
      expect(revoke).toHaveBeenCalledOnceWith('blob:ufo/archive');
    } finally {
      jasmine.clock().uninstall();
    }
  });
});
