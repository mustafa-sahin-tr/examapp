import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  TeacherDashboardSummary,
  TeacherLaggingStudent,
  TeacherWorksheetOverview,
} from '../models/teacher-dashboard.model';
import {
  TutorProfile,
  TutorPublicProfile,
  TutorSearchFilter,
  TutorSearchResult,
  UpdateTutorProfileRequest,
} from '../models/tutor.model';

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

  /**
   * Issue #54: giriş yapan öğretmenin sınavları — atanan öğrenci sayısı ve tamamlanma yüzdesiyle.
   * Ada göre sıralı gelir; hiç worksheet yoksa boş dizi.
   */
  getWorksheetsOverview(): Observable<TeacherWorksheetOverview[]> {
    return this.http.get<TeacherWorksheetOverview[]>(`${this.baseUrl}/worksheets-overview`);
  }

  /**
   * Issue #55: giriş yapan öğretmenin geride kalan öğrencileri (düşük tamamlama ve/veya süresi geçmiş atama).
   * Backend sadece en az bir bayrağı true olan satırları döndürür; hiç yoksa boş dizi.
   */
  getLaggingStudents(): Observable<TeacherLaggingStudent[]> {
    return this.http.get<TeacherLaggingStudent[]>(`${this.baseUrl}/lagging-students`);
  }

  /**
   * Issue #95: giriş yapan öğretmenin özel ders (tutor) profili.
   * Teacher kaydı yoksa 404, bağımsız öğretmen değilse 400 döner — çağıran bu iki durumu ayırmalı.
   */
  getTutorProfile(): Observable<TutorProfile> {
    return this.http.get<TutorProfile>(`${this.baseUrl}/tutor-profile`);
  }

  /** Issue #95: kendi tutor profilini günceller; başarıda güncel profil döner, validasyon hatasında 400. */
  updateTutorProfile(request: UpdateTutorProfileRequest): Observable<TutorProfile> {
    return this.http.put<TutorProfile>(`${this.baseUrl}/tutor-profile`, request);
  }

  /**
   * Issue #95: öğrenci için bağımsız öğretmen araması. Backend sadece onaylı kayıtları döndürür,
   * UI ek filtre uygulamaz. Boş/null filtre alanları query'ye hiç eklenmez.
   */
  searchTutors(filter: TutorSearchFilter): Observable<TutorSearchResult[]> {
    let params = new HttpParams();
    if (filter.subjectId != null) params = params.set('subjectId', filter.subjectId);
    if (filter.minPrice != null) params = params.set('minPrice', filter.minPrice);
    if (filter.maxPrice != null) params = params.set('maxPrice', filter.maxPrice);
    // online/inPerson sadece true iken gönderilir; false backend'de zaten filtre uygulamaz.
    if (filter.online) params = params.set('online', true);
    if (filter.inPerson) params = params.set('inPerson', true);
    if (filter.skip != null) params = params.set('skip', filter.skip);
    if (filter.take != null) params = params.set('take', filter.take);

    return this.http.get<TutorSearchResult[]>(`${this.baseUrl}/search`, { params });
  }

  /** Issue #95: tekil öğretmen public profili. Onaylı bağımsız öğretmen değilse 404. */
  getTutorPublicProfile(teacherId: number): Observable<TutorPublicProfile> {
    return this.http.get<TutorPublicProfile>(`${this.baseUrl}/${teacherId}/public-profile`);
  }
}
