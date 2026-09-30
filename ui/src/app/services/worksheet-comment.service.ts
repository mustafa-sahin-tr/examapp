import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  CreateWorksheetCommentRequest,
  WorksheetComment,
  WorksheetCommentPage,
  WorksheetCommentQuery,
  WorksheetCommentRepliesPage,
  WorksheetCommentRepliesQuery,
} from '../models/worksheet-comment.model';

/**
 * Issue #105 — worksheet / soru yorum-soru thread'leri. Gateway `/api/exam/{everything}` wildcard route'u üzerinden
 * exam API'nin `api/worksheet/{worksheetId}/comments` uçlarına gider (TestService ile aynı taban).
 */
@Injectable({ providedIn: 'root' })
export class WorksheetCommentService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/exam/worksheet';

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
    return this.http.get<WorksheetCommentRepliesPage>(`${this.commentsUrl(worksheetId)}/${rootId}/replies`, {
      params,
    });
  }

  /** Yeni kök yorum veya (tek seviye) reply. 201 + oluşturulan yorum. */
  create(worksheetId: number, request: CreateWorksheetCommentRequest): Observable<WorksheetComment> {
    return this.http.post<WorksheetComment>(this.commentsUrl(worksheetId), request);
  }

  private commentsUrl(worksheetId: number): string {
    return `${this.baseUrl}/${worksheetId}/comments`;
  }
}
