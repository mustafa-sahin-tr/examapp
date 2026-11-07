import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, switchMap, tap, throwError } from 'rxjs';

import {
  LinkedChild,
  ParentInviteCode,
  ParentLinkApproveConsent,
  ParentLinkErrorBody,
  StudentParentLinks,
} from '../models/parent-link.model';

/** Backend `ParentLinkErrorCodes.ProfileNotFound`. */
export const PARENT_PROFILE_NOT_FOUND = 'ProfileNotFound';
/** `ParentController.RegisterParent`: profil alanı yok, yalnız Parent satırını açar (kayıt sihirbazıyla aynı). */
const PARENT_REGISTER_URL = '/api/exam/parent/register';

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

  /**
   * Veli: bağlı çocuklar. #902: Keycloak'ta "Veli" rolüyle kayıt olan hesabın uygulama profili ilk girişte yoktur
   * (`ProfileNotFound`); "Kaydı Tamamla" ile aynı kayıt çağrısı bir kez yapılır ve liste yeniden istenir.
   */
  getMyChildren(): Observable<LinkedChild[]> {
    const load = () => this.http.get<LinkedChild[]>(`${this.baseUrl}/my-children`);
    return load().pipe(
      catchError((err: HttpErrorResponse) =>
        this.errorCode(err) === PARENT_PROFILE_NOT_FOUND
          ? this.http.post<{ accessToken?: string }>(PARENT_REGISTER_URL, {}).pipe(
              tap((result) => {
                if (result?.accessToken) localStorage.setItem('auth_token', result.accessToken);
              }),
              switchMap(() => load()),
            )
          : throwError(() => err),
      ),
    );
  }

  /**
   * Birincil veli: bekleyen ikinci veli isteğini onaylar (öğrenci: yalnız geçiş dönemindeki eski istek). Issue #440: öğrencinin
   * davet ettiği veli kendi bağlantısını rızayla onaylar — `consent` zorunlu (`requiresConsent`).
   */
  approve(linkId: number, consent?: ParentLinkApproveConsent): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${linkId}/approve`, consent ?? {});
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
