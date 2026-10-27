import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  LabelingBook,
  LabelingBookPageInfo,
  LabelingBookPrintedLookup,
  LabelingBookSaveRequest,
} from '../models/labeling-book';

/** Issue #577: işaretleme kitap kaydı (ofset, cevap anahtarı, içindekiler). Yalnız Admin + QuestionTrainer. */
@Injectable({ providedIn: 'root' })
export class LabelingBookService {
  // Gateway: /api/exam/{everything} -> backend /api/{everything}
  private readonly baseUrl = '/api/exam/labeling/books';
  private readonly http = inject(HttpClient);

  list(): Observable<LabelingBook[]> {
    return this.http.get<LabelingBook[]>(this.baseUrl);
  }

  create(request: LabelingBookSaveRequest): Observable<LabelingBook> {
    return this.http.post<LabelingBook>(this.baseUrl, request);
  }

  update(id: number, request: LabelingBookSaveRequest): Observable<LabelingBook> {
    return this.http.put<LabelingBook>(`${this.baseUrl}/${id}`, request);
  }

  /** Kırpma kaynağı olarak kullanılan kitap silinmez (409). */
  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  getPage(id: number, filePage: number): Observable<LabelingBookPageInfo> {
    return this.http.get<LabelingBookPageInfo>(`${this.baseUrl}/${id}/pages/${filePage}`);
  }

  /** `near`: tekrar eden basılı numaralarda bu dosya sayfasına en yakın olan önce gelir. */
  getFilePages(id: number, printedPage: number, near?: number | null): Observable<LabelingBookPrintedLookup> {
    let params = new HttpParams();
    if (near != null) {
      params = params.set('near', near);
    }
    return this.http.get<LabelingBookPrintedLookup>(`${this.baseUrl}/${id}/printed/${printedPage}`, { params });
  }
}
