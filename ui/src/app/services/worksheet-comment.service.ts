import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  CreateWorksheetCommentRequest,
  ReportWorksheetCommentRequest,
  WorksheetComment,
  WorksheetCommentPage,
  WorksheetCommentQuery,
  WorksheetCommentRepliesPage,
  WorksheetCommentRepliesQuery,
  WorksheetCommentReportResult,
  WorksheetCommentReportsPage,
  WorksheetCommentReportsQuery,
} from '../models/worksheet-comment.model';

/**
 * Issue #105 — worksheet / soru yorum-soru thread'leri. Gateway `/api/exam/{everything}` wildcard route'u üzerinden
 * exam API'nin `api/worksheet/{worksheetId}/comments` uçlarına gider (TestService ile aynı taban).
 */
@Injectable({ providedIn: 'root' })
export class WorksheetCommentService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/exam/worksheet';
  /** Issue #305: admin'in tüm worksheet'lerdeki şikayet listesi (exam API `api/admin/comments/reports`). */
  private readonly adminReportsUrl = '/api/exam/admin/comments/reports';

  /** Kök yorumlar (yeniden eskiye) + her kökün son 5 reply'ı; `questionId` boşsa worksheet seviyesi thread. */
  getThread(worksheetId: number, query: WorksheetCommentQuery = {}): Observable<WorksheetCommentPage> {
    let params = new HttpParams();
    if (query.questionId != null) {
      params = params.set('questionId', query.questionId);
    }
    if (query.cursor) {
      params = params.set('cursor', query.cursor);
    }
    if (query.take != null) {
      params = params.set('take', query.take);
    }
    if (query.moderatorView) {
      params = params.set('moderatorView', true);
    }
    return this.http.get<WorksheetCommentPage>(this.commentsUrl(worksheetId), { params });
  }

  /** Bir kökün reply'ları — eskiden yeniye, cursor'lı. */
  getReplies(
    worksheetId: number,
    rootId: number,
    query: WorksheetCommentRepliesQuery = {}
  ): Observable<WorksheetCommentRepliesPage> {
    let params = new HttpParams();
    if (query.cursor) {
      params = params.set('cursor', query.cursor);
    }
    if (query.take != null) {
      params = params.set('take', query.take);
    }
    if (query.moderatorView) {
      params = params.set('moderatorView', true);
    }
    return this.http.get<WorksheetCommentRepliesPage>(`${this.commentsUrl(worksheetId)}/${rootId}/replies`, {
      params,
    });
  }

  /** Yeni kök yorum veya (tek seviye) reply. 201 + oluşturulan yorum. */
  create(worksheetId: number, request: CreateWorksheetCommentRequest): Observable<WorksheetComment> {
    return this.http.post<WorksheetComment>(this.commentsUrl(worksheetId), request);
  }

  /**
   * Issue #305: yorumu şikayet et. Idempotent — tekrar şikayette `alreadyReported: true` döner, yeni kayıt yazılmaz.
   * Kendi yorumu 403 `CannotReportOwnComment`.
   */
  report(
    worksheetId: number,
    commentId: number,
    request: ReportWorksheetCommentRequest
  ): Observable<WorksheetCommentReportResult> {
    const body: ReportWorksheetCommentRequest = { reason: request.reason };
    const note = request.note?.trim();
    if (note) {
      body.note = note;
    }
    return this.http.post<WorksheetCommentReportResult>(`${this.commentsUrl(worksheetId)}/${commentId}/report`, body);
  }

  /** Issue #305: yorumu gizle (neden 1..500). 200 + moderatör görünümündeki yorum. */
  hide(worksheetId: number, commentId: number, reason: string): Observable<WorksheetComment> {
    return this.http.post<WorksheetComment>(`${this.commentsUrl(worksheetId)}/${commentId}/hide`, {
      reason: reason.trim(),
    });
  }

  /** Issue #305: gizlenen yorumu yeniden görünür yap (gövdesiz). 200 + yorum. */
  unhide(worksheetId: number, commentId: number): Observable<WorksheetComment> {
    return this.http.post<WorksheetComment>(`${this.commentsUrl(worksheetId)}/${commentId}/unhide`, null);
  }

  /** Issue #305: bu worksheet'te istek sahibinin moderatörü olduğu şikayet edilmiş yorumlar (son şikayet önce). */
  getReports(worksheetId: number, query: WorksheetCommentReportsQuery = {}): Observable<WorksheetCommentReportsPage> {
    return this.http.get<WorksheetCommentReportsPage>(`${this.commentsUrl(worksheetId)}/reports`, {
      params: reportsParams(query),
    });
  }

  /** Issue #305: admin — tüm worksheet'lerdeki şikayet edilmiş yorumlar. */
  getAdminReports(query: WorksheetCommentReportsQuery = {}): Observable<WorksheetCommentReportsPage> {
    return this.http.get<WorksheetCommentReportsPage>(this.adminReportsUrl, { params: reportsParams(query) });
  }

  private commentsUrl(worksheetId: number): string {
    return `${this.baseUrl}/${worksheetId}/comments`;
  }
}

function reportsParams(query: WorksheetCommentReportsQuery): HttpParams {
  let params = new HttpParams();
  if (query.page != null) {
    params = params.set('page', query.page);
  }
  if (query.pageSize != null) {
    params = params.set('pageSize', query.pageSize);
  }
  return params;
}
