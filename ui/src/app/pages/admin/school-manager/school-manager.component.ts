import { Component, DestroyRef, computed, inject, input, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import {
  TranslocoDirective,
  TranslocoPipe,
  TranslocoService,
  provideTranslocoScope,
} from '@jsverse/transloco';
import { Subject, debounceTime, firstValueFrom, take } from 'rxjs';
import { AdminService } from '../../../services/admin.service';
import { ApiResult, DistrictDto, ProvinceDto, School } from '../../../models/taxonomy';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../../shared/components/confirm-dialog/confirm-dialog.component';
import {
  EMPTY_SCHOOL_LIST_FILTER,
  MAX_SEARCH_LENGTH,
  SCHOOL_LIST_QUERY_KEYS,
  SchoolListFilter,
  filterSchools,
  isSchoolListFilterActive,
  sanitizeSchoolListFilter,
  schoolListFilterFromParams,
  schoolListFilterToParams,
} from './school-list-filter';

/**
 * Issue #150 — Okul yönetimi (liste, yeni okul formu, satır içi düzenle/sil). Eskiden
 * `TaxonomyManagerComponent`'in altına gömülüydü; davranış birebir taşındı.
 *
 * Hem `/admin/schools` route'undan hem admin-home "Okullar" sekmesinden açılır; bu yüzden
 * yönetim ekranlarının ortak Transloco scope'unu (`public/i18n/admin/<lang>.json`, issue #183)
 * admin-home'dan devralmaz, provider'ı burada da verir.
 */
const ADMIN_SCOPE = 'admin';

/** Ad aramasında yazma bitince URL'e yazılana kadar beklenen süre (ms). */
export const SCHOOL_SEARCH_DEBOUNCE_MS = 300;

@Component({
  selector: 'app-school-manager',
  standalone: true,
  templateUrl: './school-manager.component.html',
  styleUrls: ['./school-manager.component.scss'],
  imports: [
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatProgressBarModule,
    MatDialogModule,
    MatSnackBarModule,
    TranslocoDirective,
    TranslocoPipe,
  ],
  providers: [provideTranslocoScope(ADMIN_SCOPE)],
})
export class SchoolManagerComponent implements OnInit {
  private readonly admin = inject(AdminService);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  /** admin-home sekmesine gömülüyken true: sayfa başlığı ve kenar boşluğu admin-home'dan gelir. */
  readonly embedded = input(false);

  readonly busy = signal(false);
  readonly schoolsLoading = signal(false);
  readonly schoolsError = signal<string | null>(null);
  readonly schools = signal<School[]>([]);

  // ---- il / ilçe (Issue #91) ----
  readonly provinces = signal<ProvinceDto[]>([]);
  readonly provincesLoading = signal(false);
  readonly provincesError = signal<string | null>(null);
  /** İl id → ilçeleri. Yeni-okul formu ve satır düzenleme aynı anda açık olabildiği için il bazlı önbellek. */
  readonly districts = signal<Record<number, DistrictDto[]>>({});
  /** Şu an ilçeleri yüklenen il id'si; yoksa null. */
  readonly districtsLoading = signal<number | null>(null);

  readonly newSchoolProvinceId = signal<number | null>(null);
  readonly newSchoolDistrictId = signal<number | null>(null);
  readonly newSchoolDistricts = computed(() => this.districtsOf(this.newSchoolProvinceId()));
  readonly newSchoolDistrictsLoading = computed(
    () => this.newSchoolProvinceId() != null && this.districtsLoading() === this.newSchoolProvinceId()
  );

  readonly editProvinceId = signal<number | null>(null);
  readonly editDistrictId = signal<number | null>(null);
  readonly editDistricts = computed(() => this.districtsOf(this.editProvinceId()));
  readonly editDistrictsLoading = computed(
    () => this.editProvinceId() != null && this.districtsLoading() === this.editProvinceId()
  );

  // ---- liste filtresi (Issue #281) ----
  /**
   * URL'den okunan filtre — tek doğruluk kaynağı URL'dir (öğretmen/öğrenci listeleriyle aynı kalıp):
   * etkileşim yalnız `navigate` eder, state `queryParamMap` aboneliğinden kurulur. Okul ekle/düzenle/sil
   * yalnız `schools`'u yeniler; filtre URL'de kaldığı için korunur.
   */
  private readonly urlFilter = signal<SchoolListFilter>(EMPTY_SCHOOL_LIST_FILTER);
  /** Yüklü il/ilçe listelerine göre doğrulanmış filtre; listede olmayan id'ler yok sayılır. */
  readonly filter = computed(() => sanitizeSchoolListFilter(this.urlFilter(), this.provinces(), this.districts()));
  /** Arama kutusunun anlık metni; URL'e debounce ile yazılır. */
  readonly searchText = signal('');
  readonly filteredSchools = computed(() => filterSchools(this.schools(), this.filter()));
  readonly isFiltered = computed(() => isSchoolListFilterActive(this.filter()));
  readonly filterDistricts = computed(() => this.districtsOf(this.filter().provinceId));
  readonly filterDistrictsLoading = computed(
    () => this.filter().provinceId != null && this.districtsLoading() === this.filter().provinceId
  );
  /**
   * URL'deki il/ilçe henüz doğrulanamadı (iller ya da o ilin ilçeleri yükleniyor). Bu sürede sonuç boşsa
   * "filtreye uyan okul yok" yerine yükleniyor gösterilir; geçersiz id düzeltilince liste gelir.
   */
  readonly filterPending = computed(() => {
    const raw = this.urlFilter();
    if (raw.provinceId == null) return false;
    if (this.provincesLoading()) return true;
    return raw.districtId != null && this.districtsLoading() === raw.provinceId;
  });
  /** Arama kutusu `maxlength`'i; URL'den okunan `q` da bu uzunlukta kırpılır. */
  readonly maxSearchLength = MAX_SEARCH_LENGTH;
  private readonly search$ = new Subject<string>();
  /** En son URL'e yazdığımız arama; URL'den dönen kendi değerimiz kutudaki daha yeni metni ezmesin. */
  private lastWrittenQ: string | null = null;

  // inline add fields
  newSchoolName = '';
  newSchoolAddressLine = '';

  // inline edit state
  /** Düzenlenen okulun id'si; düzenleme yoksa null. */
  readonly editingId = signal<number | null>(null);
  editName = '';
  editAddressLine = '';

  constructor() {
    this.preloadScope();

    // `distinctUntilChanged` yerine geçerli URL ile karşılaştırılır: "lise" → temizle → 300 ms içinde
    // yine "lise" yazılınca son yayılan değer aynı olsa da URL boştur ve yazılmalıdır.
    this.search$
      .pipe(debounceTime(SCHOOL_SEARCH_DEBOUNCE_MS), takeUntilDestroyed(this.destroyRef))
      .subscribe((q) => {
        if (q.trim() === this.urlFilter().q.trim()) return;
        this.navigateFilter({ ...this.filter(), q });
      });
  }

  ngOnInit(): void {
    // Tek yönlü akış: URL → filtre. İlk emisyon derin linkteki filtreyi geri yükler.
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const next = schoolListFilterFromParams(params);
      this.urlFilter.set(next);
      if (next.q !== this.lastWrittenQ) this.searchText.set(next.q);
      this.reconcileFilter();
    });
    this.loadSchools();
    this.loadProvinces();
  }

  loadSchools(): void {
    this.schoolsLoading.set(true);
    this.schoolsError.set(null);
    this.admin.getSchools().subscribe({
      next: (list) => {
        this.schools.set(list);
        this.schoolsLoading.set(false);
      },
      error: () => {
        this.schoolsError.set(this.text('messages.schoolsLoadFailed'));
        this.schoolsLoading.set(false);
      },
    });
  }

  loadProvinces(): void {
    this.provincesLoading.set(true);
    this.provincesError.set(null);
    this.admin.getProvinces().subscribe({
      next: (list) => {
        this.provinces.set(list);
        this.provincesLoading.set(false);
        this.reconcileFilter();
      },
      error: () => {
        this.provincesError.set(this.text('messages.provincesLoadFailed'));
        this.provincesLoading.set(false);
      },
    });
  }

  /** İlçeleri önbellekte yoksa yükler. Aynı il için tekrar istek atılmaz. */
  private ensureDistricts(provinceId: number | null): void {
    if (provinceId == null || provinceId in this.districts()) return;
    this.districtsLoading.set(provinceId);
    this.admin.getDistricts(provinceId).subscribe({
      next: (list) => {
        this.districts.update((d) => ({ ...d, [provinceId]: list }));
        if (this.districtsLoading() === provinceId) this.districtsLoading.set(null);
        this.reconcileFilter();
      },
      error: () => {
        if (this.districtsLoading() === provinceId) this.districtsLoading.set(null);
        this.snack.open(this.text('messages.districtsLoadFailed'), this.close, { duration: 4000 });
      },
    });
  }

  private districtsOf(provinceId: number | null): DistrictDto[] {
    return provinceId == null ? [] : this.districts()[provinceId] ?? [];
  }

  /** Yeni okul formunda il değişti: ilçe seçimi sıfırlanır, yeni ilin ilçeleri yüklenir. */
  onNewSchoolProvinceChange(provinceId: number | null): void {
    this.newSchoolProvinceId.set(provinceId);
    this.newSchoolDistrictId.set(null);
    this.ensureDistricts(provinceId);
  }

  /** Düzenleme satırında il değişti: ilçe seçimi sıfırlanır, yeni ilin ilçeleri yüklenir. */
  onEditProvinceChange(provinceId: number | null): void {
    this.editProvinceId.set(provinceId);
    this.editDistrictId.set(null);
    this.ensureDistricts(provinceId);
  }

  // ---- liste filtresi (Issue #281) ----

  onSearchInput(value: string): void {
    this.searchText.set(value);
    this.search$.next(value);
  }

  /** Filtrede il değişti: ilçe sıfırlanır; ilçeler URL aboneliğinde yüklenir. */
  onFilterProvinceChange(provinceId: number | null): void {
    this.navigateFilter({ ...this.filter(), q: this.searchText(), provinceId, districtId: null });
  }

  onFilterDistrictChange(districtId: number | null): void {
    this.navigateFilter({ ...this.filter(), q: this.searchText(), districtId });
  }

  /**
   * URL'deki il/ilçe id'lerini yüklü listelerle uzlaştırır. İller yüklenmeden hiçbir şey yapmaz (ham id
   * ile süzülür). Yüklendikten sonra doğrulanmış ilin ilçelerini yükler; doğrulanmış filtre ham filtreden
   * farklıysa (bilinmeyen il / o ile ait olmayan ilçe) URL `replaceUrl` ile düzeltilir.
   */
  private reconcileFilter(): void {
    if (this.provinces().length === 0) return;
    const raw = this.urlFilter();
    const clean = this.filter();
    this.ensureDistricts(clean.provinceId);
    if (clean.provinceId !== raw.provinceId || clean.districtId !== raw.districtId) {
      this.navigateFilter({ ...clean, q: raw.q });
    }
  }

  /** Tüm kriterleri tek seferde sıfırlar; bekleyen debounce'lu arama '' ile ezilir (URL'le aynı → yazılmaz). */
  clearFilters(): void {
    this.searchText.set('');
    this.search$.next('');
    this.navigateFilter(EMPTY_SCHOOL_LIST_FILTER);
  }

  /** `merge` ile sayfaya ait olmayan param'lar korunur; geçmiş kirlenmesin diye `replaceUrl`. */
  private navigateFilter(filter: SchoolListFilter): void {
    const params = schoolListFilterToParams(filter);
    this.lastWrittenQ = (params[SCHOOL_LIST_QUERY_KEYS.q] as string | null) ?? '';
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: params,
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  /** Liste satırı için "İl / İlçe" metni; ikisi de yoksa boş string. */
  schoolLocation(sc: School): string {
    return [sc.provinceName, sc.districtName].filter((x): x is string => !!x).join(' / ');
  }

  // ---- create ----

  async addSchool(): Promise<void> {
    const name = this.newSchoolName.trim();
    if (!name) return;
    const addressLine = this.newSchoolAddressLine.trim();
    const ok = await this.run(() =>
      firstValueFrom(
        this.admin.createSchool({
          name,
          provinceId: this.newSchoolProvinceId(),
          districtId: this.newSchoolDistrictId(),
          addressLine: addressLine || null,
        })
      )
    );
    // Hatalı girişte (örn. 400 "Geçersiz ilçe") form korunur; kullanıcı düzeltip tekrar dener.
    if (!ok) return;
    this.newSchoolName = '';
    this.newSchoolAddressLine = '';
    this.newSchoolProvinceId.set(null);
    this.newSchoolDistrictId.set(null);
  }

  // ---- edit ----

  startEdit(sc: School): void {
    this.editingId.set(sc.id);
    this.editName = sc.name;
    this.editProvinceId.set(sc.provinceId ?? null);
    this.editDistrictId.set(sc.districtId ?? null);
    this.editAddressLine = sc.addressLine ?? '';
    this.ensureDistricts(sc.provinceId ?? null);
  }

  cancelEdit(): void {
    this.editingId.set(null);
  }

  isEditing(id: number): boolean {
    return this.editingId() === id;
  }

  async saveEdit(sc: School): Promise<void> {
    const name = this.editName.trim();
    if (!name) return;
    const addressLine = this.editAddressLine.trim();
    const ok = await this.run(() =>
      firstValueFrom(
        this.admin.updateSchool(sc.id, {
          name,
          provinceId: this.editProvinceId(),
          districtId: this.editDistrictId(),
          addressLine: addressLine || null,
        })
      )
    );
    // Backend doğrulama hatasında düzenleme satırı açık kalsın.
    if (!ok) return;
    this.cancelEdit();
  }

  // ---- delete ----

  async remove(sc: School): Promise<void> {
    const data: ConfirmDialogData = {
      title: this.text('deleteDialog.title'),
      message: this.text('deleteDialog.message', { name: sc.name }),
      confirmText: this.text('deleteDialog.confirm'),
      icon: 'delete',
      confirmColor: 'warn',
    };
    const ok = await firstValueFrom(this.dialog.open(ConfirmDialogComponent, { data }).afterClosed());
    if (!ok) return;
    await this.run(() => firstValueFrom(this.admin.deleteSchool(sc.id)));
  }

  /** @returns işlem başarılıysa true; 400 vb. doğrulama hatasında false (form korunur). */
  private async run(action: () => Promise<ApiResult>): Promise<boolean> {
    this.busy.set(true);
    try {
      const res = await action();
      this.snack.open(res.message, this.close, { duration: 3000 });
      if (res.success) this.loadSchools();
      return res.success;
    } catch (err: unknown) {
      const msg =
        (err as { error?: { message?: string } } | null)?.error?.message ?? this.text('messages.actionFailed');
      this.snack.open(msg, this.close, { duration: 4000 });
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  /** Scope'a göreli anahtarı senkron çözer; sözlük şablon render edilirken yüklenmiş olur. */
  private text(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${ADMIN_SCOPE}.schools.${key}`, params) ?? '';
  }

  /** Snackbar kapatma butonunun metni. */
  private get close(): string {
    return this.text('messages.close');
  }

  /**
   * Şablon dışı metinler (snackbar, dialog, hata mesajı) senkron `translate()` ile okunur;
   * sözlük şablon render edilmeden de hazır olsun diye scope burada yüklenir.
   */
  private preloadScope(): void {
    this.transloco
      .load(`${ADMIN_SCOPE}/${this.transloco.getActiveLang()}`)
      .pipe(take(1))
      .subscribe();
  }
}
