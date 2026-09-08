import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { QuestionRegion } from '../models/draws';
import { Question } from '../models/question';
import {
  PracticeAnswerResult,
  PracticeAnswerSubmitRequest,
  PracticeNextQuestion,
  PracticeSession,
  PracticeSessionStartRequest,
} from '../models/practice';

/**
 * "Soru Çöz" pratik oturumu API'si (issue #62). Tüm çağrılar gateway'deki
 * `/api/exam/{everything}` joker route'u üzerinden `PracticeController`'a gider.
 */
@Injectable({
  providedIn: 'root',
})
export class PracticeService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/exam/practice';

  startSession(request: PracticeSessionStartRequest): Observable<PracticeSession> {
    return this.http.post<PracticeSession>(`${this.baseUrl}/sessions`, request);
  }

  getSession(sessionId: number): Observable<PracticeSession> {
    return this.http.get<PracticeSession>(`${this.baseUrl}/sessions/${sessionId}`);
  }

  /** Cevaplanmadan tekrar çağrılırsa aynı bekleyen soruyu döner (idempotent). */
  getNextQuestion(sessionId: number): Observable<PracticeNextQuestion> {
    return this.http.get<PracticeNextQuestion>(`${this.baseUrl}/sessions/${sessionId}/next`);
  }

  submitAnswer(sessionId: number, request: PracticeAnswerSubmitRequest): Observable<PracticeAnswerResult> {
    return this.http.post<PracticeAnswerResult>(`${this.baseUrl}/sessions/${sessionId}/answer`, request);
  }

  /** Idempotent; zaten bitmiş oturumda da 200 döner. */
  endSession(sessionId: number): Observable<PracticeSession> {
    return this.http.put<PracticeSession>(`${this.baseUrl}/sessions/${sessionId}/end`, null);
  }

  /**
   * Tek bir `QuestionDto` → `QuestionRegion` (canvas soruları `app-question-canvas-view-v5`
   * ile göstermek için). `TestService.convertTestInstanceToRegions` içindeki soru başına
   * dönüşümün birebir karşılığıdır; tek fark doğru şıkkın sorudan değil, cevap sonrası
   * gelen `correctAnswerId` parametresinden alınmasıdır (cevap öncesi DTO'da gizlidir).
   */
  toQuestionRegion(question: Question, correctAnswerId: number | null = null): QuestionRegion {
    return {
      id: question.id,
      name: 'Soru',
      x: question.x,
      y: question.y,
      width: question.width,
      height: question.height,
      sanitizedHeight: question.sanitizedHeight,
      classificationSource: question.classificationSource ?? 0,
      isExample: question.isExample,
      subjectId: question.subjectId || 0,
      topicId: question.topicId || 0,
      difficultyLevel: question.difficultyLevel,
      showPassageFirst: question.showPassageFirst,
      passageId: question.passage ? question.passage.id.toString() : '',
      imageId: question.imageUrl,
      imageUrl: question.imageUrl,
      exampleAnswer: question.isExample ? question.practiceCorrectAnswer : null,
      answers: (question.answers ?? []).map((answer) => ({
        id: answer.id,
        label: answer.text,
        tag: answer.tag,
        order: answer.order,
        x: answer.x,
        y: answer.y,
        width: answer.width,
        height: answer.height,
        imageUrl: answer.imageUrl,
        isCorrect: correctAnswerId != null && answer.id === correctAnswerId,
      })),
      passage: question.passage
        ? {
            id: question.passage.id,
            title: question.passage.title,
            x: question.passage.x,
            y: question.passage.y,
            width: question.passage.width,
            height: question.passage.height,
            imageUrl: question.passage.imageUrl,
            imageId: question.passage.imageUrl,
          }
        : undefined,
    };
  }
}
