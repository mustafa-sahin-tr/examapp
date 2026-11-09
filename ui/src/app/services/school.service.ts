import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { SchoolOption } from '../models/taxonomy';

/**
 * Issue #277 — okul seçici (kayıt sihirbazı, ayarlar, admin dialog'ları). Liste 54 binin üstünde okul taşır, tamamı hiç
 * inmez: arama sunucuda yapılır (api/ExamApp.Api/Controllers/SchoolController.cs; ada göre sıralı).
 */
@Injectable({ providedIn: 'root' })
export class SchoolService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/exam/school';

  /** Kelimeler ad, ilçe ve ilde aranır (Türkçe harf farkı yok sayılır); boş arama ilk okulları döner. */
  search(query: string, limit: number): Observable<SchoolOption[]> {
    const params = new HttpParams().set('q', query).set('limit', limit);
    return this.http.get<SchoolOption[]>(this.baseUrl, { params });
  }

  getById(id: number): Observable<SchoolOption> {
    return this.http.get<SchoolOption>(`${this.baseUrl}/${id}`);
  }
}
