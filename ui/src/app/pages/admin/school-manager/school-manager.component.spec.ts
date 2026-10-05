import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, ParamMap, Params, Router, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, Subject, of, throwError } from 'rxjs';

import { SCHOOL_SEARCH_DEBOUNCE_MS, SchoolManagerComponent } from './school-manager.component';
import { AdminService } from '../../../services/admin.service';
import { ApiResult, ProvinceDto, School } from '../../../models/taxonomy';
import { MAX_SEARCH_LENGTH } from './school-list-filter';
import { ConfirmDialogData } from '../../../shared/components/confirm-dialog/confirm-dialog.component';
import { routes } from '../../../app.routes';
import { authGuard } from '../../../shared/guards/auth.guard';
import { adminGuard } from '../../../shared/guards/admin.guard';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import adminTr from '../../../../../public/i18n/admin/tr.json';

// Issue #150: okul testleri taxonomy-manager.component.spec.ts'ten taşındı.
describe('SchoolManagerComponent', () => {
  let fixture: ComponentFixture<SchoolManagerComponent>;
  let component: SchoolManagerComponent;
  let adminService: jasmine.SpyObj<AdminService>;
  let dialog: jasmine.SpyObj<MatDialog>;
  let snackBar: jasmine.SpyObj<MatSnackBar>;
  let router: Router;
  /** Router'ın query param akışını taklit eder: `navigate` çağrısı bu subject'e yeni param'ları iter. */
  let queryParams$: BehaviorSubject<ParamMap>;

  const okResult: ApiResult = { success: true, message: 'İşlem başarılı' };

  const schools: School[] = [
    {
      id: 1,
      name: 'Atatürk İlkokulu',
      provinceId: 6,
      provinceName: 'Ankara',
      districtId: 601,
      districtName: 'Çankaya',
      addressLine: null,
    },
  ];

  function dialogRef(result: boolean): MatDialogRef<unknown, boolean> {
    return { afterClosed: () => of(result) } as MatDialogRef<unknown, boolean>;
  }

  function paramsOf(map: ParamMap): Params {
    return Object.fromEntries(map.keys.map((k) => [k, map.get(k)]));
  }

  function configure(initialParams: Params = {}): ComponentFixture<SchoolManagerComponent> {
    queryParams$ = new BehaviorSubject<ParamMap>(convertToParamMap(initialParams));

    adminService = jasmine.createSpyObj<AdminService>('AdminService', [
      'getSchools',
      'getProvinces',
      'getDistricts',
      'createSchool',
      'updateSchool',
      'deleteSchool',
    ]);
    adminService.getSchools.and.returnValue(of(schools));
    adminService.getProvinces.and.returnValue(of([{ id: 6, name: 'Ankara' }, { id: 35, name: 'İzmir' }]));
    adminService.getDistricts.and.returnValue(of([{ id: 601, name: 'Çankaya', provinceId: 6 }]));

    dialog = jasmine.createSpyObj<MatDialog>('MatDialog', ['open']);
    dialog.open.and.returnValue(dialogRef(true));

    snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);

    TestBed.configureTestingModule({
      imports: [
        SchoolManagerComponent,
        translocoTestingModule({
          langs: { 'admin/tr': adminTr },
          // app.config.ts ile aynı: tireli scope önekleri camelCase'e çevrilmez.
          translocoConfig: { scopes: { keepCasing: true } },
        }),
      ],
      providers: [
        { provide: AdminService, useValue: adminService },
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar },
        provideNoopAnimations(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { queryParamMap: queryParams$.asObservable(), snapshot: {} } },
      ],
    });

    // Komponent MatDialogModule/MatSnackBarModule import ettiği için standalone injector gerçek
    // servisleri sağlar ve root-level mock'lar gölgelenir; mock'ları komponent seviyesinde ver.
    TestBed.overrideComponent(SchoolManagerComponent, {
      add: {
        providers: [
          { provide: MatDialog, useValue: dialog },
          { provide: MatSnackBar, useValue: snackBar },
        ],
      },
    });

    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.callFake((_commands, extras) => {
      const merged: Params = { ...paramsOf(queryParams$.value), ...(extras?.queryParams ?? {}) };
      const next = Object.fromEntries(
        Object.entries(merged)
          .filter(([, v]) => v !== null && v !== undefined)
          .map(([k, v]) => [k, String(v)]),
      );
      queryParams$.next(convertToParamMap(next));
      return Promise.resolve(true);
    });

    return TestBed.createComponent(SchoolManagerComponent);
  }

  function init(initialParams: Params = {}): void {
    fixture = configure(initialParams);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  // ── Route ────────────────────────────────────────────────────────────────

  it('routes_AdminSchoolsPath_IsGuardedByAuthAndAdminGuard', () => {
    const layoutRoute = routes.find((r) => Array.isArray(r.children));
    const route = layoutRoute?.children?.find((r) => r.path === 'admin/schools');
    const adminRoute = layoutRoute?.children?.find((r) => r.path === 'admin');

    expect(route).withContext('route tanımı bulunamadı').toBeDefined();
    expect(route?.canActivate).toEqual([authGuard, adminGuard]);
    expect(route?.canActivate).toEqual(adminRoute?.canActivate);
  });

  it('routes_AdminSchoolsPath_LazyLoadsSchoolManagerComponent', async () => {
    const layoutRoute = routes.find((r) => Array.isArray(r.children));
    const route = layoutRoute?.children?.find((r) => r.path === 'admin/schools');

    expect(route?.loadComponent).toBeDefined();
    expect(await route!.loadComponent!()).toBe(SchoolManagerComponent);
  });

  // ── İlk yükleme / liste ───────────────────────────────────────────────────

  it('init_LoadsSchoolsAndProvinces', () => {
    init();

    expect(adminService.getSchools).toHaveBeenCalledTimes(1);
    expect(adminService.getProvinces).toHaveBeenCalledTimes(1);
    expect(component.schools()).toEqual(schools);
  });

  it('schoolRow_WithProvinceAndDistrict_ShowsCombinedLocation', () => {
    init();

    const meta: HTMLElement = fixture.nativeElement.querySelector('.schools-list .meta');
    expect(meta.textContent?.trim()).toBe('Ankara / Çankaya');
  });

  it('schools_EmptyList_ShowsEmptyState', () => {
    fixture = configure();
    adminService.getSchools.and.returnValue(of([]));
    fixture.detectChanges();

    const empty: HTMLElement = fixture.nativeElement.querySelector('.schools-section .empty-state');
    expect(empty).toBeTruthy();
    expect(empty.textContent).toContain('Henüz okul eklenmemiş');
  });

  // ── Ekle ──────────────────────────────────────────────────────────────────

  it('addSchool_WithProvinceDistrictAddress_CallsCreateSchoolWithTrimmedFields', async () => {
    init();

    adminService.createSchool.and.returnValue(of(okResult));
    component.newSchoolName = '  Cumhuriyet Ortaokulu ';
    component.onNewSchoolProvinceChange(6);
    component.newSchoolDistrictId.set(601);
    component.newSchoolAddressLine = ' Atatürk Bulvarı No:1 ';

    await component.addSchool();

    expect(adminService.getDistricts).toHaveBeenCalledOnceWith(6);
    expect(adminService.createSchool).toHaveBeenCalledOnceWith({
      name: 'Cumhuriyet Ortaokulu',
      provinceId: 6,
      districtId: 601,
      addressLine: 'Atatürk Bulvarı No:1',
    });
    expect(component.newSchoolName).toBe('');
    expect(component.newSchoolProvinceId()).toBeNull();
    expect(component.newSchoolDistrictId()).toBeNull();
    expect(component.newSchoolAddressLine).toBe('');
    // Başarılı işlem sonrası liste yenilenir.
    expect(adminService.getSchools).toHaveBeenCalledTimes(2);
  });

  it('addSchool_NoLocation_SendsNulls', async () => {
    init();

    adminService.createSchool.and.returnValue(of(okResult));
    component.newSchoolName = 'Yeni Okul';

    await component.addSchool();

    expect(adminService.createSchool).toHaveBeenCalledOnceWith({
      name: 'Yeni Okul',
      provinceId: null,
      districtId: null,
      addressLine: null,
    });
  });

  it('addSchool_BlankName_DoesNotCallCreateSchool', async () => {
    init();
    component.newSchoolName = '   ';

    await component.addSchool();

    expect(adminService.createSchool).not.toHaveBeenCalled();
  });

  it('onNewSchoolProvinceChange_ProvinceChanges_ResetsDistrictSelection', () => {
    init();

    component.onNewSchoolProvinceChange(6);
    component.newSchoolDistrictId.set(601);
    component.onNewSchoolProvinceChange(35);

    expect(component.newSchoolDistrictId()).toBeNull();
    expect(adminService.getDistricts).toHaveBeenCalledWith(35);
  });

  it('addSchool_BackendRejects_KeepsFormValues', async () => {
    init();

    adminService.createSchool.and.returnValue(
      throwError(() => ({ error: { message: 'İlçe seçildiğinde il de seçilmelidir.' } }))
    );
    component.newSchoolName = 'Yeni Okul';
    component.newSchoolAddressLine = 'Adres';

    await component.addSchool();

    expect(component.newSchoolName).toBe('Yeni Okul');
    expect(component.newSchoolAddressLine).toBe('Adres');
    expect(snackBar.open).toHaveBeenCalledWith(
      'İlçe seçildiğinde il de seçilmelidir.',
      'Kapat',
      jasmine.anything()
    );
  });

  // ── Düzenle ───────────────────────────────────────────────────────────────

  it('saveEdit_CallsUpdateSchoolWithLocationFields', async () => {
    init();
    adminService.updateSchool.and.returnValue(of(okResult));

    component.startEdit(schools[0]);
    expect(component.editProvinceId()).toBe(6);
    expect(component.editDistrictId()).toBe(601);
    expect(adminService.getDistricts).toHaveBeenCalledOnceWith(6);

    component.editName = 'Atatürk İlkokulu 2';
    component.editAddressLine = ' Yeni adres ';

    await component.saveEdit(schools[0]);

    expect(adminService.updateSchool).toHaveBeenCalledOnceWith(1, {
      name: 'Atatürk İlkokulu 2',
      provinceId: 6,
      districtId: 601,
      addressLine: 'Yeni adres',
    });
    expect(component.editingId()).toBeNull();
  });

  it('saveEdit_BackendRejects_KeepsEditRowOpen', async () => {
    init();
    adminService.updateSchool.and.returnValue(throwError(() => ({ error: { message: 'Geçersiz ilçe' } })));

    component.startEdit(schools[0]);
    await component.saveEdit(schools[0]);

    expect(component.isEditing(1)).toBeTrue();
  });

  it('saveEdit_BlankName_DoesNotCallUpdateSchool', async () => {
    init();

    component.startEdit(schools[0]);
    component.editName = '   ';
    await component.saveEdit(schools[0]);

    expect(adminService.updateSchool).not.toHaveBeenCalled();
    expect(component.isEditing(1)).toBeTrue();
  });

  it('startEdit_RendersInlineEditRowForThatSchool', () => {
    init();

    component.startEdit(schools[0]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.schools-list .edit-row')).toBeTruthy();
  });

  // ── Sil ───────────────────────────────────────────────────────────────────

  it('remove_ConfirmDialogAccepted_CallsDeleteSchool', async () => {
    init();
    adminService.deleteSchool.and.returnValue(of(okResult));

    await component.remove(schools[0]);

    const data = dialog.open.calls.mostRecent().args[1]?.data as ConfirmDialogData;
    expect(data.title).toBe('okul sil');
    expect(data.message).toBe('"Atatürk İlkokulu" okulunu silmek istediğine emin misin?');
    expect(data.confirmText).toBe('Sil');
    expect(adminService.deleteSchool).toHaveBeenCalledOnceWith(1);
  });

  it('remove_ConfirmDialogRejected_DoesNotCallDeleteSchool', async () => {
    init();
    dialog.open.and.returnValue(dialogRef(false));

    await component.remove(schools[0]);

    expect(adminService.deleteSchool).not.toHaveBeenCalled();
  });

  // ── schoolLocation ────────────────────────────────────────────────────────

  it('schoolLocation_ProvinceAndDistrictPresent_JoinsBothWithSlash', () => {
    init();

    expect(component.schoolLocation(schools[0])).toBe('Ankara / Çankaya');
  });

  it('schoolLocation_OnlyProvincePresent_ReturnsProvinceNameOnly', () => {
    init();

    const school: School = { id: 2, name: 'İl Olan Okul', provinceId: 6, provinceName: 'Ankara', districtId: null, districtName: null, addressLine: null };

    expect(component.schoolLocation(school)).toBe('Ankara');
  });

  it('schoolLocation_NoProvinceOrDistrict_ReturnsEmptyString', () => {
    init();

    const school: School = { id: 3, name: 'Adressiz Okul', provinceId: null, provinceName: null, districtId: null, districtName: null, addressLine: null };

    expect(component.schoolLocation(school)).toBe('');
  });

  // ── Hata durumu ───────────────────────────────────────────────────────────

  it('loadSchools_RequestFails_ShowsSchoolsErrorBanner', () => {
    fixture = configure();
    component = fixture.componentInstance;
    adminService.getSchools.and.returnValue(throwError(() => new Error('boom')));

    fixture.detectChanges();

    expect(component.schoolsError()).toBe('Okullar yüklenemedi');
    const errorBox: HTMLElement = fixture.nativeElement.querySelector('.schools-section .state-box--error');
    expect(errorBox).toBeTruthy();
    expect(errorBox.querySelector('button')).toBeTruthy();
  });

  it('schoolsRetryButtonClick_AfterLoadSchoolsError_CallsLoadSchoolsAgain', () => {
    fixture = configure();
    component = fixture.componentInstance;
    adminService.getSchools.and.returnValue(throwError(() => new Error('boom')));
    fixture.detectChanges();
    expect(adminService.getSchools).toHaveBeenCalledTimes(1);

    adminService.getSchools.and.returnValue(of(schools));
    const retry: HTMLButtonElement = fixture.nativeElement.querySelector('.schools-section .state-box--error button');
    retry.click();
    fixture.detectChanges();

    expect(adminService.getSchools).toHaveBeenCalledTimes(2);
    expect(component.schoolsError()).toBeNull();
  });

  it('ensureDistricts_RequestFails_ShowsDistrictsSnackbar', () => {
    init();
    adminService.getDistricts.and.returnValue(throwError(() => new Error('boom')));

    component.onNewSchoolProvinceChange(35);

    expect(snackBar.open).toHaveBeenCalledWith('İlçeler yüklenemedi', 'Kapat', jasmine.anything());
    expect(component.newSchoolDistrictsLoading()).toBeFalse();
  });

  it('loadProvinces_RequestFails_ShowsLocationErrorWithRetry', () => {
    fixture = configure();
    component = fixture.componentInstance;
    adminService.getProvinces.and.returnValue(throwError(() => new Error('boom')));

    fixture.detectChanges();

    expect(component.provincesError()).toBe('İller yüklenemedi');
    const box: HTMLElement = fixture.nativeElement.querySelector('.location-error');
    expect(box).toBeTruthy();
    expect(box.querySelector('button')).toBeTruthy();
  });

  // ── Sayfa başlığı (doğrudan route vs admin-home sekmesi) ──────────────────

  it('render_StandaloneRoute_ShowsPageHeaderWithPadding', () => {
    init();

    const root: HTMLElement = fixture.nativeElement.querySelector('.schools');
    expect(root.classList).toContain('al');
    const h1: HTMLElement = fixture.nativeElement.querySelector('.al__head h1');
    expect(h1.textContent?.trim()).toBe('Okul Yönetimi');
    expect(fixture.nativeElement.querySelector('.al__head p').textContent).toContain('okulları');
  });

  it('render_EmbeddedInAdminHome_HidesPageHeader', () => {
    fixture = configure();
    fixture.componentRef.setInput('embedded', true);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.al__head')).toBeNull();
    expect(fixture.nativeElement.querySelector('h1')).toBeNull();
    expect(fixture.nativeElement.querySelector('.schools').classList).not.toContain('al');
    // Liste kartı gömülüyken de görünür.
    expect(fixture.nativeElement.querySelector('.schools-section')).toBeTruthy();
  });

  // ── Liste filtresi (Issue #281) ───────────────────────────────────────────

  describe('list filter', () => {
    function school(id: number, name: string, provinceId: number | null, districtId: number | null): School {
      return {
        id,
        name,
        provinceId,
        provinceName: provinceId === 6 ? 'Ankara' : provinceId === 34 ? 'İstanbul' : null,
        districtId,
        districtName: null,
        addressLine: null,
      };
    }

    const many: School[] = [
      school(1, 'Atatürk İlkokulu', 6, 601),
      school(2, 'Keçiören Anadolu Lisesi', 6, 602),
      school(3, 'İstanbul Erkek Lisesi', 34, 3401),
      school(4, 'CUMHURIYET ILKOKULU', 34, 3401),
      school(5, 'Konumsuz Okul', null, null),
    ];

    function setup(initialParams: Params = {}, list: School[] = many): void {
      fixture = configure(initialParams);
      component = fixture.componentInstance;
      adminService.getSchools.and.returnValue(of(list));
      adminService.getProvinces.and.returnValue(
        of([
          { id: 6, name: 'Ankara' },
          { id: 34, name: 'İstanbul' },
        ]),
      );
      adminService.getDistricts.and.callFake((provinceId: number) =>
        of(
          provinceId === 6
            ? [
                { id: 601, name: 'Çankaya', provinceId: 6 },
                { id: 602, name: 'Keçiören', provinceId: 6 },
              ]
            : [{ id: 3401, name: 'Kadıköy', provinceId: 34 }],
        ),
      );
      fixture.detectChanges();
    }

    const ids = (): number[] => component.filteredSchools().map((s) => s.id);
    const el = <T extends Element = HTMLElement>(sel: string): T | null => fixture.nativeElement.querySelector(sel);
    const url = (): Params => paramsOf(queryParams$.value);

    it('noFilter_ShowsAllSchoolsAndTotalCount', () => {
      setup();

      expect(ids()).toEqual([1, 2, 3, 4, 5]);
      expect(component.isFiltered()).toBeFalse();
      expect(el('.column-header .count')?.textContent?.trim()).toBe('5');
      expect(fixture.nativeElement.querySelectorAll('.schools-list .item').length).toBe(5);
    });

    it('search_LowercaseQuery_MatchesDottedCapitalI', fakeAsync(() => {
      setup();

      component.onSearchInput('istanbul');
      tick(SCHOOL_SEARCH_DEBOUNCE_MS);

      expect(ids()).toEqual([3]);
      expect(url()).toEqual({ q: 'istanbul' });
    }));

    it('search_IlkokulVariants_MatchAcrossCaseAndTurkishI', fakeAsync(() => {
      setup();

      for (const q of ['ilkokul', 'İlkokul', 'ILKOKUL', 'İLKOKULU']) {
        component.onSearchInput(q);
        tick(SCHOOL_SEARCH_DEBOUNCE_MS);
        expect(ids()).withContext(q).toEqual([1, 4]);
      }
    }));

    it('search_Typing_IsDebouncedBeforeUrlUpdate', fakeAsync(() => {
      setup();
      const input = el<HTMLInputElement>('.filter-search-input')!;

      input.value = 'lise';
      input.dispatchEvent(new Event('input'));
      tick(SCHOOL_SEARCH_DEBOUNCE_MS - 1);

      expect(router.navigate).not.toHaveBeenCalled();
      expect(ids()).toEqual([1, 2, 3, 4, 5]);

      tick(1);
      fixture.detectChanges();

      expect(router.navigate).toHaveBeenCalledTimes(1);
      expect(ids()).toEqual([2, 3]);
      expect(el('.column-header .count')?.textContent?.trim()).toBe('2 / 5');
    }));

    it('search_UrlEchoOfOlderQuery_DoesNotOverwriteNewerTypedText', fakeAsync(() => {
      setup();

      component.onSearchInput('lis');
      tick(SCHOOL_SEARCH_DEBOUNCE_MS);
      component.onSearchInput('lise');
      // Router'ın eski değeri geç yansıtması: kutudaki daha yeni metin ezilmemeli.
      queryParams$.next(convertToParamMap({ q: 'lis' }));

      expect(component.searchText()).toBe('lise');
      tick(SCHOOL_SEARCH_DEBOUNCE_MS);
      expect(url()).toEqual({ q: 'lise' });
    }));

    it('province_Selected_ListsOnlyThatProvinceAndLoadsItsDistricts', () => {
      setup();

      component.onFilterProvinceChange(6);
      fixture.detectChanges();

      expect(url()).toEqual({ provinceId: '6' });
      expect(ids()).toEqual([1, 2]);
      expect(adminService.getDistricts).toHaveBeenCalledWith(6);
      expect(component.filterDistricts().map((d) => d.id)).toEqual([601, 602]);
    });

    it('districtSelect_NoProvince_IsDisabled_ProvinceSelected_IsEnabled', async () => {
      setup();
      const district = (): HTMLElement => el('mat-select.filter-district')!;
      // ngModel `disabled`'ı bir sonraki mikro görevde uygular.
      await fixture.whenStable();
      fixture.detectChanges();

      expect(district().classList).toContain('mat-mdc-select-disabled');

      component.onFilterProvinceChange(34);
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(district().classList).not.toContain('mat-mdc-select-disabled');
    });

    it('district_Selected_ListsOnlyThatDistrict', () => {
      setup({ provinceId: '6' });

      component.onFilterDistrictChange(602);

      expect(url()).toEqual({ provinceId: '6', districtId: '602' });
      expect(ids()).toEqual([2]);
    });

    it('province_Changed_ResetsDistrict', () => {
      setup({ provinceId: '6', districtId: '601' });

      component.onFilterProvinceChange(34);

      expect(url()).toEqual({ provinceId: '34' });
      expect(component.filter().districtId).toBeNull();
      expect(ids()).toEqual([3, 4]);
    });

    it('filters_Combined_AreAppliedWithAnd', () => {
      setup({ q: 'ilkokul', provinceId: '34' });

      expect(ids()).toEqual([4]);

      queryParams$.next(convertToParamMap({ q: 'lise', provinceId: '6', districtId: '601' }));
      expect(ids()).toEqual([]);
    });

    it('clearFilters_ResetsAllCriteriaAndUrl', () => {
      setup({ q: 'lise', provinceId: '6', districtId: '602', other: 'keep' });
      expect(component.searchText()).toBe('lise');

      component.clearFilters();
      fixture.detectChanges();

      expect(url()).toEqual({ other: 'keep' });
      expect(component.searchText()).toBe('');
      expect(component.isFiltered()).toBeFalse();
      expect(ids()).toEqual([1, 2, 3, 4, 5]);
      expect(el<HTMLButtonElement>('button.filter-clear')?.disabled).toBeTrue();
    });

    it('clearFilters_PendingDebouncedSearch_IsCancelled', fakeAsync(() => {
      setup();

      component.onSearchInput('lise');
      component.clearFilters();
      tick(SCHOOL_SEARCH_DEBOUNCE_MS);

      expect(url()).toEqual({});
      expect(ids()).toEqual([1, 2, 3, 4, 5]);
    }));

    it('url_ValidParams_RestoreFilterOnLoad', () => {
      setup({ q: 'Lise', provinceId: '6', districtId: '602' });

      expect(component.searchText()).toBe('Lise');
      expect(component.filter()).toEqual({ q: 'Lise', provinceId: 6, districtId: 602 });
      expect(ids()).toEqual([2]);
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('url_MalformedIds_AreIgnored', () => {
      setup({ provinceId: 'abc', districtId: '601' });

      expect(component.filter()).toEqual({ q: '', provinceId: null, districtId: null });
      expect(ids()).toEqual([1, 2, 3, 4, 5]);
    });

    it('url_UnknownProvince_IsIgnoredAndUrlCorrectedAfterProvincesLoad', () => {
      setup({ q: 'lise', provinceId: '9999', districtId: '1' });

      expect(component.filter()).toEqual({ q: 'lise', provinceId: null, districtId: null });
      expect(ids()).toEqual([2, 3]);
      expect(url()).toEqual({ q: 'lise' });
      expect(router.navigate).toHaveBeenCalledWith(
        [],
        jasmine.objectContaining({ replaceUrl: true, queryParamsHandling: 'merge' }),
      );
      // İlçeler yalnız doğrulanmış il için istenir; bilinmeyen il için istek yok.
      expect(adminService.getDistricts).not.toHaveBeenCalledWith(9999);
    });

    it('url_DistrictNotInProvince_IsIgnoredAndUrlCorrected', () => {
      setup({ provinceId: '6', districtId: '3401' });

      expect(component.filter()).toEqual({ q: '', provinceId: 6, districtId: null });
      expect(ids()).toEqual([1, 2]);
      expect(url()).toEqual({ provinceId: '6' });
    });

    it('url_ProvincesStillLoading_ShowsPendingNotNoMatch_ThenCorrects', () => {
      const provinces$ = new Subject<ProvinceDto[]>();
      fixture = configure({ provinceId: '9999' });
      component = fixture.componentInstance;
      adminService.getSchools.and.returnValue(of(many));
      adminService.getProvinces.and.returnValue(provinces$);
      fixture.detectChanges();

      // Ham id ile süzülür, sonuç boş ama henüz doğrulanamadığı için "uyan yok" denmez.
      expect(component.filterPending()).toBeTrue();
      expect(el('.filter-pending')?.textContent).toContain('Filtre uygulanıyor');
      expect(el('.no-match')).toBeNull();
      expect(el('mat-progress-bar')).toBeTruthy();
      expect(adminService.getDistricts).not.toHaveBeenCalled();

      provinces$.next([{ id: 6, name: 'Ankara' }]);
      fixture.detectChanges();

      expect(component.filterPending()).toBeFalse();
      expect(url()).toEqual({});
      expect(el('.filter-pending')).toBeNull();
      expect(fixture.nativeElement.querySelectorAll('.schools-list .item').length).toBe(5);
    });

    it('url_ValidProvince_LoadsDistrictsOnlyAfterProvincesLoad', () => {
      const provinces$ = new Subject<ProvinceDto[]>();
      fixture = configure({ provinceId: '6' });
      component = fixture.componentInstance;
      adminService.getProvinces.and.returnValue(provinces$);
      fixture.detectChanges();

      expect(adminService.getDistricts).not.toHaveBeenCalled();

      provinces$.next([{ id: 6, name: 'Ankara' }]);

      expect(adminService.getDistricts).toHaveBeenCalledOnceWith(6);
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('search_ClearThenRetypeSameTextWithinDebounce_IsWrittenToUrl', fakeAsync(() => {
      setup();

      component.onSearchInput('lise');
      tick(SCHOOL_SEARCH_DEBOUNCE_MS);
      expect(url()).toEqual({ q: 'lise' });

      component.clearFilters();
      expect(url()).toEqual({});
      component.onSearchInput('lise');
      tick(SCHOOL_SEARCH_DEBOUNCE_MS);

      expect(url()).toEqual({ q: 'lise' });
      expect(ids()).toEqual([2, 3]);
    }));

    it('search_SameAsUrl_DoesNotNavigateAgain', fakeAsync(() => {
      setup({ q: 'lise' });

      component.onSearchInput('lise ');
      tick(SCHOOL_SEARCH_DEBOUNCE_MS);

      expect(router.navigate).not.toHaveBeenCalled();
    }));

    it('counter_VisibleNumberIsAriaHidden_TranslatedSentenceInStatusRegion', () => {
      setup({ q: 'lise' });

      expect(el('.column-header .count')?.getAttribute('aria-hidden')).toBe('true');
      const status = el('.column-header .count-status')!;
      expect(status.getAttribute('role')).toBe('status');
      expect(status.classList).toContain('cdk-visually-hidden');
      expect(status.textContent?.trim()).toBe('5 okuldan 2 tanesi filtreye uyuyor');
    });

    it('searchInput_MaxLength_IsBoundToSharedConstant', () => {
      setup();

      expect(el('.filter-search-input')?.getAttribute('maxlength')).toBe(String(MAX_SEARCH_LENGTH));
    });

    it('noMatch_ShowsFilterEmptyStateDistinctFromNoSchools', () => {
      setup({ q: 'yok böyle okul' });

      const noMatch = el('.schools-section .no-match');
      expect(noMatch?.textContent).toContain('Filtreye uyan okul yok');
      expect(fixture.nativeElement.textContent).not.toContain('Henüz okul eklenmemiş');
      expect(el('.schools-list')).toBeNull();
      expect(el('.column-header .count')?.textContent?.trim()).toBe('0 / 5');

      (noMatch!.querySelector('button') as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(url()).toEqual({});
      expect(el('.no-match')).toBeNull();
      expect(fixture.nativeElement.querySelectorAll('.schools-list .item').length).toBe(5);
    });

    it('noSchoolsAtAll_ShowsOriginalEmptyStateNotFilterEmptyState', () => {
      setup({ q: 'lise' }, []);

      expect(el('.schools-section .empty-state')?.textContent).toContain('Henüz okul eklenmemiş');
      expect(el('.no-match')).toBeNull();
    });

    it('addSchool_WithActiveFilter_KeepsFilterAndReappliesToReloadedList', async () => {
      setup({ q: 'lise', provinceId: '6' });
      adminService.createSchool.and.returnValue(of(okResult));
      adminService.getSchools.and.returnValue(of([...many, school(6, 'Çankaya Fen Lisesi', 6, 601)]));
      component.newSchoolName = 'Çankaya Fen Lisesi';

      await component.addSchool();

      expect(router.navigate).not.toHaveBeenCalled();
      expect(url()).toEqual({ q: 'lise', provinceId: '6' });
      expect(component.searchText()).toBe('lise');
      expect(ids()).toEqual([2, 6]);
    });

    it('saveEdit_WithActiveFilter_KeepsFilter', async () => {
      setup({ provinceId: '34' });
      adminService.updateSchool.and.returnValue(of(okResult));

      component.startEdit(many[2]);
      await component.saveEdit(many[2]);

      expect(router.navigate).not.toHaveBeenCalled();
      expect(component.filter().provinceId).toBe(34);
      expect(ids()).toEqual([3, 4]);
    });

    it('remove_WithActiveFilter_KeepsFilter', async () => {
      setup({ q: 'ilkokul' });
      adminService.deleteSchool.and.returnValue(of(okResult));
      adminService.getSchools.and.returnValue(of(many.filter((s) => s.id !== 1)));

      await component.remove(many[0]);

      expect(router.navigate).not.toHaveBeenCalled();
      expect(component.filter().q).toBe('ilkokul');
      expect(ids()).toEqual([4]);
    });

    it('controls_HaveAriaLabels', () => {
      setup();

      expect(el('.filter-row')?.getAttribute('role')).toBe('search');
      expect(el('.filter-search-input')?.getAttribute('aria-label')).toBe('Okul adına göre ara');
      expect(el('mat-select.filter-province')?.getAttribute('aria-label')).toBe('İle göre filtrele');
      expect(el('mat-select.filter-district')?.getAttribute('aria-label')).toBe('İlçeye göre filtrele');
      expect(el('button.filter-clear')?.textContent).toContain('Filtreleri temizle');
    });
  });
});
