import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import {
  AdminPlacementTest,
  PlacementAnswer,
  PlacementQuestion,
  PlacementReport,
  PlacementStatus,
} from '../models/placement.model';

/** Issue #1114: seviye tespiti — öğrenci akışı, velinin raporu ve admin test yönetimi. */
@Injectable({ providedIn: 'root' })
export class PlacementService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/exam/placement';
  private readonly adminBase = '/api/exam/admin/placement-tests';

  status(): Observable<PlacementStatus> {
    return this.http.get<PlacementStatus>(`${this.base}/status`);
  }

  questions(): Observable<PlacementQuestion[]> {
    return this.http.get<PlacementQuestion[]>(`${this.base}/questions`);
  }

  submit(answers: PlacementAnswer[]): Observable<PlacementReport> {
    return this.http.post<PlacementReport>(`${this.base}/submit`, { answers });
  }

  report(): Observable<PlacementReport> {
    return this.http.get<PlacementReport>(`${this.base}/report`);
  }

  /** Çözülmemişse null. */
  childReport(studentId: number): Observable<PlacementReport | null> {
    return this.http
      .get<{ report: PlacementReport | null }>(`/api/exam/parent/children/${studentId}/placement`)
      .pipe(map((res) => res.report));
  }

  adminList(): Observable<AdminPlacementTest[]> {
    return this.http.get<AdminPlacementTest[]>(this.adminBase);
  }

  adminGenerate(gradeId: number): Observable<AdminPlacementTest> {
    return this.http.post<AdminPlacementTest>(`${this.adminBase}/${gradeId}/generate`, {});
  }

  adminSetPublished(gradeId: number, published: boolean): Observable<AdminPlacementTest> {
    return this.http.put<AdminPlacementTest>(`${this.adminBase}/${gradeId}/published`, { published });
  }
}
