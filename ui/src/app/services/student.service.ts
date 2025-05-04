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
import { Grade } from '../models/student';

@Injectable({
  providedIn: 'root',
})
export class StudentService {
  constructor(private http: HttpClient) {}
  private baseUrl = '/api/exam/student';

  getProfile(): Observable<StudentProfile> {
    return this.http.get<StudentProfile>(`${this.baseUrl}/profile`);
  }

  updateGrade(newGradeId: number): Observable<StudentProfile> {
    return this.http.post<StudentProfile>(`${this.baseUrl}/update-grade`, newGradeId);
  }

  updateAvatar(file: File): Observable<StudentProfile> {
    const formData = new FormData();
    formData.append('avatar', file);
    return this.http.post<StudentProfile>(`${this.baseUrl}/update-avatar`, formData);
  }

  loadGrades(): Observable<Grade[]> {
    return this.http.get<Grade[]>(`${this.baseUrl}/grades`);
  }

  register(student: any): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/register`, student);
  }
}
