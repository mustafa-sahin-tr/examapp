import { InjectionToken } from '@angular/core';
import { Observable } from 'rxjs';
import { Paged } from '../models/test-instance';
import {
  TeacherApplicationActionResult,
  TeacherApplicationDetail,
  TeacherApplicationListItem,
  TeacherApplicationListQuery,
} from '../models/teacher-application.model';

/**
 * Issue #1277: öğretmen başvurusu uçları — platform admin (`AdminService`, tüm başvurular) ya da okul yöneticisi
 * (`SchoolAdminService`, yalnız kendi okulu). Onay sayfası hangisiyle çalışacağını rota verisinden seçer.
 */
export interface TeacherApplicationsApi {
  getTeacherApplications(query: TeacherApplicationListQuery): Observable<Paged<TeacherApplicationListItem>>;
  getTeacherApplication(teacherId: number): Observable<TeacherApplicationDetail>;
  approveTeacherApplication(teacherId: number): Observable<TeacherApplicationActionResult>;
  rejectTeacherApplication(teacherId: number, reason: string): Observable<TeacherApplicationActionResult>;
}

/** Rota `data` anahtarı: `true` → okul yöneticisi kapsamı. */
export const SCHOOL_ADMIN_SCOPE_DATA_KEY = 'schoolAdminScope';

export const TEACHER_APPLICATIONS_API = new InjectionToken<TeacherApplicationsApi>('TEACHER_APPLICATIONS_API');
