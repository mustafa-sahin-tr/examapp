import { DestroyRef, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { PageEvent } from '@angular/material/paginator';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { Observable, Subject, catchError, map, of, switchMap, tap } from 'rxjs';
import { Paged } from '../../models/test-instance';
import { AdminSchoolPagedQuery } from '../../models/admin-paged-query.model';
import {
  SchoolFilterValue,
  schoolFilterFromParams,
  schoolFilterToQuery,
} from '../components/school-filter/school-filter.model';
import { DEFAULT_PAGE_SIZE, lastPageIndex, parsePageIndex, parsePageSize } from './paging.util';

type LoadResult<T> = { ok: true; page: Paged<T> } | { ok: false; error: HttpErrorResponse };

export interface SchoolPagedListOptions<T> {
  /** Sayfa isteği; `schoolId` ve `unassigned` asla birlikte dolu gelmez. */
  fetch: (query: AdminSchoolPagedQuery) => Observable<Paged<T>>;
  /** Hata → kullanıcıya gösterilecek metin. */
  errorMessage: (error: HttpErrorResponse) => string;
}

/**
 * Admin listeleri (öğretmen #152, öğrenci #153) için okul filtreli, server-side sayfalı liste durumu.
 * **Injection context içinde** oluşturulmalıdır (komponent alan başlatıcısı / constructor).
 *
 * URL tek doğruluk kaynağıdır: filtre/sayfa `?schoolId=5&page=2`, `?unassigned=true` query param'larında
 * tutulur; state yalnızca `queryParamMap` aboneliğinden kurulur ve yükleme oradan tetiklenir.
 * Kullanıcı etkileşimi sadece URL'i günceller. Böylece derin link, geri/ileri ve aynı bileşenin
 * yeniden kullanımı (menüden param'sız giriş) tutarlı çalışır. Filtre değişince 1. sayfaya dönülür.
 * Çakışan istekler `switchMap` ile iptal edilir; en son seçimin yanıtı kazanır.
 */
export class SchoolPagedList<T> {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly filter = signal<SchoolFilterValue>('all');
  /** 0-tabanlı (MatPaginator); backend'e `pageIndex + 1` gider. */
  readonly pageIndex = signal(0);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly items = signal<T[]>([]);
  readonly totalCount = signal(0);

  readonly isEmpty = computed(() => !this.loading() && !this.error() && this.items().length === 0);
  /** İlk yükleme / boş listeden yükleme → tam spinner; sayfa geçişlerinde tablo kalır, üstte ince bar. */
  readonly showSpinner = computed(() => this.loading() && this.items().length === 0);
  readonly isFiltered = computed(() => this.filter() !== 'all');

  private readonly reload$ = new Subject<void>();

  constructor(private readonly options: SchoolPagedListOptions<T>) {
    this.reload$
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.error.set(null);
        }),
        switchMap(() =>
          this.options
            .fetch({
              page: this.pageIndex() + 1,
              pageSize: this.pageSize(),
              ...schoolFilterToQuery(this.filter()),
            })
            .pipe(
              map((page): LoadResult<T> => ({ ok: true, page })),
              catchError((error: HttpErrorResponse) => of<LoadResult<T>>({ ok: false, error })),
            ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => this.applyResult(result));

    // Tek yönlü akış: URL → state → istek. İlk emisyon ilk yüklemeyi de tetikler.
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      this.applyParams(params);
      this.load();
    });
  }

  /** Mevcut URL state'iyle yeniden yükler (Yenile / Tekrar dene). */
  load(): void {
    this.reload$.next();
  }

  /** Filtre değişince 1. sayfaya dönülür; yükleme URL aboneliğinden gelir. */
  onFilterChange(value: SchoolFilterValue): void {
    this.navigate(value, 0, this.pageSize());
  }

  onPage(event: PageEvent): void {
    this.navigate(this.filter(), event.pageIndex, event.pageSize);
  }

  private applyParams(params: ParamMap): void {
    this.filter.set(schoolFilterFromParams(params.get('schoolId'), params.get('unassigned')));
    this.pageIndex.set(parsePageIndex(params.get('page')));
    this.pageSize.set(parsePageSize(params.get('pageSize')));
  }

  private applyResult(result: LoadResult<T>): void {
    if (!result.ok) {
      this.loading.set(false);
      this.items.set([]);
      this.totalCount.set(0);
      this.error.set(this.options.errorMessage(result.error));
      return;
    }

    const { items, totalCount } = result.page;
    // Derin link son sayfanın ötesini gösteriyorsa (ör. ?page=99) son dolu sayfaya dön. Yalnız
    // gerçekten ötesindeyken: aksi hâlde (ör. araya giren silme) boş durum gösterilir, döngü olmaz.
    const last = lastPageIndex(totalCount, this.pageSize());
    if (items.length === 0 && totalCount > 0 && this.pageIndex() > last) {
      this.navigate(this.filter(), last, this.pageSize());
      return;
    }

    this.loading.set(false);
    this.items.set(items);
    this.totalCount.set(totalCount);
  }

  /** Varsayılan değerler URL'e yazılmaz; `merge` ile sayfaya ait olmayan param'lar korunur. */
  private navigate(filter: SchoolFilterValue, pageIndex: number, pageSize: number): void {
    const { schoolId, unassigned } = schoolFilterToQuery(filter);
    const page = pageIndex + 1;
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        schoolId,
        unassigned: unassigned ? 'true' : null,
        page: page > 1 ? page : null,
        pageSize: pageSize !== DEFAULT_PAGE_SIZE ? pageSize : null,
      },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }
}

/**
 * Admin liste hatalarının ortak yorumu: 403 → yetki metni; backend'in 400'de döndüğü yerelleştirilmiş
 * düz metin (filtre çakışması) olduğu gibi; diğerleri → genel yükleme hatası.
 */
export function adminListErrorMessage(
  err: HttpErrorResponse,
  text: (key: 'forbidden' | 'loadFailed') => string,
): string {
  if (err.status === 403) return text('forbidden');
  if (err.status === 400 && typeof err.error === 'string' && err.error.trim()) return err.error;
  return text('loadFailed');
}
