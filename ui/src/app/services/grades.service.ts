import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Grade } from '../models/student';

@Injectable({
  providedIn: 'root',
})
export class GradesService {
  private readonly http = inject(HttpClient);

  /** Backend `GET api/worksheet/grades` → `GradeDto { id, name }`. */
  getGrades(): Observable<Grade[]> {
    return this.http.get<Grade[]>('/api/exam/worksheet/grades');
  }
}
