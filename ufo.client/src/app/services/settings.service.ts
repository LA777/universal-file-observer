import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { Theme, UserSettings } from '../models/models';

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
}
