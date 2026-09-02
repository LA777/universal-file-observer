import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApplicationVersion } from '../models/models';

@Injectable({ providedIn: 'root' })
export class VersionService {
  private apiUrl = '/api/version';

  constructor(private http: HttpClient) {}

  /**
   * The version of the build serving this page - three segments,
   * major.minor.patch. Asked of the server rather than compiled into the bundle
   * so the number can never lag behind the API it belongs to; the server reads
   * it from <Version> in Ufo.Server.csproj.
   */
  getVersion(): Observable<ApplicationVersion> {
    return this.http.get<ApplicationVersion>(this.apiUrl);
  }
}
