import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, map, tap } from 'rxjs';
import {
  StudentSchoolDecisionResponse,
  StudentSchoolRequestPage,
} from '../models/student-school-request.model';

/**
 * Issue #361: bekleyen öğrenci okul başvuruları (platform admin + okulun onaylı öğretmeni).
 * Gateway üzerinden `/api/exam/student-school-requests` (`/api/exam/{everything}` wildcard). Kapsam sunucuda:
 * öğretmen yalnız kendi okulunu görür; başka okulun öğrencisine karar → 404.
 */
@Injectable({ providedIn: 'root' })
export class StudentSchoolRequestService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/exam/student-school-requests';

  /** Menü rozeti/başlık için bekleyen başvuru sayısı. */
  readonly pendingCount = signal(0);

  list(page = 1, pageSize = 20): Observable<StudentSchoolRequestPage> {
    const params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));
    return this.http
      .get<StudentSchoolRequestPage>(this.baseUrl, { params })
      .pipe(tap((res) => this.pendingCount.set(res?.totalCount ?? 0)));
  }

  refreshPendingCount(): Observable<number> {
    return this.http.get<{ count: number }>(`${this.baseUrl}/count`).pipe(
      map((res) => res?.count ?? 0),
      tap((count) => this.pendingCount.set(count))
    );
  }

  approve(studentId: number): Observable<StudentSchoolDecisionResponse> {
    return this.http.post<StudentSchoolDecisionResponse>(`${this.baseUrl}/${studentId}/approve`, {});
  }

  reject(studentId: number): Observable<StudentSchoolDecisionResponse> {
    return this.http.post<StudentSchoolDecisionResponse>(`${this.baseUrl}/${studentId}/reject`, {});
  }
}
