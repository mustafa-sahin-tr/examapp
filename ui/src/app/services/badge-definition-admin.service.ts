import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  BadgeDefinitionAdmin,
  BadgeDefinitionAdminListResponse,
  BadgeRuleTypeSchema,
  CreateBadgeDefinitionRequest,
  UpdateBadgeDefinitionRequest,
} from '../models/badge-definition-admin.model';

/**
 * Issue #148 — BadgeService admin rozet tanımı CRUD'u (yalnız Admin). Gateway `/api/badge/{everything}` →
 * BadgeService `/api/{everything}`; `BadgeService` (raporlar) ile aynı göreli taban kullanılır.
 * Silme yok: kazanılmış rozetler geçerli kalsın diye yalnız deactivate/activate.
 */
@Injectable({ providedIn: 'root' })
export class BadgeDefinitionAdminService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/badge/admin/badge-definitions';

  getRuleTypes(): Observable<BadgeRuleTypeSchema[]> {
    return this.http.get<BadgeRuleTypeSchema[]>(`${this.baseUrl}/rule-types`);
  }

  /** Sayfalı liste; sunucu `take`'i 1..200'e sıkıştırır. */
  list(includeInactive: boolean, skip: number, take: number): Observable<BadgeDefinitionAdminListResponse> {
    const params = new HttpParams()
      .set('includeInactive', String(includeInactive))
      .set('skip', String(skip))
      .set('take', String(take));
    return this.http.get<BadgeDefinitionAdminListResponse>(this.baseUrl, { params });
  }

  get(id: string): Observable<BadgeDefinitionAdmin> {
    return this.http.get<BadgeDefinitionAdmin>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }

  create(request: CreateBadgeDefinitionRequest): Observable<BadgeDefinitionAdmin> {
    return this.http.post<BadgeDefinitionAdmin>(this.baseUrl, request);
  }

  update(id: string, request: UpdateBadgeDefinitionRequest): Observable<BadgeDefinitionAdmin> {
    return this.http.put<BadgeDefinitionAdmin>(`${this.baseUrl}/${encodeURIComponent(id)}`, request);
  }

  /** İdempotent; öğrencilere artık verilmez, kazanılmış rozetler kalır. */
  deactivate(id: string): Observable<BadgeDefinitionAdmin> {
    return this.http.post<BadgeDefinitionAdmin>(`${this.baseUrl}/${encodeURIComponent(id)}/deactivate`, {});
  }

  /** İdempotent. Aktif rozet üst sınırı doluysa 409 (`errors.isActive`). */
  activate(id: string): Observable<BadgeDefinitionAdmin> {
    return this.http.post<BadgeDefinitionAdmin>(`${this.baseUrl}/${encodeURIComponent(id)}/activate`, {});
  }
}
