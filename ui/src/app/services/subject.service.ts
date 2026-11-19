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

  /** Issue #1215: yalnız ana dersler (öğrenci seçicileri); seçmeli modüller ve lehçeler gelmez. */
  loadCoreSubjects(): Observable<Subject[]> {
    return this.http.get<Subject[]>(this.baseUrl, { params: { coreOnly: true } });
  }

  getTopicsBySubject(subjectId: number): Observable<Topic[]> {
    return this.http.get<Topic[]>(`${this.baseUrl}/topics/${subjectId}`);
  }

  getSubTopicsByTopic(topicId: number): Observable<SubTopic[]> {
    return this.http.get<SubTopic[]>(`${this.baseUrl}/subtopics/${topicId}`);
  }

  getSubjectsByGrade(gradeId: number) {
    return this.http.get<Subject[]>(`/api/exam/subject/by-grade/${gradeId}`);
  }

  /** Issue #1215: sınıfın ana dersleri (öğrenci seçicileri); sınıfın ana dersi yoksa bütün dersleri. */
  getCoreSubjectsByGrade(gradeId: number) {
    return this.http.get<Subject[]>(`/api/exam/subject/by-grade/${gradeId}`, { params: { coreOnly: true } });
  }

  getTopicsBySubjectAndGrade(subjectId: number, gradeId: number): Observable<Topic[]> {
    return this.http.get<Topic[]>(`${this.baseUrl}/topics?subjectId=${subjectId}&gradeId=${gradeId}`);
  }
}
