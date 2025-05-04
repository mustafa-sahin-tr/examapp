import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';

@Injectable({
  providedIn: 'root',
})
export class TeacherService {
  constructor(private http: HttpClient) {}

  register(teacher: any): Observable<any> {
    return this.http.post<any>(`/api/exam/teacher/register`, teacher);
  }
}
