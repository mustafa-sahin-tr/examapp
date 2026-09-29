import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, defer, tap, throwError } from 'rxjs';
import { AppNotification } from '../models/notification.model';

/** Liste ucunun sunucu tarafı üst sınırı 100; bildirim sayfası için makul pencere. */
export const NOTIFICATION_LIST_TAKE = 50;

/**
 * Issue #146 — kalıcı bildirimler (BadgeService). Gateway `/api/badge/{everything}` →
 * BadgeService `/api/notifications/...`. Sahiplik sunucuda çağıranın Keycloak subject'i ile sağlanır.
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/badge/notifications';

  private readonly unreadCountState = signal(0);
  /** Zil rozeti için okunmamış bildirim sayısı. */
  readonly unreadCount = this.unreadCountState.asReadonly();

  /** Çağıranın son bildirimleri (yeniden eskiye). */
  list(unreadOnly: boolean, take: number = NOTIFICATION_LIST_TAKE): Observable<AppNotification[]> {
    const params = new HttpParams().set('unreadOnly', String(unreadOnly)).set('take', String(take));
    return this.http.get<AppNotification[]>(`${this.baseUrl}/me`, { params });
  }

  /** Okunmamış sayısını çeker ve `unreadCount` signal'ını günceller. */
  refreshUnreadCount(): Observable<number> {
    return this.http
      .get<number>(`${this.baseUrl}/me/unread-count`)
      .pipe(tap((count) => this.unreadCountState.set(Math.max(0, count ?? 0))));
  }

  /**
   * Tek bildirimi okundu işaretler (yalnızca okunmamış öğe için çağrılmalı). Sayaç abone olunduğu anda iyimser
   * olarak bir azalır; istek başarısız olursa geri alınır.
   */
  markRead(id: number): Observable<void> {
    return defer(() => {
      const decremented = this.unreadCountState() > 0;
      if (decremented) {
        this.unreadCountState.update((count) => count - 1);
      }
      return this.http.post<void>(`${this.baseUrl}/${id}/read`, null).pipe(
        catchError((err: unknown) => {
          if (decremented) {
            this.unreadCountState.update((count) => count + 1);
          }
          return throwError(() => err);
        })
      );
    });
  }

  /** Oturum kapanınca önceki kullanıcının sayısı görünmesin. */
  resetUnreadCount(): void {
    this.unreadCountState.set(0);
  }
}
