import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, finalize, of, shareReplay, tap } from 'rxjs';

import { ParentContactPayload, ParentInvitationResponse, ParentStatus } from '../models/parent-status.model';
import { ParentStatusStore } from './parent-status.store';

/**
 * Issue #440 / #441: öğrencinin veli durumu ve "veli bilgisi ekle / e-postayı düzelt / yeniden gönder" uçları
 * (`/api/exam/student-parent/...`, yalnız Student; hoşgörü süresi dolmuş öğrenciye de açık). Her başarılı yanıt
 * `ParentStatusStore`'a yazılır.
 */
@Injectable({ providedIn: 'root' })
export class StudentParentService {
  private readonly http = inject(HttpClient);
  private readonly store = inject(ParentStatusStore);
  private readonly baseUrl = '/api/exam/student-parent';

  private inFlight$: Observable<ParentStatus | null> | null = null;

  /** Durumu sunucudan tazeler (eşzamanlı çağıranlar tek isteği paylaşır). Hata çağırana iletilir. */
  reload(): Observable<ParentStatus | null> {
    if (!this.inFlight$) {
      this.inFlight$ = this.http.get<ParentStatus>(`${this.baseUrl}/status`).pipe(
        tap((status) => this.store.set(status)),
        finalize(() => (this.inFlight$ = null)),
        shareReplay({ bufferSize: 1, refCount: false })
      );
    }
    return this.inFlight$;
  }

  /** Bu oturumda yüklendiyse elde olanı, yoksa sunucudan; hata → null (karar sunucuya bırakılır). */
  ensureLoaded(): Observable<ParentStatus | null> {
    const current = this.store.status();
    if (current) {
      return of(current);
    }
    return this.reload().pipe(catchError(() => of(null)));
  }

  /** Veli bilgisi ekle / e-postayı düzelt (farklı e-posta eski bağlantıyı geçersiz kılar; aynı e-posta yeniden gönderimdir). */
  startInvitation(payload: ParentContactPayload): Observable<ParentInvitationResponse> {
    return this.http
      .post<ParentInvitationResponse>(`${this.baseUrl}/invitation`, payload)
      .pipe(tap((res) => this.store.set(res?.status)));
  }

  /** Açık daveti aynı e-postaya yeniden gönder (24 saatte 1, toplam 5). */
  resend(): Observable<ParentInvitationResponse> {
    return this.http
      .post<ParentInvitationResponse>(`${this.baseUrl}/invitation/resend`, {})
      .pipe(tap((res) => this.store.set(res?.status)));
  }
}
