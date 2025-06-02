import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

@Injectable({
  providedIn: 'root',
})
export class GradesService {
  constructor(private http: HttpClient) {}

  getGrades(): Observable<any[]> {
    return this.http.get<any[]>('/api/exam/worksheet/grades');
  }
}
