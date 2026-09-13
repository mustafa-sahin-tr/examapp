import { TranslocoTestingModule, TranslocoTestingOptions } from '@jsverse/transloco';

import trTranslations from '../../../../public/i18n/tr.json';
import enTranslations from '../../../../public/i18n/en.json';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../models/locale';

/**
 * Paylaşılan komponent testleri için **gerçek** kök sözlüğü yükleyen Transloco kurulumu
 * (issue #183). Sahte çeviri kullanılmaz: bir anahtar bozulur/silinirse test kırılır.
 *
 * Kullanım:
 * ```ts
 * TestBed.configureTestingModule({ imports: [MyComponent, translocoTestingModule()] });
 * ```
 * Scope'lu bir sözlüğe ihtiyaç varsa `langs` ile eklenir:
 * `translocoTestingModule({ langs: { 'landing/tr': landingTr } })`.
 */
export function translocoTestingModule(options: TranslocoTestingOptions = {}) {
  return TranslocoTestingModule.forRoot({
    ...options,
    langs: { tr: trTranslations, en: enTranslations, ...(options.langs ?? {}) },
    translocoConfig: {
      availableLangs: [...SUPPORTED_LOCALE_CODES],
      defaultLang: DEFAULT_LOCALE,
      // `app.config.ts` ile aynı olmalı: tireli scope adları camelCase'e çevrilmesin,
      // aksi hâlde scope'lu sözlükler bulunamaz ve ham anahtar render edilir.
      scopes: { keepCasing: true },
      ...(options.translocoConfig ?? {}),
    },
    preloadLangs: true,
  });
}
