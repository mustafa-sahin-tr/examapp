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

/** Issue #1277 / #1278: `GET /api/exam/school-admin/summary` (okul paneli kartları + menü rozeti). */
export interface SchoolAdminSummary {
  schoolId: number;
  schoolName: string;
  teacherCount: number;
  studentCount: number;
  pendingTeacherApplications: number;
  pendingStudentRequests: number;
}

/** Issue #1278: `GET /api/exam/school-admin/teachers` satırı. */
export interface SchoolAdminTeacher {
  teacherId: number;
  fullName: string;
  isSchoolAdmin: boolean;
  isSuspended: boolean;
  approvedAt: string | null;
  subjects: string[];
}

/** Issue #1278: `GET /api/exam/school-admin/students` satırı (numara maskeli). */
export interface SchoolAdminStudent {
  studentId: number;
  fullName: string;
  studentNumber: string;
  gradeId: number | null;
  gradeName: string | null;
  verifiedAt: string | null;
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

  getTeachers(page: number, pageSize: number): Observable<Paged<SchoolAdminTeacher>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<Paged<SchoolAdminTeacher>>(`${this.baseUrl}/teachers`, { params });
  }

  getStudents(page: number, pageSize: number, gradeId: number | null): Observable<Paged<SchoolAdminStudent>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (gradeId != null) {
      params = params.set('gradeId', gradeId);
    }
    return this.http.get<Paged<SchoolAdminStudent>>(`${this.baseUrl}/students`, { params });
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

