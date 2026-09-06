import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import {
  CreateWorksheetAccessRequest,
  ResponseBase,
  WorksheetAccessRequest,
} from '../models/worksheet-access-request.model';

/**
 * Atama izni request/approve akışı (issue #13, Epic #5-E).
 * Tüm çağrılar gateway üzerinden (`/api/exam/worksheet/...`); backend `[Authorize(Roles="Teacher")]`.
 */
@Injectable({ providedIn: 'root' })
export class WorksheetAccessRequestService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/exam/worksheet/access-requests';

  /** Sahibin gelen talepleri (yönetim ekranı). */
  readonly incomingRequests = signal<WorksheetAccessRequest[]>([]);
  /** Menü rozeti için bekleyen talep sayısı. */
  readonly pendingCount = signal(0);

  /** Yeni talep oluştur. 409 (bekleyen talep) / 400 (geçersiz) `HttpErrorResponse.error` üzerinden gelir. */
  createRequest(body: CreateWorksheetAccessRequest): Observable<ResponseBase> {
    return this.http.post<ResponseBase>(this.baseUrl, body);
  }

  /** Gelen talepleri çeker ve `incomingRequests` signal'ını günceller. */
  loadIncoming(includeDecided = false): Observable<WorksheetAccessRequest[]> {
    const params = new HttpParams().set('includeDecided', String(includeDecided));
    return this.http
      .get<WorksheetAccessRequest[]>(`${this.baseUrl}/incoming`, { params })
      .pipe(tap((rows) => this.incomingRequests.set(rows ?? [])));
  }

  /** Bekleyen talep sayısını çeker ve `pendingCount` signal'ını günceller. */
  refreshPendingCount(): Observable<number> {
    return this.http
      .get<number>(`${this.baseUrl}/incoming/count`)
      .pipe(tap((count) => this.pendingCount.set(count ?? 0)));
  }

  approve(id: number): Observable<ResponseBase> {
    return this.http.post<ResponseBase>(`${this.baseUrl}/${id}/approve`, null);
  }

  reject(id: number): Observable<ResponseBase> {
    return this.http.post<ResponseBase>(`${this.baseUrl}/${id}/reject`, null);
  }

  /** Verilmiş bir atama iznini geri alır. */
  revokeGrant(worksheetId: number, teacherUserId: number): Observable<ResponseBase> {
    const params = new HttpParams()
      .set('worksheetId', String(worksheetId))
      .set('teacherUserId', String(teacherUserId));
    return this.http.delete<ResponseBase>('/api/exam/worksheet/access-grants', { params });
  }
}
