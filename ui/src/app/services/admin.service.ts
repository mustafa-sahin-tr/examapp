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
  TeacherApplicationActionResult,
  TeacherApplicationDetail,
  TeacherApplicationListItem,
  TeacherApplicationListQuery,
  TeacherRejectRequest,
} from '../models/teacher-application.model';
import { AdminTeacherListItem, AdminTeacherListQuery } from '../models/admin-teacher.model';
import { AdminStudentListItem, AdminStudentListQuery } from '../models/admin-student.model';
import { AdminSchoolPagedQuery } from '../models/admin-paged-query.model';
import { AdminPasswordResetResponse, AdminPasswordResetTarget } from '../models/admin-password-reset.model';
import { AdminAccountStatusResponse, AdminAccountTarget } from '../models/admin-account-status.model';
import { AdminStudentSchoolRequest, AdminStudentSchoolResponse } from '../models/admin-student-school.model';
import { Paged } from '../models/test-instance';

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
  /**
   * Issue #187: sayfalı başvuru listesi. `status=pending` → yalnız bekleyenler (en eski önce); `status=all` → önce
   * bekleyenler, sonra karar verilmişler (en yeni karar önce). E-posta maskeli (#262); 429 rate limit (`Retry-After`).
   */
  getTeacherApplications(query: TeacherApplicationListQuery): Observable<Paged<TeacherApplicationListItem>> {
    const params = new HttpParams()
      .set('status', query.status)
      .set('page', query.page)
      .set('pageSize', query.pageSize);
    return this.http.get<Paged<TeacherApplicationListItem>>(`${this.baseUrl}/teacher-applications`, { params });
  }
  /**
   * Issue #262: tek başvurunun detayı, TAM e-posta ile (listede maskeli). Her çağrı audit'lenir; issue #187: her
   * durumdaki başvuru için döner, 404 başvuru yok; 429 rate limit (`Retry-After`, liste uçlarıyla ortak kova).
   */
  getTeacherApplication(teacherId: number): Observable<TeacherApplicationDetail> {
    return this.http.get<TeacherApplicationDetail>(`${this.baseUrl}/teacher-applications/${teacherId}`);
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

  // ---- öğretmen listesi (Issue #152) ----
  /**
   * Sayfalı öğretmen listesi. `schoolId` ve `unassigned` birlikte gönderilmez (backend 400);
   * ikisi birden verilirse `unassigned` önceliklidir.
   */
  getTeachers(query: AdminTeacherListQuery): Observable<Paged<AdminTeacherListItem>> {
    return this.http.get<Paged<AdminTeacherListItem>>(`${this.baseUrl}/teachers`, {
      params: schoolPagedParams(query),
    });
  }

  // ---- öğrenci listesi (Issue #153) ----
  /**
   * Sayfalı öğrenci listesi. `schoolId` ve `unassigned` birlikte gönderilmez (backend 400);
   * ikisi birden verilirse `unassigned` önceliklidir.
   */
  getStudents(query: AdminStudentListQuery): Observable<Paged<AdminStudentListItem>> {
    return this.http.get<Paged<AdminStudentListItem>>(`${this.baseUrl}/students`, {
      params: schoolPagedParams(query),
    });
  }

  // ---- şifre sıfırlama (Issue #156) ----
  /**
   * Hedefin Keycloak şifresini geçici bir şifreyle değiştirir (ilk girişte değiştirme zorunlu) ve tüm
   * oturumlarını kapatır. Yanıttaki şifre SAKLANMAZ — yalnızca çağırana (sonuç dialog'u) iletilir.
   * Hatalar: 403 kendi hesabı/korumalı rol (`{ message }`) veya yetkisiz (gövdesiz), 404 kayıt/hesap yok,
   * 429 rate limit (`Retry-After`), 502 kimlik sunucusu hatası (şifre değişip oturumlar kapatılamadıysa da 502,
   * ayrı `{ message }` ile — şifre dönmez). `id` = Teacher.Id / Student.Id (admin listesindeki `id`).
   */
  resetPassword(target: AdminPasswordResetTarget, id: number): Observable<AdminPasswordResetResponse> {
    const segment = target === 'teacher' ? 'teachers' : 'students';
    return this.http.post<AdminPasswordResetResponse>(`${this.baseUrl}/${segment}/${id}/reset-password`, null);
  }

  /**
   * Issue #155 — hesabı etkinleştirir / devre dışı bırakır (Keycloak). Devre dışı bırakmada kullanıcının tüm açık
   * oturumları da kapatılır. Yanıt: yeni durum `{ enabled }`. Hatalar `{ message }`: 403 (kendisi / yönetici-servis
   * hesabı), 404, 429 (`Retry-After`), 502 (kimlik sunucusu; hesap kapanıp oturumlar kapatılamadıysa da 502).
   */
  setAccountStatus(target: AdminAccountTarget, id: number, enabled: boolean): Observable<AdminAccountStatusResponse> {
    const segment = target === 'teacher' ? 'teachers' : 'students';
    return this.http.patch<AdminAccountStatusResponse>(`${this.baseUrl}/${segment}/${id}/account-status`, { enabled });
  }

  /**
   * Issue #277 (madde 8) — öğrencinin (Student.Id) okulunu değiştirir; öğrenci okulu kayıttan sonra kilitlidir (#259).
   * Hatalar: 400, 403, 404, 409 (eşzamanlı değişiklik → listeyi yenileyip tekrar dene), 429 (`Retry-After`), 502.
   */
  changeStudentSchool(studentId: number, schoolId: number): Observable<AdminStudentSchoolResponse> {
    const body: AdminStudentSchoolRequest = { schoolId };
    return this.http.put<AdminStudentSchoolResponse>(`${this.baseUrl}/students/${studentId}/school`, body);
  }

  // ---- classifier cache ----
  getClassifierCache(): Observable<ClassifierCacheStatus> {
    return this.http.get<ClassifierCacheStatus>(`${this.baseUrl}/classifier-cache`);
  }
  refreshClassifierCache(): Observable<ClassifierCacheRefreshResult> {
    return this.http.post<ClassifierCacheRefreshResult>(`${this.baseUrl}/classifier-cache/refresh`, {});
  }
}

/** Okul filtreli admin listeleri için query param'ları; `unassigned` varsa `schoolId` gönderilmez. */
function schoolPagedParams(query: AdminSchoolPagedQuery): HttpParams {
  let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
  if (query.unassigned) params = params.set('unassigned', 'true');
  else if (query.schoolId != null) params = params.set('schoolId', query.schoolId);
  return params;
}
