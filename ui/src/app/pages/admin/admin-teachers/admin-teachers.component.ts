import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';
import { TranslocoDirective, TranslocoPipe, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Subject, catchError, map, of, switchMap, take, tap } from 'rxjs';
import { AdminService } from '../../../services/admin.service';
import { AdminTeacherApprovalStatus, AdminTeacherListItem } from '../../../models/admin-teacher.model';
import { Paged } from '../../../models/test-instance';
import { SchoolFilterComponent } from '../../../shared/components/school-filter/school-filter.component';
import {
  SchoolFilterValue,
  schoolFilterFromParams,
  schoolFilterToQuery,
} from '../../../shared/components/school-filter/school-filter.model';
import {
  DEFAULT_PAGE_SIZE,
  PAGE_SIZE_OPTIONS,
  lastPageIndex,
  parsePageIndex,
  parsePageSize,
} from '../../../shared/utils/paging.util';
import { provideTranslatedPaginatorIntl } from '../../../shared/utils/paginator-intl.util';

/** Yönetim ekranlarının ortak Transloco scope'u: `public/i18n/admin/<lang>.json` (issue #183). */
const ADMIN_SCOPE = 'admin';

type ApprovalKey = 'pending' | 'approved' | 'rejected';
type AccountStatus = 'active' | 'inactive' | 'unknown';

const APPROVAL_KEYS: Record<AdminTeacherApprovalStatus, ApprovalKey> = {
  Pending: 'pending',
  Approved: 'approved',
  Rejected: 'rejected',
};

/** Şablonun doğrudan bastığı satır modeli; boş/null alanların yorumu burada tek yerde yapılır. */
export interface AdminTeacherRow {
  id: number;
  /** null → "—" */
  fullName: string | null;
  /** null → "—" */
  email: string | null;
  /** null → bağımsızsa "Bağımsız", değilse "—" */
  schoolName: string | null;
  independent: boolean;
  approvalKey: ApprovalKey;
  accountStatus: AccountStatus;
}

type LoadResult =
  | { ok: true; page: Paged<AdminTeacherListItem> }
  | { ok: false; error: HttpErrorResponse };

/**
 * Issue #152 — Admin öğretmen listesi: server-side sayfalama + okul filtresi.
 * URL tek doğruluk kaynağıdır: filtre/sayfa `?schoolId=5&page=2`, `?unassigned=true` query param'larında
 * tutulur; state yalnızca `queryParamMap` aboneliğinden kurulur ve yükleme oradan tetiklenir.
 * Kullanıcı etkileşimi sadece URL'i günceller. Böylece derin link, geri/ileri ve aynı bileşenin
 * yeniden kullanımı (menüden param'sız `/admin/teachers`) tutarlı çalışır. Filtre değişince 1. sayfaya
 * dönülür. Çakışan istekler `switchMap` ile iptal edilir; en son seçimin yanıtı kazanır.
 */
@Component({
  selector: 'app-admin-teachers',
  standalone: true,
  imports: [
    MatButtonModule,
    MatIconModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    MatTableModule,
    TranslocoDirective,
    TranslocoPipe,
    SchoolFilterComponent,
  ],
  providers: [
    provideTranslocoScope(ADMIN_SCOPE),
    provideTranslatedPaginatorIntl(ADMIN_SCOPE, `${ADMIN_SCOPE}.paginator`),
  ],
  templateUrl: './admin-teachers.component.html',
  styleUrls: ['./admin-teachers.component.scss'],
})
export class AdminTeachersComponent {
  private readonly adminService = inject(AdminService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly transloco = inject(TranslocoService);

  readonly displayedColumns = ['fullName', 'email', 'school', 'approvalStatus', 'accountStatus'];
  readonly pageSizeOptions = PAGE_SIZE_OPTIONS;

  readonly filter = signal<SchoolFilterValue>('all');
  /** 0-tabanlı (MatPaginator); backend'e `pageIndex + 1` gider. */
  readonly pageIndex = signal(0);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly teachers = signal<AdminTeacherListItem[]>([]);
  readonly totalCount = signal(0);

  readonly rows = computed<AdminTeacherRow[]>(() => this.teachers().map(toRow));
  readonly isEmpty = computed(() => !this.loading() && !this.error() && this.teachers().length === 0);
  /** İlk yükleme / boş listeden yükleme → tam spinner; sayfa geçişlerinde tablo kalır, üstte ince bar. */
  readonly showSpinner = computed(() => this.loading() && this.teachers().length === 0);
  readonly isFiltered = computed(() => this.filter() !== 'all');

  private readonly reload$ = new Subject<void>();

  constructor() {
    // Hata metinleri şablon dışında senkron `translate()` ile okunur; scope baştan yüklensin.
    this.transloco.load(`${ADMIN_SCOPE}/${this.transloco.getActiveLang()}`).pipe(take(1)).subscribe();

    this.reload$
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.error.set(null);
        }),
        switchMap(() =>
          this.adminService
            .getTeachers({
              page: this.pageIndex() + 1,
              pageSize: this.pageSize(),
              ...schoolFilterToQuery(this.filter()),
            })
            .pipe(
              map((page): LoadResult => ({ ok: true, page })),
              catchError((error: HttpErrorResponse) => of<LoadResult>({ ok: false, error })),
            ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => this.applyResult(result));

    // Tek yönlü akış: URL → state → istek. İlk emisyon ilk yüklemeyi de tetikler.
    this.route.queryParamMap
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((params) => {
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

  private applyResult(result: LoadResult): void {
    if (!result.ok) {
      this.loading.set(false);
      this.teachers.set([]);
      this.totalCount.set(0);
      this.error.set(this.errorMessage(result.error));
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
    this.teachers.set(items);
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

  private errorMessage(err: HttpErrorResponse): string {
    if (err.status === 403) return this.text('forbidden');
    // Backend 400'de (filtre çakışması) yerelleştirilmiş düz metin döner.
    if (err.status === 400 && typeof err.error === 'string' && err.error.trim()) return err.error;
    return this.text('loadFailed');
  }

  /** Scope'a göreli anahtarı senkron çözer. */
  private text(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${ADMIN_SCOPE}.teachers.${key}`, params) ?? '';
  }
}

function toRow(item: AdminTeacherListItem): AdminTeacherRow {
  return {
    id: item.id,
    fullName: item.fullName?.trim() || null,
    email: item.email?.trim() || null,
    schoolName: item.schoolName?.trim() || null,
    independent: item.isIndependentTutor,
    approvalKey: APPROVAL_KEYS[item.approvalStatus] ?? 'pending',
    accountStatus: item.isEnabled === true ? 'active' : item.isEnabled === false ? 'inactive' : 'unknown',
  };
}
