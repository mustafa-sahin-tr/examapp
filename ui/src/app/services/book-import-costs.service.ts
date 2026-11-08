import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { BookImportCostFilter, BookImportCostReport } from '../models/book-import';

/** Issue #1056: kitap aktarımı ve hızlı ekleme AI maliyet raporu (yalnız Admin). */
@Injectable({ providedIn: 'root' })
export class BookImportCostsService {
  // Gateway: /api/exam/{everything} -> backend /api/{everything}
  private readonly baseUrl = '/api/exam/admin/book-import-costs';
  private readonly http = inject(HttpClient);

  report(filter: BookImportCostFilter, page: number, pageSize: number): Observable<BookImportCostReport> {
    const params = this.filterParams(filter).set('page', page).set('pageSize', pageSize);
    return this.http.get<BookImportCostReport>(this.baseUrl, { params });
  }

  exportJobs(filter: BookImportCostFilter): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/jobs.csv`, { params: this.filterParams(filter), responseType: 'blob' });
  }

  private filterParams(filter: BookImportCostFilter): HttpParams {
    let params = new HttpParams().set('from', filter.from).set('to', filter.to);
    if (filter.kind) params = params.set('kind', filter.kind);
    if (filter.userId != null) params = params.set('userId', filter.userId);
    return params;
  }
}
