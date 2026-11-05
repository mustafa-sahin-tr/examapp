import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { LearningOutcomeMastery } from '../models/learning-outcome-mastery.model';

/**
 * Issue #696: kazanım seviyeleri. Öğrenci kimliği kendi ucunda token'dan gelir; veli ve öğretmende yetki sunucuda, kapsam
 * dışı öğrenci 404.
 */
@Injectable({ providedIn: 'root' })
export class LearningOutcomeMasteryService {
  private readonly http = inject(HttpClient);

  getMine(): Observable<LearningOutcomeMastery> {
    return this.http.get<LearningOutcomeMastery>('/api/exam/student/me/learning-outcome-mastery');
  }

  getChild(studentId: number): Observable<LearningOutcomeMastery> {
    return this.http.get<LearningOutcomeMastery>(`/api/exam/parent/children/${studentId}/learning-outcome-mastery`);
  }

  getTeacherStudent(studentId: number): Observable<LearningOutcomeMastery> {
    return this.http.get<LearningOutcomeMastery>(`/api/exam/teacher/students/${studentId}/learning-outcome-mastery`);
  }
}
