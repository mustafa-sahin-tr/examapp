import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Passage, Question } from '../models/question';

@Injectable({
  providedIn: 'root'
})
export class QuestionService {
  private apiUrl = '/api/questions'; // Backend API URL

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

  getAll(testId: number | undefined ): Observable<Question[]> {
    return this.http.get<Question[]>(`${this.apiUrl}/bytest/${testId}`);
  }

  loadPassages(): Observable<Passage[]> {
    return this.http.get<Passage[]>(`${this.apiUrl}/passages`);
  }

  saveBulk(bulkDto: any) : Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/save`, bulkDto);
  }
}
