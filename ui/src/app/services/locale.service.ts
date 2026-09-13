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
 * Aktif dil tercihini yöneten servis (issue #180). `ColorSchemeService` ile aynı deseni izler:
 * signal tabanlı durum + localStorage kalıcılığı + `<html>` üzerinde tek bir DOM etkisi.
 *
 * SSR/prerender sırasında (isPlatformBrowser false) DOM ve localStorage'a dokunulmaz,
 * her zaman {@link DEFAULT_LOCALE} kullanılır.
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
    this.injector.get(TranslocoService, null, { optional: true })?.setActiveLang(locale);

    this.reloadPage();
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
 * localStorage'daki dil tercihini okur. Servis örneği olmadan da (örn. `TRANSLOCO_CONFIG`
 * başlangıç dili) çağrılabilmesi için modül seviyesinde bir fonksiyondur.
 */
export function readStoredLocale(isBrowser: boolean = typeof localStorage !== 'undefined'): AppLocale {
  if (!isBrowser) {
    return DEFAULT_LOCALE;
  }

  try {
    const stored = localStorage.getItem(LOCALE_STORAGE_KEY);
    return isAppLocale(stored) ? stored : DEFAULT_LOCALE;
  } catch {
    return DEFAULT_LOCALE;
  }
}
