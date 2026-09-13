import { HttpClient } from '@angular/common/http';
import { isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, PendingTasks, inject } from '@angular/core';
import { Translation, TranslocoLoader } from '@jsverse/transloco';
import { Observable, from, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';

import { AppLocale, isAppLocale, localeDefinitionOf } from '../models/locale';

/**
 * Geçerli bir Transloco scope adı: küçük harfle başlar, küçük harf/rakam/tire ile devam eder.
 * Dinamik `import()` şablonuna sadece bu kalıba uyan değerler girer — beklenmeyen bir scope adı
 * (örn. `../secret`) dosya sistemine sızmasın diye.
 */
const SCOPE_PATTERN = /^[a-z][a-z0-9-]*$/;

/**
 * Sözlükleri `public/i18n/<lang>.json` (kök) ve `public/i18n/<scope>/<lang>.json` (sayfa/alan
 * scope'u) üzerinden yükler (issue #180, #182).
 *
 * Transloco scope'lu bir sözlük istediğinde `lang` parametresi `"<scope>/<lang>"` biçiminde gelir
 * (örn. `landing/tr`); sözlük içeriği aktif dilin sözlüğüne `<scope>.` önekiyle merge edilir.
 *
 * Tarayıcıda HttpClient ile `/i18n/<path>.json` çekilir. SSR/prerender sırasında ortada dinlenen bir
 * HTTP sunucusu olmadığı için aynı JSON dosyaları build'e gömülü olarak okunur:
 *  - kök sözlük → `LocaleDefinition.serverDictionary` (tek tanım noktası `SUPPORTED_LOCALES`),
 *  - scope sözlüğü → şablonlu dinamik `import()`; esbuild `public/i18n/<*>/<*>.json` desenini
 *    glob'layıp her scope dosyası için ayrı bir chunk üretir, yani yeni bir scope eklemek için
 *    burada kod değişmez.
 * Bu sayede prerender edilen HTML boş anahtar yerine gerçek metni içerir.
 *
 * Dil listesi burada tutulmaz: desteklenen diller tek tanım noktası olan `SUPPORTED_LOCALES`'ten
 * gelir, bilinmeyen bir `lang` (ya da geçersiz bir scope) boş sözlüğe düşer.
 */
@Injectable({ providedIn: 'root' })
export class TranslocoHttpLoader implements TranslocoLoader {
  private readonly http = inject(HttpClient);
  private readonly pendingTasks = inject(PendingTasks);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  getTranslation(path: string): Observable<Translation> {
    const parsed = parseTranslationPath(path);
    if (!parsed) {
      return of({});
    }

    if (!this.isBrowser) {
      // `import()` yerel bir Promise döndürür; zone.js onu görmediği için `PendingTasks` olmadan
      // uygulama sözlük gelmeden "stable" sayılır ve prerender boş şablonu yazar.
      return from(this.pendingTasks.run(() => loadServerDictionary(parsed))).pipe(
        map((module) => module.default),
        catchError(() => of({}))
      );
    }

    return this.http.get<Translation>(`/i18n/${path}.json`);
  }
}

interface TranslationPath {
  /** Scope adı; kök sözlükte `null`. */
  readonly scope: string | null;
  readonly lang: AppLocale;
}

/** `"tr"` → kök sözlük, `"landing/tr"` → scope sözlüğü. Tanımsız dil/scope için `null`. */
function parseTranslationPath(path: string): TranslationPath | null {
  const segments = path.split('/');
  const lang = segments.pop();

  if (!isAppLocale(lang)) {
    return null;
  }

  if (segments.length === 0) {
    return { scope: null, lang };
  }

  const scope = segments.join('/');
  return SCOPE_PATTERN.test(scope) ? { scope, lang } : null;
}

function loadServerDictionary({ scope, lang }: TranslationPath): Promise<{ default: Translation }> {
  return scope === null
    ? localeDefinitionOf(lang).serverDictionary()
    : import(`../../../public/i18n/${scope}/${lang}.json`);
}
