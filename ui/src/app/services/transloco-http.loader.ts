import { HttpClient } from '@angular/common/http';
import { isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, inject } from '@angular/core';
import { Translation, TranslocoLoader } from '@jsverse/transloco';
import { Observable, from, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';

import { isAppLocale, localeDefinitionOf } from '../models/locale';

/**
 * Sözlükleri `public/i18n/<lang>.json` üzerinden yükler (issue #180).
 *
 * Tarayıcıda HttpClient ile `/i18n/<lang>.json` çekilir. SSR/prerender sırasında ortada dinlenen bir
 * HTTP sunucusu olmadığı için aynı JSON dosyaları build'e gömülü olarak okunur
 * (`LocaleDefinition.serverDictionary` → lazy `import()`); bu sayede prerender edilen HTML boş
 * anahtar yerine gerçek metni içerir.
 *
 * Dil listesi burada tutulmaz: desteklenen diller tek tanım noktası olan `SUPPORTED_LOCALES`'ten
 * gelir, bilinmeyen bir `lang` değeri boş sözlüğe düşer.
 */
@Injectable({ providedIn: 'root' })
export class TranslocoHttpLoader implements TranslocoLoader {
  private readonly http = inject(HttpClient);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  getTranslation(lang: string): Observable<Translation> {
    if (!isAppLocale(lang)) {
      return of({});
    }

    if (!this.isBrowser) {
      return from(localeDefinitionOf(lang).serverDictionary()).pipe(
        map((module) => module.default),
        catchError(() => of({}))
      );
    }

    return this.http.get<Translation>(`/i18n/${lang}.json`);
  }
}
