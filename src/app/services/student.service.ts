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
import { StudentProfile } from '../models/student-profile';

@Injectable({
  providedIn: 'root'
})
export class StudentService {

  constructor(private http: HttpClient) {}

  getProfile(): Observable<StudentProfile> {
    return this.http.get<StudentProfile>(`/api/student/profile`);
  }
  
}