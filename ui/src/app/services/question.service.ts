import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Passage, Question } from '../models/question';

@Injectable({
  providedIn: 'root',
})
export class QuestionService {
  private apiUrl = '/api/exam/questions'; // Backend API URL

  constructor(private http: HttpClient) {}

  saveQuestion(questionData: any): Observable<any> {
    return this.http.post<any>(this.apiUrl, questionData);
  }

  check(questionId: number, answerIndex: number): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/${questionId}/check`, { answerIndex });
  }

  get(questionId: number): Observable<Question> {
    return this.http.get<Question>(`${this.apiUrl}/${questionId}`);
  }

  getAll(testId: number | undefined): Observable<Question[]> {
    return this.http.get<Question[]>(`${this.apiUrl}/bytest/${testId}`);
  }

  loadPassages(): Observable<Passage[]> {
    return this.http.get<Passage[]>(`${this.apiUrl}/passages`);
  }

  saveBulk(bulkDto: any): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/save`, bulkDto);
  }

  updateCorrectAnswer(
    questionId: number,
    correctAnswerId: number,
    scale: number,
    subjectId?: number | null,
    topicId?: number | null,
    subTopicId?: number | null
  ): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/${questionId}/correct-answer`, {
      correctAnswerId,
      scale,
      subjectId,
      topicId,
      subTopicId,
    });
  }

  removeQuestionFromTest(testId: number, questionId: number): Observable<any> {
    return this.http.delete<any>(`${this.apiUrl}/test/${testId}/question/${questionId}`);
  }
}
