import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Paged } from '../models/test-instance';

/** Issue #1279: `GET /api/exam/school-sections` satırı. */
export interface SchoolSection {
  id: number;
  gradeId: number;
  gradeName: string;
  name: string;
  studentCount: number;
}

/** Issue #1279: şubedeki öğrenci (numara maskeli). */
export interface SchoolSectionStudent {
  studentId: number;
  fullName: string;
  studentNumber: string;
  since: string;
}

export interface SchoolSectionAssignResult {
  assigned: number;
  unchanged: number;
}

/**
 * Issue #1279 (#1071 S4): okul şubeleri. Okuma `api/school-sections` (okulun onaylı her öğretmeni), yönetim
 * `api/school-admin/sections` (yalnız okul yöneticisi). Okul ve yetki sunucuda DB'den çözülür.
 */
@Injectable({ providedIn: 'root' })
export class SchoolSectionService {
  private readonly http = inject(HttpClient);
  private readonly readUrl = '/api/exam/school-sections';
  private readonly adminUrl = '/api/exam/school-admin/sections';

  list(gradeId: number | null = null): Observable<SchoolSection[]> {
    const params = gradeId != null ? new HttpParams().set('gradeId', gradeId) : undefined;
    return this.http.get<SchoolSection[]>(this.readUrl, { params });
  }

  students(sectionId: number, page: number, pageSize: number): Observable<Paged<SchoolSectionStudent>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<Paged<SchoolSectionStudent>>(`${this.readUrl}/${sectionId}/students`, { params });
  }

  create(gradeId: number, name: string): Observable<SchoolSection> {
    return this.http.post<SchoolSection>(this.adminUrl, { gradeId, name });
  }

  rename(sectionId: number, name: string): Observable<SchoolSection> {
    return this.http.put<SchoolSection>(`${this.adminUrl}/${sectionId}`, { name });
  }

  delete(sectionId: number): Observable<void> {
    return this.http.delete<void>(`${this.adminUrl}/${sectionId}`);
  }

  assign(sectionId: number, studentIds: number[]): Observable<SchoolSectionAssignResult> {
    return this.http.post<SchoolSectionAssignResult>(`${this.adminUrl}/${sectionId}/students`, { studentIds });
  }

  remove(sectionId: number, studentId: number): Observable<void> {
    return this.http.delete<void>(`${this.adminUrl}/${sectionId}/students/${studentId}`);
  }
}
