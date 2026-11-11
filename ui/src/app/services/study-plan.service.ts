import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { StudyPlan } from '../models/study-plan.model';

/** Issue #1113: öğrencinin bu haftaki çalışma planı ve velinin salt okunur görünümü. */
@Injectable({ providedIn: 'root' })
export class StudyPlanService {
  private readonly http = inject(HttpClient);

  current(): Observable<StudyPlan> {
    return this.http.get<StudyPlan>('/api/exam/study-plan/current');
  }

  setDone(taskId: number, done: boolean): Observable<StudyPlan> {
    return this.http.put<StudyPlan>(`/api/exam/study-plan/tasks/${taskId}`, { done });
  }

  child(studentId: number): Observable<StudyPlan> {
    return this.http.get<StudyPlan>(`/api/exam/parent/children/${studentId}/study-plan`);
  }
}
