import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, map } from 'rxjs';
import {
  FileSystemSearchCriteria,
  FsSearchResult,
  IndexedSearchResponse,
  SearchCriteria,
} from '../models/models';

@Injectable({ providedIn: 'root' })
export class SearchService {
  constructor(private http: HttpClient) {}

  /** Search indexed snapshot data; the API answers 204 (empty body) when nothing matches. */
  searchIndexed(criteria: SearchCriteria): Observable<IndexedSearchResponse> {
    return this.http
      .post<IndexedSearchResponse | null>('/api/search', criteria)
      .pipe(map((response) => response ?? { files: [], folders: [] }));
  }

  /** Live search of the local file system. */
  searchFileSystem(criteria: FileSystemSearchCriteria): Observable<FsSearchResult[]> {
    return this.http.post<FsSearchResult[]>('/api/filesystem/search', criteria);
  }
}
