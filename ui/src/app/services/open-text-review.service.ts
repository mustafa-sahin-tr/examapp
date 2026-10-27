import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  OpenTextQueueQuery,
  OpenTextReviewPage,
  OpenTextReviewRequest,
  OpenTextReviewResult,
} from '../models/open-text';

/**
 * Issue #583: açık uçlu cevapların öğretmen değerlendirme kuyruğu (gateway `/api/exam/open-text-reviews`). Yalnız Teacher (onaylı)
 * ve Admin; kapsam sunucuda (öğretmen kendi atamasıyla hedeflediği öğrenciler, Admin hepsi). Öğrenci kendi değerlendirmesini
 * oturum / kontrol yanıtında görür, bu uçlara erişemez.
 */
@Injectable({ providedIn: 'root' })
export class OpenTextReviewService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/exam/open-text-reviews';

  /** Kuyruk (sayfalı): pending en eski cevaptan, diğerleri en yeniden. 400 `OpenTextInvalidReview` geçersiz sorgu. */
  list(query: OpenTextQueueQuery): Observable<OpenTextReviewPage> {
    let params = new HttpParams()
      .set('status', query.status)
      .set('page', String(query.page))
      .set('pageSize', String(query.pageSize));
    if (query.worksheetId != null && query.worksheetId > 0) {
      params = params.set('worksheetId', String(query.worksheetId));
    }
    return this.http.get<OpenTextReviewPage>(this.baseUrl, { params });
  }

  /**
   * Değerlendir / düzelt. 400 `OpenTextInvalidReview`, 404 `OpenTextReviewNotFound`, 409 `OpenTextNotSubmitted` /
   * `OpenTextAnswerChanged` (öğrenci metni değişti — listeyi yenile), 429 `RateLimited`.
   */
  review(instanceQuestionId: number, body: OpenTextReviewRequest): Observable<OpenTextReviewResult> {
    return this.http.put<OpenTextReviewResult>(`${this.baseUrl}/${instanceQuestionId}`, body);
  }
}
