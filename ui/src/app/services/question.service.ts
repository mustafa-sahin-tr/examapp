import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Passage, Question } from '../models/question';
import { LinkBankQuestionsResult } from '../models/question-bank';

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

  attachStudyPage(imageData: any): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/attach-study-page`, { imageData });
  }

  updateCorrectAnswer(questionId: number, correctAnswerId: number, scale: number): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/${questionId}/correct-answer`, {
      correctAnswerId,
      scale,
    });
  }

  updateClassification(
    questionId: number,
    subjectId?: number | null,
    topicId?: number | null,
    subTopicId?: number | null,
    subTopicIds?: number[]
  ): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/${questionId}/classification`, {
      subjectId,
      topicId,
      subTopicId,
      subTopicIds,
    });
  }

  /** issue #472: bankadan seçilen hazır soruları teste bağlar (testin sonuna; testte olanlar atlanır). */
  linkBankQuestions(testId: number, questionIds: number[]): Observable<LinkBankQuestionsResult> {
    return this.http.post<LinkBankQuestionsResult>(`${this.apiUrl}/test/${testId}/bank-questions`, { questionIds });
  }

  /** Issue #685: testin bütün maddelerinin yeni sırası (yalnız bundan sonra başlayan oturumlara uygulanır). */
  reorderTestQuestions(testId: number, questionIds: number[]): Observable<{ success: boolean }> {
    return this.http.put<{ success: boolean }>(`${this.apiUrl}/test/${testId}/order`, { questionIds });
  }

  removeQuestionFromTest(testId: number, questionId: number): Observable<any> {
    return this.http.delete<any>(`${this.apiUrl}/test/${testId}/question/${questionId}`);
  }
}
