import { Injectable } from '@angular/core';

/**
 * Hands a blob to the browser as a download.
 *
 * A service rather than a function so a component that downloads can be
 * tested without a real anchor click reaching the browser under Karma.
 */
@Injectable({ providedIn: 'root' })
export class FileDownloadService {
  save(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    anchor.style.display = 'none';

    // Firefox wants the anchor in the document before click() is honoured.
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);

    // Deferred: revoking synchronously can cancel the download in some browsers.
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
}
