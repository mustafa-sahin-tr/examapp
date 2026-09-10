import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  ApiResult,
  ClassifierCacheRefreshResult,
  ClassifierCacheStatus,
  DistrictDto,
  ProvinceDto,
  School,
  TaxonomyFilter,
  TaxonomyTree,
} from '../models/taxonomy';
import { AdminDashboardSummary, AdminDashboardTrends } from '../models/admin-dashboard.model';
import {
  PendingTeacherApplication,
  TeacherApplicationActionResult,
  TeacherRejectRequest,
} from '../models/teacher-application.model';

interface UpsertSubject {
  name: string;
  /** null/undefined → GradeSubject'e dokunulmaz; dizi → backend tam senkron yapar. */
  gradeIds?: number[] | null;
}
interface UpsertTopic {
  name: string;
  subjectId: number;
  gradeId: number;
}
interface UpsertSubTopic {
  name: string;
  topicId: number;
}
/** Backend UpsertSchoolDto (Issue #91). İlçe verilirse il de zorunlu; addressLine ≤ 500 karakter. */
export interface UpsertSchool {
  name: string;
  provinceId?: number | null;
  districtId?: number | null;
  addressLine?: string | null;
}

@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/exam/admin';

  // ---- taxonomy ----
  getTaxonomy(filter?: TaxonomyFilter): Observable<TaxonomyTree> {
    let params = new HttpParams();
    if (filter?.gradeId != null) params = params.set('gradeId', filter.gradeId);
    if (filter?.unassigned) params = params.set('unassigned', 'true');
    return this.http.get<TaxonomyTree>(`${this.baseUrl}/taxonomy`, { params });
  }

  /** Dersi sınıfa bağlar (idempotent). */
  addSubjectGrade(subjectId: number, gradeId: number) {
    return this.http.post<ApiResult>(`${this.baseUrl}/subjects/${subjectId}/grades/${gradeId}`, {});
  }
  /** Ders–sınıf bağlantısını kaldırır (idempotent; ders/konu/soru verisi silinmez). */
  removeSubjectGrade(subjectId: number, gradeId: number) {
    return this.http.delete<ApiResult>(`${this.baseUrl}/subjects/${subjectId}/grades/${gradeId}`);
  }

  createSubject(body: UpsertSubject) {
    return this.http.post<ApiResult>(`${this.baseUrl}/subjects`, body);
  }
  updateSubject(id: number, body: UpsertSubject) {
    return this.http.put<ApiResult>(`${this.baseUrl}/subjects/${id}`, body);
  }
  deleteSubject(id: number) {
    return this.http.delete<ApiResult>(`${this.baseUrl}/subjects/${id}`);
  }

  createTopic(body: UpsertTopic) {
    return this.http.post<ApiResult>(`${this.baseUrl}/topics`, body);
  }
  updateTopic(id: number, body: UpsertTopic) {
    return this.http.put<ApiResult>(`${this.baseUrl}/topics/${id}`, body);
  }
  deleteTopic(id: number) {
    return this.http.delete<ApiResult>(`${this.baseUrl}/topics/${id}`);
  }

  createSubTopic(body: UpsertSubTopic) {
    return this.http.post<ApiResult>(`${this.baseUrl}/subtopics`, body);
  }
  updateSubTopic(id: number, body: UpsertSubTopic) {
    return this.http.put<ApiResult>(`${this.baseUrl}/subtopics/${id}`, body);
  }
  deleteSubTopic(id: number) {
    return this.http.delete<ApiResult>(`${this.baseUrl}/subtopics/${id}`);
  }

  // ---- schools ----
  getSchools(): Observable<School[]> {
    return this.http.get<School[]>(`${this.baseUrl}/schools`);
  }
  createSchool(body: UpsertSchool) {
    return this.http.post<ApiResult>(`${this.baseUrl}/schools`, body);
  }
  updateSchool(id: number, body: UpsertSchool) {
    return this.http.put<ApiResult>(`${this.baseUrl}/schools/${id}`, body);
  }
  deleteSchool(id: number) {
    return this.http.delete<ApiResult>(`${this.baseUrl}/schools/${id}`);
  }

  // ---- location (Issue #91) ----
  getProvinces(): Observable<ProvinceDto[]> {
    return this.http.get<ProvinceDto[]>(`${this.baseUrl}/provinces`);
  }
  getDistricts(provinceId: number): Observable<DistrictDto[]> {
    return this.http.get<DistrictDto[]>(`${this.baseUrl}/districts`, {
      params: { provinceId },
    });
  }

  // ---- dashboard (Issue #86 / #88) ----
  getDashboardSummary(): Observable<AdminDashboardSummary> {
    return this.http.get<AdminDashboardSummary>(`${this.baseUrl}/dashboard/summary`);
  }

  /** Son `days` günün (1..365, varsayılan 30) günlük soru oluşturma / çözme serileri. */
  getDashboardTrends(days = 30): Observable<AdminDashboardTrends> {
    return this.http.get<AdminDashboardTrends>(`${this.baseUrl}/dashboard/trends`, {
      params: { days },
    });
  }

  // ---- bağımsız öğretmen başvuruları (Issue #94) ----
  /** Pending durumdaki bağımsız öğretmen başvuruları, en eski önce. */
  getPendingTeacherApplications(): Observable<PendingTeacherApplication[]> {
    return this.http.get<PendingTeacherApplication[]>(`${this.baseUrl}/teacher-applications`);
  }
  /** 404 kayıt yok, 409 zaten karar verilmiş. */
  approveTeacherApplication(teacherId: number): Observable<TeacherApplicationActionResult> {
    return this.http.post<TeacherApplicationActionResult>(
      `${this.baseUrl}/teacher-applications/${teacherId}/approve`,
      {},
    );
  }
  /** reason zorunlu (1..500); 400 eksik/uzun, 404 kayıt yok, 409 zaten karar verilmiş. */
  rejectTeacherApplication(teacherId: number, reason: string): Observable<TeacherApplicationActionResult> {
    const body: TeacherRejectRequest = { reason };
    return this.http.post<TeacherApplicationActionResult>(
      `${this.baseUrl}/teacher-applications/${teacherId}/reject`,
      body,
    );
  }

  // ---- classifier cache ----
  getClassifierCache(): Observable<ClassifierCacheStatus> {
    return this.http.get<ClassifierCacheStatus>(`${this.baseUrl}/classifier-cache`);
  }
  refreshClassifierCache(): Observable<ClassifierCacheRefreshResult> {
    return this.http.post<ClassifierCacheRefreshResult>(`${this.baseUrl}/classifier-cache/refresh`, {});
  }
}
