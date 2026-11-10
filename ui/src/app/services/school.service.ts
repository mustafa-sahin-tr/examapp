import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, shareReplay } from 'rxjs';
import { DistrictDto, ProvinceDto, SchoolOption } from '../models/taxonomy';

/** Okul aramasını il / ilçeye daraltır (#1106). */
export interface SchoolSearchScope {
  provinceId: number | null;
  districtId: number | null;
}

/**
 * Issue #277 — okul seçici (kayıt sihirbazı, ayarlar, admin dialog'ları). Liste 54 binin üstünde okul taşır, tamamı hiç
 * inmez: arama sunucuda yapılır (api/ExamApp.Api/Controllers/SchoolController.cs; ada göre sıralı).
 */
@Injectable({ providedIn: 'root' })
export class SchoolService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/exam/school';

  /** İller referans veridir, oturumda bir kez iner. */
  private provinces$?: Observable<ProvinceDto[]>;

  /** Kelimeler ad, ilçe ve ilde aranır (Türkçe harf farkı yok sayılır); boş arama ilk okulları döner. */
  search(query: string, limit: number, scope?: SchoolSearchScope): Observable<SchoolOption[]> {
    let params = new HttpParams().set('q', query).set('limit', limit);
    if (scope?.provinceId != null) params = params.set('provinceId', scope.provinceId);
    if (scope?.districtId != null) params = params.set('districtId', scope.districtId);
    return this.http.get<SchoolOption[]>(this.baseUrl, { params });
  }

  provinces(): Observable<ProvinceDto[]> {
    this.provinces$ ??= this.http.get<ProvinceDto[]>(`${this.baseUrl}/provinces`).pipe(shareReplay(1));
    return this.provinces$;
  }

  districts(provinceId: number): Observable<DistrictDto[]> {
    const params = new HttpParams().set('provinceId', provinceId);
    return this.http.get<DistrictDto[]>(`${this.baseUrl}/districts`, { params });
  }

  getById(id: number): Observable<SchoolOption> {
    return this.http.get<SchoolOption>(`${this.baseUrl}/${id}`);
  }
}
