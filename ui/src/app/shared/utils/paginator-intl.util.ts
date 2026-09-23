import { DestroyRef, Provider, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { TranslocoService } from '@jsverse/transloco';
import { switchMap } from 'rxjs';

/**
 * Komponent-yerel, Transloco ile çevrilen `MatPaginatorIntl` sağlar (diğer paginator'lar etkilenmez).
 * Anahtarlar `<keyPrefix>.itemsPerPage|next|previous|first|last|range|rangeEmpty`;
 * `scope` sözlüğü her dil değişiminde yüklenip etiketler yenilenir.
 *
 * Kullanım: `providers: [provideTranslocoScope('admin'), provideTranslatedPaginatorIntl('admin', 'admin.paginator')]`
 */
export function provideTranslatedPaginatorIntl(scope: string, keyPrefix: string): Provider {
  return {
    provide: MatPaginatorIntl,
    useFactory: () => {
      const intl = new MatPaginatorIntl();
      const transloco = inject(TranslocoService);
      const text = (key: string, params?: Record<string, unknown>): string =>
        transloco.translate<string>(`${keyPrefix}.${key}`, params) ?? '';

      transloco.langChanges$
        .pipe(
          switchMap((lang) => transloco.load(`${scope}/${lang}`)),
          takeUntilDestroyed(inject(DestroyRef)),
        )
        .subscribe(() => {
          intl.itemsPerPageLabel = text('itemsPerPage');
          intl.nextPageLabel = text('next');
          intl.previousPageLabel = text('previous');
          intl.firstPageLabel = text('first');
          intl.lastPageLabel = text('last');
          intl.getRangeLabel = (page: number, size: number, length: number): string => {
            if (length === 0 || size === 0) return text('rangeEmpty', { length });
            const start = page * size + 1;
            const end = Math.min(start + size - 1, length);
            return text('range', { start, end, length });
          };
          intl.changes.next();
        });

      return intl;
    },
  };
}
