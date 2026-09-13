import { DOCUMENT, isPlatformBrowser } from '@angular/common';
import { Injectable, Injector, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';

import {
  AppLocale,
  DEFAULT_LOCALE,
  LocaleDefinition,
  isAppLocale,
  localeDefinitionOf,
} from '../models/locale';

export const LOCALE_STORAGE_KEY = 'app-locale';

/**
 * Aktif dil tercihini yöneten servis (issue #180, #181). `ColorSchemeService` ile aynı deseni
 * izler: signal tabanlı durum + localStorage kalıcılığı + `<html>` üzerinde tek bir DOM etkisi.
 *
 * Tercih sırası: localStorage'daki açık seçim → tarayıcı dili (`navigator.languages`) →
 * {@link DEFAULT_LOCALE}. Tarayıcı dilinden gelen sonuç localStorage'a **yazılmaz**; kullanıcı
 * menüden açıkça bir dil seçene kadar tarayıcı dili değişimlerini takip etmeye devam ederiz.
 *
 * SSR/prerender sırasında (isPlatformBrowser false) DOM, localStorage ve navigator'a dokunulmaz,
 * her zaman {@link DEFAULT_LOCALE} kullanılır. Bunun görünür sonucu: prerender edilen HTML her
 * zaman Türkçedir; `en` tercihi olan kullanıcı hydration tamamlanana kadar kısa süre TR görür.
 *
 * Dil değişince sayfa yeniden yüklenir: Angular `LOCALE_ID` bootstrap sırasında çözüldüğü için
 * `date`/`number` pipe'ları ve Material datepicker ancak yeni bir bootstrap ile yeni dili görür.
 */
@Injectable({ providedIn: 'root' })
export class LocaleService {
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  private readonly localeState = signal<AppLocale>(readStoredLocale(this.isBrowser));

  /** Aktif dil kodu. */
  readonly locale = this.localeState.asReadonly();

  /** Aktif dilin tüm tanımı (Angular locale, date-fns locale, etiket). */
  readonly localeDefinition = computed<LocaleDefinition>(() => localeDefinitionOf(this.locale()));

  constructor() {
    this.applyDocumentLang(this.locale());
  }

  setLocale(locale: AppLocale): void {
    if (!isAppLocale(locale) || locale === this.localeState()) {
      return;
    }

    this.localeState.set(locale);

    if (!this.isBrowser) {
      return;
    }

    this.applyDocumentLang(locale);

    try {
      localStorage.setItem(LOCALE_STORAGE_KEY, locale);
    } catch {
      // localStorage erişilemez (gizli mod, kota) → tercih yalnızca bu oturum boyunca geçerli olur
    }

    // Transloco lazy alınır: TRANSLOCO_CONFIG başlangıç dilini bu servisten okuduğu için
    // constructor'da enjekte edilseydi döngüsel bağımlılık oluşurdu.
    // Reload zaten geliyor, yani üretimde bu satırın görünür etkisi yok; reload'un çalışmadığı
    // ortamlarda (test, reload'u ezen alt sınıf) Transloco'nun aktif dilini tutarlı bırakır.
    this.injector.get(TranslocoService, null, { optional: true })?.setActiveLang(locale);

    this.reloadPage();
  }

  /**
   * Sunucudan gelen kullanıcı profilindeki dil tercihini uygular (issue #181).
   * Değer desteklenmiyorsa veya aktif dille aynıysa hiçbir şey yapmaz — bu sayede reload
   * sonrasında profil aynı değeri taşıdığı için ikinci kez tetiklenmez.
   */
  syncFromProfile(preferredLocale: string | null | undefined): void {
    if (!isAppLocale(preferredLocale) || preferredLocale === this.localeState()) {
      return;
    }

    this.setLocale(preferredLocale);
  }

  /** Testlerde spy'lanabilmesi için ayrı metot. */
  protected reloadPage(): void {
    this.document.defaultView?.location.reload();
  }

  private applyDocumentLang(locale: AppLocale): void {
    if (!this.isBrowser) {
      return;
    }
    this.document.documentElement.setAttribute('lang', locale);
  }
}

/**
 * Kullanıcının açık dil tercihini (localStorage) okur; yoksa tarayıcı diline düşer.
 * Servis örneği olmadan da (örn. `TRANSLOCO_CONFIG` başlangıç dili) çağrılabilmesi için
 * modül seviyesinde bir fonksiyondur.
 */
export function readStoredLocale(
  isBrowser: boolean = typeof localStorage !== 'undefined',
  languages: readonly string[] = readNavigatorLanguages()
): AppLocale {
  if (!isBrowser) {
    return DEFAULT_LOCALE;
  }

  try {
    const stored = localStorage.getItem(LOCALE_STORAGE_KEY);
    if (isAppLocale(stored)) {
      return stored;
    }
  } catch {
    // localStorage okunamıyorsa tarayıcı diline düşeriz
  }

  return detectBrowserLocale(languages);
}

/**
 * `navigator.languages` / `navigator.language` değerlerini desteklenen dillerle eşler
 * (`en-GB` → `en`, `tr-TR` → `tr`). Eşleşme yoksa {@link DEFAULT_LOCALE} döner.
 */
export function detectBrowserLocale(languages: readonly string[] = readNavigatorLanguages()): AppLocale {
  for (const tag of languages) {
    const base = tag?.split('-')[0]?.toLowerCase();
    if (isAppLocale(base)) {
      return base;
    }
  }

  return DEFAULT_LOCALE;
}

/** SSR'da `navigator` yoktur → boş liste (çağıran DEFAULT_LOCALE'e düşer). */
function readNavigatorLanguages(): readonly string[] {
  if (typeof navigator === 'undefined') {
    return [];
  }

  if (navigator.languages?.length) {
    return navigator.languages;
  }

  return navigator.language ? [navigator.language] : [];
}
