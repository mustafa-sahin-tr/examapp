import { HttpClient } from '@angular/common/http';
import { isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, inject } from '@angular/core';
import { Translation, TranslocoLoader } from '@jsverse/transloco';
import { Observable, from, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';

/**
 * Sözlükleri `public/i18n/<lang>.json` üzerinden yükler (issue #180).
 *
 * Tarayıcıda HttpClient ile `/i18n/<lang>.json` çekilir. SSR/prerender sırasında ortada dinlenen bir
 * HTTP sunucusu olmadığı için aynı JSON dosyaları build'e gömülü olarak (lazy `import()`) okunur;
 * bu sayede prerender edilen HTML boş anahtar yerine gerçek metni içerir.
 *
 * Yeni dil eklendiğinde `serverTranslations` haritasına da bir satır eklenmelidir; eklenmezse
 * yalnızca sunucu tarafı render boş sözlükle çalışır, tarayıcı tarafı etkilenmez.
 */
const serverTranslations: Record<string, () => Promise<{ default: Translation }>> = {
  tr: () => import('../../../public/i18n/tr.json'),
  en: () => import('../../../public/i18n/en.json'),
};

@Injectable({ providedIn: 'root' })
export class TranslocoHttpLoader implements TranslocoLoader {
  private readonly http = inject(HttpClient);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  getTranslation(lang: string): Observable<Translation> {
    if (!this.isBrowser) {
      const load = serverTranslations[lang];
      if (!load) {
        return of({});
      }
      return from(load()).pipe(
        map((module) => module.default),
        catchError(() => of({}))
      );
    }

    return this.http.get<Translation>(`/i18n/${lang}.json`);
  }
}
