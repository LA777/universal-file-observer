import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ServerCertificate, Theme, UserSettings } from '../models/models';

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private apiUrl = '/api/settings';

  constructor(private http: HttpClient) {}

  /**
   * The current user's saved settings. The API answers with its defaults rather
   * than a 404 when nothing has been saved, so this never has to handle a gap.
   */
  getSettings(): Observable<UserSettings> {
    return this.http.get<UserSettings>(this.apiUrl);
  }

  saveSettings(theme: Theme): Observable<unknown> {
    return this.http.put(this.apiUrl, { theme });
  }

  /**
   * The server's TLS certificate. Administrators only - anyone else is refused
   * with a 403, which is why the Settings page does not render this section for
   * them in the first place.
   */
  getCertificate(): Observable<ServerCertificate> {
    return this.http.get<ServerCertificate>(`${this.apiUrl}/certificate`);
  }

  /**
   * Uploads a PKCS#12 archive as the server's certificate.
   *
   * @param pfxBase64 The .pfx/.p12 file, base64 encoded.
   * @param passphrase Its passphrase, or empty when it has none. Used only to
   * open the archive - the server re-protects it with its own key and never
   * stores this.
   */
  uploadCertificate(pfxBase64: string, passphrase: string): Observable<unknown> {
    return this.http.put(`${this.apiUrl}/certificate`, { pfxBase64, passphrase });
  }

  /** Replaces the current certificate with a freshly generated self-signed one. */
  generateSelfSignedCertificate(): Observable<unknown> {
    return this.http.post(`${this.apiUrl}/certificate/self-signed`, {});
  }
}
