import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { LearningOutcome } from '../models/learning-outcome';

/** Issue #692: kazanım listesi ve soruya bağlama. */
@Injectable({ providedIn: 'root' })
export class LearningOutcomeService {
  private readonly http = inject(HttpClient);

  search(gradeId: number, subjectId: number, search?: string): Observable<LearningOutcome[]> {
    let params = new HttpParams().set('gradeId', gradeId).set('subjectId', subjectId);
    if (search?.trim()) {
      params = params.set('search', search.trim());
    }
    return this.http.get<LearningOutcome[]>('/api/exam/learning-outcomes', { params });
  }

  getForQuestion(questionId: number): Observable<LearningOutcome[]> {
    return this.http.get<LearningOutcome[]>(`/api/exam/questions/${questionId}/learning-outcomes`);
  }

  setForQuestion(questionId: number, learningOutcomeIds: number[]): Observable<LearningOutcome[]> {
    return this.http.put<LearningOutcome[]>(`/api/exam/questions/${questionId}/learning-outcomes`, { learningOutcomeIds });
  }
}
