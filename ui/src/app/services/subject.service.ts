import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { CheckStudentResponse } from '../models/check-student-response';
import { Router } from '@angular/router';
import { Subject } from '../models/subject';
import { Topic } from '../models/topic';
import { SubTopic } from '../models/subtopic';

@Injectable({
  providedIn: 'root',
})
export class SubjectService {
  constructor(private http: HttpClient, private router: Router) {}
  private baseUrl = '/api/exam/subject';

  loadCategories(): Observable<Subject[]> {
    return this.http.get<Subject[]>(this.baseUrl);
  }

  getTopicsBySubject(subjectId: number): Observable<Topic[]> {
    return this.http.get<Topic[]>(`${this.baseUrl}/topics/${subjectId}`);
  }

  getSubTopicsByTopic(topicId: number): Observable<SubTopic[]> {
    return this.http.get<SubTopic[]>(`${this.baseUrl}/subtopics/${topicId}`);
  }
}
