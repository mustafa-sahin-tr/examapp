import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  ParentAssignmentStatus,
  ParentChildAssignmentList,
  ParentChildProgress,
  ParentChildSchedule,
  ParentChildSummary,
  ParentChildTestResult,
} from '../models/parent-dashboard.model';

/** Veli paneli (issue #420, #421, #422). Yetki sunucuda: yalnızca Active bağlantı; diğer her durum 404. */
@Injectable({ providedIn: 'root' })
export class ParentDashboardService {
  // Gateway: /api/exam/{everything} -> backend /api/{everything}
  private readonly baseUrl = '/api/exam/parent/children';
  private readonly http = inject(HttpClient);

  getChildSummary(studentId: number): Observable<ParentChildSummary> {
    return this.http.get<ParentChildSummary>(`${this.baseUrl}/${studentId}/summary`);
  }

  /** Issue #421: ödev/test listesi (sayfa başına 20, en yeni teslim tarihi önce). `status` null = hepsi. */
  getChildAssignments(
    studentId: number,
    status: ParentAssignmentStatus | null,
    page = 1
  ): Observable<ParentChildAssignmentList> {
    let params = new HttpParams().set('page', page);
    if (status) params = params.set('status', status);
    return this.http.get<ParentChildAssignmentList>(`${this.baseUrl}/${studentId}/assignments`, { params });
  }

  /** Issue #421: bitmiş bir testin özeti (yalnızca sayılar; soru/şık içeriği yok). */
  getChildTestResult(studentId: number, testInstanceId: number): Observable<ParentChildTestResult> {
    return this.http.get<ParentChildTestResult>(`${this.baseUrl}/${studentId}/test-results/${testInstanceId}`);
  }

  /** Issue #422: puan, seviye, haftalık puan, rozetler ve çocuğun kendi sırası. */
  getChildProgress(studentId: number): Observable<ParentChildProgress> {
    return this.http.get<ParentChildProgress>(`${this.baseUrl}/${studentId}/progress`);
  }

  /**
   * Issue #422: planlar + ders randevuları. `from`/`to` "yyyy-MM-dd" (yerel günler, dahil, en fazla 31 gün); ikisi de
   * verilmezse sunucu bu haftayı (Europe/Istanbul) döner.
   */
  getChildSchedule(studentId: number, range?: { from: string; to: string }): Observable<ParentChildSchedule> {
    const params = range ? new HttpParams().set('from', range.from).set('to', range.to) : undefined;
    return this.http.get<ParentChildSchedule>(`${this.baseUrl}/${studentId}/schedule`, { params });
  }
}
