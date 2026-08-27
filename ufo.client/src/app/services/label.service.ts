import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { Label } from '../models/models';

@Injectable({ providedIn: 'root' })
export class LabelService {
  private apiUrl = '/api/label';

  constructor(private http: HttpClient) {}

  /** All labels of the current user; the API returns 404 when there are none. */
  getAllLabels(): Observable<Label[]> {
    return this.http.get<Label[]>(this.apiUrl).pipe(catchError(() => of([] as Label[])));
  }

  /** Labels assigned to one snapshot; 404 when none. */
  getLabelsBySnapshot(snapshotId: string): Observable<Label[]> {
    return this.http
      .get<Label[]>(`${this.apiUrl}/snapshot/${snapshotId}`)
      .pipe(catchError(() => of([] as Label[])));
  }

  /** Create a label; optionally assign it to snapshots in the same call. */
  createLabel(name: string, colorHex: string, snapshotIds: string[] = []): Observable<unknown> {
    return this.http.post(this.apiUrl, { name, colorHex, snapshotIds });
  }

  updateLabel(label: { id: string; name: string; colorHex: string }): Observable<unknown> {
    return this.http.put(this.apiUrl, label);
  }

  deleteLabel(labelId: string): Observable<unknown> {
    // The API answers with a plain-text message.
    return this.http.delete(`${this.apiUrl}/${labelId}`, { responseType: 'text' });
  }

  addToSnapshot(labelId: string, snapshotId: string): Observable<unknown> {
    return this.http.post(`${this.apiUrl}/${labelId}/snapshot/${snapshotId}`, {});
  }

  removeFromSnapshot(labelId: string, snapshotId: string): Observable<unknown> {
    return this.http.delete(`${this.apiUrl}/${labelId}/snapshot/${snapshotId}`);
  }
}
