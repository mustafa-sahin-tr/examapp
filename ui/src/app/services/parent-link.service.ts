import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { LinkedChild, ParentInviteCode, ParentLinkErrorBody, StudentParentLinks } from '../models/parent-link.model';

/** Veli–öğrenci bağlantısı (issue #419). */
@Injectable({ providedIn: 'root' })
export class ParentLinkService {
  // Gateway: /api/exam/{everything} -> backend /api/{everything}
  private readonly baseUrl = '/api/exam/parent-links';
  private readonly http = inject(HttpClient);

  /** Öğrenci: yeni davet kodu (öncekini geçersizler). */
  createInviteCode(): Observable<ParentInviteCode> {
    return this.http.post<ParentInviteCode>(`${this.baseUrl}/invite-code`, {});
  }

  /** Öğrenci: bağlı veliler. */
  getMyParents(): Observable<StudentParentLinks> {
    return this.http.get<StudentParentLinks>(`${this.baseUrl}/my-parents`);
  }

  /** Veli: kodu kullan → onay bekleyen bağlantı (öğrenci verisi yok). */
  redeem(code: string): Observable<LinkedChild> {
    return this.http.post<LinkedChild>(`${this.baseUrl}/redeem`, { code });
  }

  /** Veli: bağlı çocuklar. */
  getMyChildren(): Observable<LinkedChild[]> {
    return this.http.get<LinkedChild[]>(`${this.baseUrl}/my-children`);
  }

  /** Öğrenci: bekleyen veli isteğini onaylar. */
  approve(linkId: number): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${linkId}/approve`, {});
  }

  /** Öğrenci: bekleyen veli isteğini reddeder. */
  reject(linkId: number): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${linkId}/reject`, {});
  }

  /** Öğrenci ya da veli: kendi bağlantısını (aktif ya da bekleyen) koparır. */
  revoke(linkId: number): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${linkId}/revoke`, {});
  }

  /** Sunucunun yerelleştirilmiş mesajı; yoksa çağıranın yedeği. */
  extractError(err: HttpErrorResponse, fallback: string): string {
    const body = err?.error as ParentLinkErrorBody | null;
    return typeof body?.message === 'string' && body.message.trim() ? body.message : fallback;
  }

  errorCode(err: HttpErrorResponse): string | null {
    const body = err?.error as ParentLinkErrorBody | null;
    return typeof body?.errorCode === 'string' ? body.errorCode : null;
  }
}
