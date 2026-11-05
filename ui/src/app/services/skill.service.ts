import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, shareReplay } from 'rxjs';
import { QuestionContextTags, Skill } from '../models/skill';

/** Issue #703: beceri listesi ve sorunun bağlam temelli etiketleri. */
@Injectable({ providedIn: 'root' })
export class SkillService {
  private readonly http = inject(HttpClient);

  // Liste seed'den gelir ve değişmez; oturum boyunca bir kez istenir.
  private readonly skills$ = this.http.get<Skill[]>('/api/exam/skills').pipe(shareReplay({ bufferSize: 1, refCount: false }));

  list(): Observable<Skill[]> {
    return this.skills$;
  }

  getForQuestion(questionId: number): Observable<QuestionContextTags> {
    return this.http.get<QuestionContextTags>(`/api/exam/questions/${questionId}/context-tags`);
  }

  setForQuestion(questionId: number, isContextBased: boolean, skillIds: number[]): Observable<QuestionContextTags> {
    return this.http.put<QuestionContextTags>(`/api/exam/questions/${questionId}/context-tags`, { isContextBased, skillIds });
  }
}
