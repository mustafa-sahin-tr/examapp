import { DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Meta, Title } from '@angular/platform-browser';
import { TranslocoService } from '@jsverse/transloco';

/**
 * Landing dışındaki tanıtım sayfaları ortak `public-pages` Transloco scope'unu kullanır
 * (issue #182/#183). Scope adı tek yerde tanımlanır ki route komponentleri ve testler aynı
 * değeri paylaşsın.
 */
export const PUBLIC_PAGES_SCOPE = 'public-pages';

/**
 * `<title>` ve `description` meta etiketini scope sözlüğünden ayarlar. `selectTranslate` scope'u
 * yükler ve dil değişiminde yeniden yayınlar; sözlük hazır olduğunda açıklama senkron
 * `translate()` ile okunabilir.
 *
 * Injection context içinden (komponent constructor'ı / alan başlatıcı) çağrılmalıdır.
 *
 * @param section Sözlükteki bölüm adı, örn. `terms` → `terms.meta.title` / `terms.meta.description`
 * @param extraTags Sayfaya özel ek meta etiketleri (çevrilmeyen sabitler).
 */
export function usePublicPageMeta(section: string, extraTags: ReadonlyArray<Record<string, string>> = []): void {
  const transloco = inject(TranslocoService);
  const titleService = inject(Title);
  const metaService = inject(Meta);
  const destroyRef = inject(DestroyRef);

  transloco
    .selectTranslate<string>(`${section}.meta.title`, {}, PUBLIC_PAGES_SCOPE)
    .pipe(takeUntilDestroyed(destroyRef))
    .subscribe((title) => {
      titleService.setTitle(title);
      metaService.updateTag({
        name: 'description',
        content: transloco.translate<string>(`${PUBLIC_PAGES_SCOPE}.${section}.meta.description`) ?? '',
      });
      for (const tag of extraTags) {
        metaService.updateTag(tag);
      }
    });
}
