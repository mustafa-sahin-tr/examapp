import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { School } from '../models/taxonomy';

/**
 * Issue #277 — okul listesi (kayıt sihirbazı ve admin öğrenci okul değişikliği için).
 * `GET /api/exam/school` → `SchoolDto[]` (api/ExamApp.Api/Controllers/SchoolController.cs; ada göre sıralı).
 */
@Injectable({ providedIn: 'root' })
export class SchoolService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/exam/school';

  getSchools(): Observable<School[]> {
    return this.http.get<School[]>(this.baseUrl);
  }
}
