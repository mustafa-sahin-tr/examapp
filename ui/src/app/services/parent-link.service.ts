import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { LinkedChild, ParentInviteCode, ParentLinkErrorBody, StudentParentLinks } from '../models/parent-link.model';

/** Veli–öğrenci bağlantısı (issue #419; #436 veli-öncelikli model). */
@Injectable({ providedIn: 'root' })
export class ParentLinkService {
  // Gateway: /api/exam/{everything} -> backend /api/{everything}
  private readonly baseUrl = '/api/exam/parent-links';
  private readonly http = inject(HttpClient);

  /** Birincil veli: kendi bağlantısındaki çocuk için ikinci veli davet kodu (öncekini geçersizler). */
  createSecondParentCode(linkId: number): Observable<ParentInviteCode> {
    return this.http.post<ParentInviteCode>(`${this.baseUrl}/${linkId}/second-parent-code`, {});
  }

  /** Öğrenci: bağlı veliler (salt okunur). */
  getMyParents(): Observable<StudentParentLinks> {
    return this.http.get<StudentParentLinks>(`${this.baseUrl}/my-parents`);
  }

  /** Veli: ikinci veli kodunu kullan → birincil velinin onayını bekleyen bağlantı (öğrenci verisi yok). */
  redeem(code: string): Observable<LinkedChild> {
    return this.http.post<LinkedChild>(`${this.baseUrl}/redeem`, { code });
  }

  /** Veli: bağlı çocuklar. */
  getMyChildren(): Observable<LinkedChild[]> {
    return this.http.get<LinkedChild[]>(`${this.baseUrl}/my-children`);
  }

  /** Birincil veli: bekleyen ikinci veli isteğini onaylar (öğrenci: yalnız geçiş dönemindeki eski istek). */
  approve(linkId: number): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${linkId}/approve`, {});
  }

  /** Birincil veli: bekleyen ikinci veli isteğini reddeder (öğrenci: yalnız geçiş dönemindeki eski istek). */
  reject(linkId: number): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${linkId}/reject`, {});
  }

  /** Veli: birincil veli her bağlantıyı, her veli kendi bekleyen isteğini koparır (öğrenci koparamaz). */
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
