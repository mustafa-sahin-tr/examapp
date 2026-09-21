import { Injectable } from '@angular/core';

/**
 * auth-ui'nin desteklediği dil kodları (issue #186).
 *
 * DİKKAT: Bu liste ana uygulamadaki `ui/src/app/models/locale.ts` → `SUPPORTED_LOCALES`
 * ve backend'deki `ExamApp.Foundation.Localization.SupportedLocales` ile **elle** senkron
 * tutulur. auth-ui login öncesi çalıştığı ve Transloco kurulumu olmadığı için burada
 * sadece kodların küçük bir kopyası bulunur — yeni bir dil eklerken üç yeri de güncelle.
 */
export const SUPPORTED_LOGIN_LOCALES = ['tr', 'en'] as const;

export type LoginLocale = (typeof SUPPORTED_LOGIN_LOCALES)[number];

/** Ana uygulamanın dil tercihini sakladığı localStorage anahtarı (`ui/src/app/services/locale.service.ts`). */
export const LOCALE_STORAGE_KEY = 'app-locale';

/** Hiçbir ipucu bulunamadığında kullanılan dil. */
export const DEFAULT_LOGIN_LOCALE: LoginLocale = 'tr';

function isLoginLocale(value: unknown): value is LoginLocale {
  return typeof value === 'string' && (SUPPORTED_LOGIN_LOCALES as readonly string[]).includes(value);
}

/**
 * Keycloak login ekranının hangi dilde açılacağını belirler (issue #186).
 *
 * auth-ui ve ana uygulama aynı origin'de (gateway :5678) servis edildiği için
 * (`Services/Gateway/ocelot.json` → `/app/{everything}` → auth-ui) localStorage paylaşılır;
 * kullanıcının ana uygulamada seçtiği dil buradan okunabilir.
 */
@Injectable({ providedIn: 'root' })
export class LocaleHintService {
  /**
   * Öncelik sırası:
   *  1. Ana uygulamanın kaydettiği `localStorage['app-locale']` (geçerliyse),
   *  2. Tarayıcı dil listesinde ilk eşleşen desteklenen dil ön eki,
   *  3. `'tr'`.
   */
  resolveLoginLocale(): LoginLocale {
    return this.storedLocale() ?? this.browserLocale() ?? DEFAULT_LOGIN_LOCALE;
  }

  /**
   * Sunucudan gelen profil bilgisindeki `preferredLocale`'i, geçerliyse, ana uygulamanın
   * okuduğu anahtara yazar — böylece bir sonraki `/oidc-login` doğru dille açılır.
   * Geçersiz/boş değerde mevcut kayıt korunur.
   */
  rememberPreferredLocale(preferredLocale: string | null | undefined): void {
    const normalized = this.normalize(preferredLocale);
    if (!normalized || typeof window === 'undefined') {
      return;
    }

    try {
      localStorage.setItem(LOCALE_STORAGE_KEY, normalized);
    } catch {
      // Private mode / kota hatası: dil ipucu kritik değil, sessizce geç.
    }
  }

  private storedLocale(): LoginLocale | null {
    if (typeof window === 'undefined') {
      return null;
    }

    try {
      return this.normalize(localStorage.getItem(LOCALE_STORAGE_KEY));
    } catch {
      return null;
    }
  }

  private browserLocale(): LoginLocale | null {
    if (typeof navigator === 'undefined') {
      return null;
    }

    const candidates = navigator.languages?.length ? navigator.languages : [navigator.language];
    for (const candidate of candidates) {
      const normalized = this.normalize(candidate);
      if (normalized) {
        return normalized;
      }
    }

    return null;
  }

  /** `tr-TR` / `EN_us` gibi değerleri desteklenen koda indirger; desteklenmiyorsa `null`. */
  private normalize(value: string | null | undefined): LoginLocale | null {
    if (!value) {
      return null;
    }

    const code = value.trim().toLowerCase().split(/[-_]/)[0];
    return isLoginLocale(code) ? code : null;
  }
}
