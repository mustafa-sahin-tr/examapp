import type { Translation } from '@jsverse/transloco';
import localeEn from '@angular/common/locales/en';
import localeTr from '@angular/common/locales/tr';
import type { Locale as DateFnsLocale } from 'date-fns';
import { enUS } from 'date-fns/locale/en-US';
import { tr } from 'date-fns/locale/tr';

/**
 * Uygulamanın desteklediği dillerin **tek tanım noktası** (issue #180).
 *
 * Yeni bir dil eklemek için:
 *  1. `public/i18n/<code>.json` dosyasını mevcut sözlüklerle aynı anahtar setiyle oluştur,
 *  2. Aşağıdaki `SUPPORTED_LOCALES` dizisine bir satır ekle (`serverDictionary` dahil).
 *
 * Başka hiçbir yerde dil listesi tutulmaz; Transloco `availableLangs`, `registerLocaleData`,
 * `LOCALE_ID`, `MAT_DATE_LOCALE` ve dil değiştirme menüsü hep bu listeden türer.
 */
export interface LocaleDefinition {
  /** Sözlük dosyası adı ve localStorage'da saklanan değer (`public/i18n/<code>.json`). */
  readonly code: string;
  /** Dil seçicide gösterilen ad — kendi dilinde yazılır, çeviriye tabi değildir. */
  readonly label: string;
  /** Angular `LOCALE_ID` değeri (date/number pipe'ları bunu kullanır). */
  readonly angularLocale: string;
  /** `registerLocaleData` için Angular locale verisi. */
  readonly angularLocaleData: unknown[];
  /** Material date-fns adapter'ının (`MAT_DATE_LOCALE`) beklediği date-fns locale nesnesi. */
  readonly dateFnsLocale: DateFnsLocale;
  /**
   * SSR/prerender sırasında kullanılan, build'e gömülü sözlük. Sunucuda `/i18n/<code>.json`
   * çekecek bir HTTP sunucusu olmadığı için `TranslocoHttpLoader` bu lazy `import()`'u okur.
   */
  readonly serverDictionary: () => Promise<{ default: Translation }>;
}

export const SUPPORTED_LOCALES = [
  {
    code: 'tr',
    label: 'Türkçe',
    angularLocale: 'tr',
    angularLocaleData: localeTr,
    dateFnsLocale: tr,
    serverDictionary: () => import('../../../public/i18n/tr.json'),
  },
  {
    code: 'en',
    label: 'English',
    angularLocale: 'en-US',
    angularLocaleData: localeEn,
    dateFnsLocale: enUS,
    serverDictionary: () => import('../../../public/i18n/en.json'),
  },
] as const satisfies readonly LocaleDefinition[];

/** Desteklenen dil kodları — listeden türer, elle yazılmaz. */
export type AppLocale = (typeof SUPPORTED_LOCALES)[number]['code'];

export const DEFAULT_LOCALE: AppLocale = 'tr';

/** Transloco `availableLangs` ve dil seçici menüsü için kod listesi. */
export const SUPPORTED_LOCALE_CODES: readonly AppLocale[] = SUPPORTED_LOCALES.map((locale) => locale.code);

export function isAppLocale(value: unknown): value is AppLocale {
  return typeof value === 'string' && SUPPORTED_LOCALE_CODES.includes(value as AppLocale);
}

export function localeDefinitionOf(code: AppLocale): LocaleDefinition {
  return SUPPORTED_LOCALES.find((locale) => locale.code === code) ?? SUPPORTED_LOCALES[0];
}
