import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { SolutionRecording, SolutionStrokeDocument } from '../models/solution-recording';

/**
 * Issue #1226: soru çözüm kaydı. `question/*` uçları öğretmen (soru sahibi) içindir; `session-question/*` uçları öğrencinin
 * test oturumu satırıyla çalışır ve kaydı yalnız bitmiş oturumda ya da örnek soruda verir. Ses yalnız bu uçlardan (blob).
 */
@Injectable({ providedIn: 'root' })
export class SolutionRecordingService {
  private readonly baseUrl = '/api/exam/solution-recordings';
  private readonly http = inject(HttpClient);

  get(questionId: number): Observable<SolutionRecording> {
    return this.http.get<SolutionRecording>(`${this.baseUrl}/question/${questionId}`);
  }

  audio(questionId: number): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/question/${questionId}/audio`, { responseType: 'blob' });
  }

  save(questionId: number, strokes: SolutionStrokeDocument, durationMs: number, audio: Blob): Observable<SolutionRecording> {
    const form = new FormData();
    form.append('audio', audio, 'solution');
    form.append('strokes', JSON.stringify(strokes));
    form.append('durationMs', String(Math.round(durationMs)));
    return this.http.put<SolutionRecording>(`${this.baseUrl}/question/${questionId}`, form);
  }

  delete(questionId: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/question/${questionId}`);
  }

  getForStudent(instanceQuestionId: number): Observable<SolutionRecording> {
    return this.http.get<SolutionRecording>(`${this.baseUrl}/session-question/${instanceQuestionId}`);
  }

  audioForStudent(instanceQuestionId: number): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/session-question/${instanceQuestionId}/audio`, { responseType: 'blob' });
  }
}
