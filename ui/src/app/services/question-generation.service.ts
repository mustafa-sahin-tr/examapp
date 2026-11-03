import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface QuestionDraft {
  text: string;
  options: string[];
  correctIndex: number;
  /** İnceleyen için doğru cevabın gerekçesi; soruyla kaydedilmez. */
  explanation?: string | null;
}

export interface QuestionDraftsResponse {
  learningOutcomeId: number;
  outcomeCode: string;
  outcomeDescription: string;
  drafts: QuestionDraft[];
  /** Kurallara uymadığı için elenen taslak sayısı. */
  rejected: number;
}

export interface SavedQuestionDrafts {
  questionIds: number[];
}

@Injectable({ providedIn: 'root' })
export class QuestionGenerationService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = '/api/exam/question-generation';

  /** Ücretli Claude çağrısı; taslaklar kaydedilmez. */
  generate(learningOutcomeId: number, count: number, difficultyLevel: number): Observable<QuestionDraftsResponse> {
    return this.http.post<QuestionDraftsResponse>(`${this.apiUrl}/drafts`, { learningOutcomeId, count, difficultyLevel });
  }

  save(learningOutcomeId: number, difficultyLevel: number, questions: QuestionDraft[]): Observable<SavedQuestionDrafts> {
    return this.http.post<SavedQuestionDrafts>(`${this.apiUrl}/save`, { learningOutcomeId, difficultyLevel, questions });
  }
}
