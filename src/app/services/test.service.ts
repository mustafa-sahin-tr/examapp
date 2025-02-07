import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { CheckStudentResponse } from '../models/check-student-response';
import { Router } from '@angular/router';
import { Subject } from '../models/subject';
import { Topic } from '../models/topic';
import { SubTopic } from '../models/subtopic';
import { TestInstance } from '../models/test-instance';
import { StudentAnswer } from '../models/student-answer';

@Injectable({
  providedIn: 'root'
})
export class TestService {

  constructor(private http: HttpClient, private router: Router) {}

  getTestWithAnswers(testInstanceId: number): Observable<TestInstance> {
    return this.http.get<TestInstance>(`/api/exam/test-instance/${testInstanceId}`);
  }

  saveAnswer( studentAnswer: StudentAnswer ): Observable<any> {
    return this.http.post<any>(`/api/exam/save-answer`,  studentAnswer );
  }
  
}