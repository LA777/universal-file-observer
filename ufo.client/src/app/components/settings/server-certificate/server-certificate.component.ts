import { Component, OnInit, ChangeDetectionStrategy, ChangeDetectorRef, ElementRef, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ServerCertificate } from '../../../models/models';
import { SettingsService } from '../../../services/settings.service';

/**
 * Matches ServerCertificateService.MaximumPfxSizeInBytes. Checked here purely so
 * an obvious mistake is reported without a round trip; the server enforces it.
 */
const MAXIMUM_CERTIFICATE_SIZE_IN_BYTES = 256 * 1024;

/**
 * The server's TLS certificate, on the Settings page.
 *
 * Its own component rather than another section of SettingsComponent: the
 * certificate is server-scoped where the rest of that page is per-user, the two
 * share no state, and keeping them apart keeps each stylesheet inside the
 * component style budget.
 */
@Component({
  selector: 'app-server-certificate',
  standalone: true,
  imports: [CommonModule, FormsModule, MatIconModule, MatTooltipModule],
  templateUrl: './server-certificate.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './server-certificate.component.css'
})
export class ServerCertificateComponent implements OnInit {
  certificate: ServerCertificate | null = null;
  certificateFileName = '';
  certificatePassphrase = '';
  isWorking = false;
  message = '';
  errorMessage = '';

  private selectedFile: File | null = null;

  /**
   * The live file input, kept so it can be cleared. A file input only raises
   * `change` when the chosen file differs from its current value, so leaving a
   * consumed selection in place means re-picking the same file does nothing.
   */
  @ViewChild('certificateFileInput')
  private certificateFileInput?: ElementRef<HTMLInputElement>;

  constructor(
    private settingsService: SettingsService,
    private changeDetectorRef: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.load();
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;

    this.message = '';
    this.errorMessage = '';
    this.selectedFile = file;
    this.certificateFileName = file?.name ?? '';

    if (file && file.size > MAXIMUM_CERTIFICATE_SIZE_IN_BYTES) {
      this.selectedFile = null;
      this.errorMessage = `${file.name} is larger than ${MAXIMUM_CERTIFICATE_SIZE_IN_BYTES / 1024} KB, so it is not a PKCS#12 archive.`;
    }
  }

  async upload(): Promise<void> {
    if (!this.selectedFile || this.isWorking) {
      return;
    }

    this.isWorking = true;
    this.message = '';
    this.errorMessage = '';

    let pfxBase64: string;
    try {
      pfxBase64 = await this.readFileAsBase64(this.selectedFile);
    } catch {
      this.isWorking = false;
      this.errorMessage = 'The file could not be read.';
      this.changeDetectorRef.markForCheck();
      return;
    }

    this.settingsService.uploadCertificate(pfxBase64, this.certificatePassphrase).subscribe({
      next: () => {
        this.isWorking = false;
        this.message =
          'Certificate installed. It is served from the next connection onwards - reload over HTTPS to see it.';
        // The passphrase has done its job; there is no reason to leave it in the
        // page where a later screenshot or shoulder-surfer would catch it.
        this.certificatePassphrase = '';
        this.clearSelectedFile();
        this.load();
      },
      error: response => {
        this.isWorking = false;
        this.errorMessage = this.describeFailure(response, 'Could not install the certificate.');
        this.changeDetectorRef.markForCheck();
      }
    });
  }

  generateSelfSigned(): void {
    if (this.isWorking) {
      return;
    }

    this.isWorking = true;
    this.message = '';
    this.errorMessage = '';

    this.settingsService.generateSelfSignedCertificate().subscribe({
      next: () => {
        this.isWorking = false;
        this.message =
          'A new self-signed certificate is in use. Browsers will warn about it until it is trusted on each device.';
        this.clearSelectedFile();
        this.load();
      },
      error: response => {
        this.isWorking = false;
        this.errorMessage = this.describeFailure(response, 'Could not generate a certificate.');
        this.changeDetectorRef.markForCheck();
      }
    });
  }

  private load(): void {
    this.settingsService.getCertificate().subscribe({
      next: certificate => {
        this.certificate = certificate;
        this.changeDetectorRef.markForCheck();
      },
      error: () => {
        this.certificate = null;
        this.errorMessage = 'Could not read the server certificate.';
        this.changeDetectorRef.markForCheck();
      }
    });
  }

  private clearSelectedFile(): void {
    this.selectedFile = null;
    this.certificateFileName = '';

    // Clearing the element's value too, so choosing the same file again is still
    // a change and still raises the event that re-enables Install.
    if (this.certificateFileInput) {
      this.certificateFileInput.nativeElement.value = '';
    }
  }

  /**
   * The server takes the archive as base64 in a JSON body rather than as a
   * multipart upload, which keeps the endpoint the same shape as every other
   * write in this API.
   */
  private readFileAsBase64(file: File): Promise<string> {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();

      reader.onload = () => {
        const result = reader.result as string;
        // readAsDataURL yields "data:<type>;base64,<payload>"; only the payload
        // is wanted.
        const separatorIndex = result.indexOf(',');
        resolve(separatorIndex >= 0 ? result.slice(separatorIndex + 1) : result);
      };
      reader.onerror = () => reject(reader.error);

      reader.readAsDataURL(file);
    });
  }

  /**
   * Prefers the server's own message: rejections here are specific and
   * actionable ("the passphrase is wrong", "the certificate expired"), and
   * replacing them with a generic failure would throw that away.
   */
  private describeFailure(response: unknown, fallback: string): string {
    const message = (response as { error?: { message?: string } })?.error?.message;

    return message && message.trim().length > 0 ? message : fallback;
  }
}
