import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { TopicPerformance } from '../models/topic-performance.model';

/** Sorgu penceresi. */
export interface TopicPerformanceWindow {
  days: number;
  minQuestions: number;
}

/**
 * Issue #521: konu performansı (#497, #498). Gateway: /api/exam/{everything} -> backend /api/{everything}.
 * Öğrenci kimliği kendi ucunda token'dan gelir; velide yetki sunucuda (yalnız Active bağlantı, diğer her durum 404).
 */
@Injectable({ providedIn: 'root' })
export class TopicPerformanceService {
  private readonly http = inject(HttpClient);

  getMine(window: TopicPerformanceWindow): Observable<TopicPerformance> {
    return this.http.get<TopicPerformance>('/api/exam/student/me/topic-performance', { params: this.params(window) });
  }

  getChild(studentId: number, window: TopicPerformanceWindow): Observable<TopicPerformance> {
    return this.http.get<TopicPerformance>(`/api/exam/parent/children/${studentId}/topic-performance`, {
      params: this.params(window),
    });
  }

  private params(window: TopicPerformanceWindow): HttpParams {
    return new HttpParams().set('days', window.days).set('groupBy', 'topic').set('minQuestions', window.minQuestions);
  }
}
