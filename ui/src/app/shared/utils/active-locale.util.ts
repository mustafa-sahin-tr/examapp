import { AppLocale, DEFAULT_LOCALE, isAppLocale, localeDefinitionOf } from '../../models/locale';

/**
 * Angular DI'sı olmayan saf yardımcılar için aktif dil kaynağı (issue #183).
 *
 * `LocaleService` aktif dili `<html lang>` üzerine yazar; buradan okumak servisin durumunu
 * tek kaynak olarak korur ve util'lerin `Injector` gerektirmeden `Intl` çağrılarını doğru
 * dile bağlamasını sağlar. SSR'da `document` yoktur → {@link DEFAULT_LOCALE}.
 *
 * Dil değişiminde sayfa yeniden yüklendiği için (bkz. `LocaleService`) sonuç bir istek
 * ömrü boyunca sabittir; çağıran taraf formatter'ları güvenle önbelleğe alabilir.
 */
export function activeAppLocale(): AppLocale {
  if (typeof document === 'undefined') {
    return DEFAULT_LOCALE;
  }
  const lang = document.documentElement.getAttribute('lang');
  return isAppLocale(lang) ? lang : DEFAULT_LOCALE;
}

/** `Intl.*` / `toLocale*` çağrılarında kullanılacak locale kodu (`tr`, `en-US`). */
export function activeIntlLocale(): string {
  return localeDefinitionOf(activeAppLocale()).angularLocale;
}
