import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  ApproveAllBookImportResult,
  BookImportJob,
  BookImportJobDetail,
  BookImportPageFix,
  BookImportReviewQuestion,
  BookImportQuota,
  BookImportSetup,
  ConfirmBookImportSetup,
  StartBookImportRequest,
} from '../models/book-import';
import { LabelingBankQuestion } from '../models/labeling';

/** Issue #1018 (epic #1017): kitaptan aktarım işleri. Pilot: yalnız Admin + QuestionTrainer. */
@Injectable({ providedIn: 'root' })
export class BookImportService {
  // Gateway: /api/exam/{everything} -> backend /api/{everything}
  private readonly baseUrl = '/api/exam/book-imports';
  private readonly http = inject(HttpClient);

  list(): Observable<BookImportJob[]> {
    return this.http.get<BookImportJob[]>(this.baseUrl);
  }

  get(id: string): Observable<BookImportJobDetail> {
    return this.http.get<BookImportJobDetail>(`${this.baseUrl}/${id}`);
  }

  quota(): Observable<BookImportQuota> {
    return this.http.get<BookImportQuota>(`${this.baseUrl}/quota`);
  }

  start(request: StartBookImportRequest): Observable<BookImportJob> {
    const form = new FormData();
    form.append('file', request.file, request.file.name);
    form.append('bookName', request.bookName);
    form.append('gradeId', String(request.gradeId));
    if (request.subjectId != null) form.append('subjectId', String(request.subjectId));
    return this.http.post<BookImportJob>(this.baseUrl, form);
  }

  /** Issue #1021: kurulum onay ekranı. */
  setup(id: string): Observable<BookImportSetup> {
    return this.http.get<BookImportSetup>(`${this.baseUrl}/${id}/setup`);
  }

  confirmSetup(id: string, request: ConfirmBookImportSetup): Observable<BookImportJob> {
    return this.http.post<BookImportJob>(`${this.baseUrl}/${id}/setup/confirm`, request);
  }

  cancel(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/cancel`, {});
  }

  /** Issue #1024: Admin — maliyet tavanında bekleyen işi sürdür. */
  continue(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/continue`, {});
  }

  /** Issue #1023: inceleme — testin soruları, cevap seçimi, onay ve silme. */
  reviewQuestions(id: string, testId: number): Observable<BookImportReviewQuestion[]> {
    return this.http.get<BookImportReviewQuestion[]>(`${this.baseUrl}/${id}/tests/${testId}/questions`);
  }

  setAnswer(id: string, testId: number, questionId: number, optionIndex: number): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}/tests/${testId}/questions/${questionId}/answer`, { optionIndex });
  }

  approveTest(id: string, testId: number): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/tests/${testId}/approve`, {});
  }

  approveAll(id: string): Observable<ApproveAllBookImportResult> {
    return this.http.post<ApproveAllBookImportResult>(`${this.baseUrl}/${id}/approve`, {});
  }

  removeTest(id: string, testId: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}/tests/${testId}`);
  }

  /** Issue #1023 "İşaretlemede düzelt": testin bir sayfasının taslaklarını işaretleme ekranında düzeltilenlerle değiştirir. */
  replacePage(id: string, testId: number, filePage: number, questions: LabelingBankQuestion[]): Observable<BookImportPageFix> {
    return this.http.put<BookImportPageFix>(`${this.baseUrl}/${id}/tests/${testId}/pages/${filePage}/questions`, { questions });
  }

  /** Sayfa görseli (WebP, #1020); yetkili istek olduğundan <img src> yerine blob. */
  pageImage(id: string, filePage: number): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/${id}/pages/${filePage}`, { responseType: 'blob' });
  }
}
