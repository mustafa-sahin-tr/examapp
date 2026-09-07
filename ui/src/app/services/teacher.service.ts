import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { TeacherDashboardSummary } from '../models/teacher-dashboard.model';

@Injectable({
  providedIn: 'root',
})
export class TeacherService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/exam/teacher';

  // eslint-disable-next-line @typescript-eslint/no-explicit-any -- mevcut imza, çağıranlar val.accessToken kullanıyor (kapsam dışı)
  register(teacher: any): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/register`, teacher);
  }

  /**
   * Issue #53: giriş yapan öğretmenin özet sayıları.
   * teacherId gönderilmez — backend authenticated user'dan alır.
   */
  getDashboardSummary(): Observable<TeacherDashboardSummary> {
    return this.http.get<TeacherDashboardSummary>(`${this.baseUrl}/dashboard-summary`);
  }
}
