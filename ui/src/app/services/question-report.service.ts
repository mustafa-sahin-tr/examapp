import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  CreateQuestionReport,
  QuestionReportCreated,
  QuestionReportListStatus,
  QuestionReportPage,
} from '../models/question-report';

/** Issue #781: öğrencinin soru bildirimi ve soru sahibinin listesi. */
@Injectable({ providedIn: 'root' })
export class QuestionReportService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = '/api/exam/questions';

  create(questionId: number, report: CreateQuestionReport): Observable<QuestionReportCreated> {
    return this.http.post<QuestionReportCreated>(`${this.apiUrl}/${questionId}/report`, report);
  }

  list(status: QuestionReportListStatus, page: number, pageSize: number): Observable<QuestionReportPage> {
    const params = new HttpParams().set('status', status).set('page', page).set('pageSize', pageSize);
    return this.http.get<QuestionReportPage>(`${this.apiUrl}/reports`, { params });
  }

  markReviewed(id: number): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/reports/${id}/reviewed`, {});
  }
}
