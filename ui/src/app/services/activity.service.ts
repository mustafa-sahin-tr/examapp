import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ActivityCheckAnswer, ActivityCheckResult, ActivitySolutionsResult } from '../models/activity';

/**
 * Issue #571/#573: etkinlik kontrol ve çözüm uçları (gateway `/api/exam/test-instances/...`). Cevap kaydı, başlatma ve
 * bitirme mevcut `TestService` uçlarıyla yapılır (save-answer, start-test, end-test).
 */
@Injectable({ providedIn: 'root' })
export class ActivityService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/exam/test-instances';

  /**
   * "Cevapları kontrol et": etkinliğin TÜM maddeleri (1–100) gönderilir; cevaplar da kaydedilir. Hatalar:
   * 400 `InvalidCheckRequest`/`InvalidAnswerPayload`, 404, 409 `NotAnActivity`/`TestNotInProgress`, 429 `RateLimited`.
   */
  check(instanceId: number, answers: ActivityCheckAnswer[]): Observable<ActivityCheckResult> {
    return this.http.post<ActivityCheckResult>(`${this.baseUrl}/${instanceId}/check`, { answers });
  }

  /** "Doğru cevapları göster": ilk kontrolden önce 409 `ActivityCheckRequired`. */
  getSolutions(instanceId: number): Observable<ActivitySolutionsResult> {
    return this.http.get<ActivitySolutionsResult>(`${this.baseUrl}/${instanceId}/solutions`);
  }
}
