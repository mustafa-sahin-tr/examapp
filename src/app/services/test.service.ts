import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { CheckStudentResponse } from '../models/check-student-response';
import { Router } from '@angular/router';
import { Subject } from '../models/subject';
import { Topic } from '../models/topic';
import { SubTopic } from '../models/subtopic';
import { Exam, Test, TestInstance } from '../models/test-instance';
import { StudentAnswer } from '../models/student-answer';

@Injectable({
  providedIn: 'root'
})
export class TestService {

  constructor(private http: HttpClient, private router: Router) {}

  getTestWithAnswers(testInstanceId: number): Observable<TestInstance> {
    return this.http.get<TestInstance>(`/api/worksheet/test-instance/${testInstanceId}`);
  }

  saveAnswer( studentAnswer: StudentAnswer ): Observable<any> {
    return this.http.post<any>(`/api/worksheet/save-answer`,  studentAnswer );
  }

  completeTest( testInstanceId: number ): Observable<any> {
    return this.http.put<any>(`/api/worksheet/end-test/${testInstanceId}`, null);
  }

  startTest( testInstanceId: number ): Observable<any> {
    return this.http.post<any>(`/api/worksheet/start-test/${testInstanceId}`, null);
  }

  loadTest() {
    return this.http.get<Exam[]>(`/api/worksheet`);
  }

  loadOnlyTest() {
    return this.http.get<Test[]>(`/api/worksheet/list`);
  }
  
  create(test: Test): Observable<{message:string, examId: number}> {
    return this.http.post<any>('/api/worksheet', test);
  }

  get(id: number): Observable<Test> {
    return this.http.get<Test>(`/api/worksheet/${id}`);
  }
}