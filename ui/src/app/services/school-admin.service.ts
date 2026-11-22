import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { Paged } from '../models/test-instance';
import {
  TeacherApplicationActionResult,
  TeacherApplicationDetail,
  TeacherApplicationListItem,
  TeacherApplicationListQuery,
  TeacherRejectRequest,
} from '../models/teacher-application.model';
import { TeacherApplicationsApi } from './teacher-applications-api';

/** Issue #1277: `GET /api/exam/school-admin/summary`. */
export interface SchoolAdminSummary {
  schoolId: number;
  pendingTeacherApplications: number;
}

/**
 * Issue #1277 (#1071 S2): okul yöneticisinin kendi okulunun öğretmen başvuruları. Gateway `/api/exam/{everything}`.
 * Yetki sunucuda DB'den çözülür (yönetici değil → 403, başka okulun başvurusu → 404); uçlar platform admin uçlarıyla
 * aynı şekli döner, bu yüzden onay sayfası (`TeacherApprovalsComponent`) iki kapsamda da kullanılır.
 */
@Injectable({ providedIn: 'root' })
export class SchoolAdminService implements TeacherApplicationsApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/exam/school-admin';

  /** Menü rozeti: bekleyen öğretmen başvurusu sayısı. */
  readonly pendingTeacherApplications = signal(0);

  refreshSummary(): Observable<SchoolAdminSummary> {
    return this.http
      .get<SchoolAdminSummary>(`${this.baseUrl}/summary`)
      .pipe(tap((s) => this.pendingTeacherApplications.set(s?.pendingTeacherApplications ?? 0)));
  }

  getTeacherApplications(query: TeacherApplicationListQuery): Observable<Paged<TeacherApplicationListItem>> {
    const params = new HttpParams().set('status', query.status).set('page', query.page).set('pageSize', query.pageSize);
    return this.http.get<Paged<TeacherApplicationListItem>>(`${this.baseUrl}/teacher-applications`, { params }).pipe(
      tap((page) => {
        if (query.status === 'pending') {
          this.pendingTeacherApplications.set(page?.totalCount ?? 0);
        }
      }),
    );
  }

  getTeacherApplication(teacherId: number): Observable<TeacherApplicationDetail> {
    return this.http.get<TeacherApplicationDetail>(`${this.baseUrl}/teacher-applications/${teacherId}`);
  }

  approveTeacherApplication(teacherId: number): Observable<TeacherApplicationActionResult> {
    return this.http
      .post<TeacherApplicationActionResult>(`${this.baseUrl}/teacher-applications/${teacherId}/approve`, {})
      .pipe(tap(() => this.decrementPending()));
  }

  rejectTeacherApplication(teacherId: number, reason: string): Observable<TeacherApplicationActionResult> {
    const body: TeacherRejectRequest = { reason };
    return this.http
      .post<TeacherApplicationActionResult>(`${this.baseUrl}/teacher-applications/${teacherId}/reject`, body)
      .pipe(tap(() => this.decrementPending()));
  }

  private decrementPending(): void {
    this.pendingTeacherApplications.update((n) => Math.max(0, n - 1));
  }
}

