import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, map, of, switchMap, tap } from 'rxjs';

import { AppLocale } from '../models/locale';
import { AuthService, UserProfile } from './auth.service';
import { LocaleService } from './locale.service';

/** `PUT /api/auth/me/locale` gövdesi (auth-api `UpdatePreferredLocaleRequest`). */
interface UpdatePreferredLocaleRequest {
  preferredLocale: AppLocale;
}

/**
 * Dil seçimini kullanıcı profiline yazan servis (issue #181).
 *
 * `LocaleService` saf tutulur (HTTP/oturum bilgisi taşımaz); profil yazma işi burada durur,
 * böylece `AuthService ↔ LocaleService` arasında DI döngüsü oluşmaz.
 *
 * Akış: `PUT /api/auth/me/locale` → `POST /api/exam/auth/refresh` (exam API'nin Redis profil
 * cache'i tazelensin, aksi halde bir saat eski değer kalır) → localStorage'daki `user` güncellenir
 * → en son `LocaleService.setLocale` (sayfayı yeniden yükler, bu yüzden mutlaka son adım).
 *
 * Sunucu tarafı başarısız olsa da yerel tercih uygulanır: kullanıcı seçtiği dili görmelidir.
 */
@Injectable({ providedIn: 'root' })
export class LocalePreferenceService {
  private readonly http = inject(HttpClient);
  private readonly authService = inject(AuthService);
  private readonly localeService = inject(LocaleService);

  /**
   * Seçilen dili uygular; oturum açıksa önce sunucuya kaydeder.
   * Cold observable döner — çağıranın `subscribe()` etmesi gerekir.
   */
  persistPreference(locale: AppLocale): Observable<void> {
    if (!this.authService.hasToken()) {
      this.localeService.setLocale(locale);
      return of(undefined);
    }

    const body: UpdatePreferredLocaleRequest = { preferredLocale: locale };

    return this.http.put<UserProfile>('/api/auth/me/locale', body).pipe(
      switchMap((profile) =>
        this.authService.refresh().pipe(
          map((refreshed) => refreshed ?? profile),
          catchError((error: unknown) => {
            console.warn('Profil cache yenilenemedi, dil tercihi yine de kaydedildi', error);
            return of(profile);
          })
        )
      ),
      tap((profile) => this.storeProfile(profile)),
      catchError((error: unknown) => {
        console.warn('Dil tercihi profile kaydedilemedi, yalnızca bu tarayıcıda uygulanıyor', error);
        return of(null);
      }),
      // Reload'u tetiklediği için her koşulda en son çalışır.
      tap(() => this.localeService.setLocale(locale)),
      map(() => undefined)
    );
  }

  private storeProfile(profile: UserProfile | null): void {
    if (!profile) {
      return;
    }

    try {
      localStorage.setItem('user', JSON.stringify(profile));
    } catch (error) {
      console.warn('Kullanıcı profili localStorage a yazılamadı', error);
    }
  }
}
