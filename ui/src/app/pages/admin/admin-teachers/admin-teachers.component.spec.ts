import { EnvironmentProviders, Provider } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, ParamMap, Params, Router, convertToParamMap, provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { MatPaginator } from '@angular/material/paginator';
import { BehaviorSubject, Subject, of, throwError } from 'rxjs';

import { AdminTeachersComponent } from './admin-teachers.component';
import { AdminService } from '../../../services/admin.service';
import { AdminTeacherListItem } from '../../../models/admin-teacher.model';
import { Paged } from '../../../models/test-instance';
import { School } from '../../../models/taxonomy';
import { SchoolFilterComponent } from '../../../shared/components/school-filter/school-filter.component';
import { routes } from '../../../app.routes';
import { authGuard } from '../../../shared/guards/auth.guard';
import { adminGuard } from '../../../shared/guards/admin.guard';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import adminTr from '../../../../../public/i18n/admin/tr.json';

describe('AdminTeachersComponent', () => {
  let fixture: ComponentFixture<AdminTeachersComponent>;
  let component: AdminTeachersComponent;
  let adminService: jasmine.SpyObj<AdminService>;
  let router: Router;

  const schools: School[] = [
    { id: 5, name: 'Ankara Lisesi', provinceId: null, provinceName: null, districtId: null, districtName: null, addressLine: null },
  ];

  function teacher(overrides: Partial<AdminTeacherListItem> = {}): AdminTeacherListItem {
    return {
      id: 12,
      userId: 1042,
      fullName: 'Ayşe Yılmaz',
      email: 'ayse@okul.k12.tr',
      schoolId: 5,
      schoolName: 'Ankara Lisesi',
      isIndependentTutor: false,
      approvalStatus: 'Approved',
      isEnabled: true,
      ...overrides,
    };
  }

  function paged(items: AdminTeacherListItem[], totalCount = items.length, pageNumber = 1): Paged<AdminTeacherListItem> {
    return { pageNumber, pageSize: 20, totalCount, items };
  }

  /** Router'ın query param akışını taklit eder: `navigate` çağrısı bu subject'e yeni param'ları iter. */
  let queryParams$: BehaviorSubject<ParamMap>;

  function paramsOf(map: ParamMap): Params {
    return map.keys.reduce<Params>((acc, key) => ({ ...acc, [key]: map.get(key) }), {});
  }

  /** TestBed'i kurar; komponent `create()` ile oluşturulur (constructor URL aboneliğiyle hemen istek atar). */
  function configure(initialParams: Params = {}): void {
    adminService = jasmine.createSpyObj<AdminService>('AdminService', ['getTeachers', 'getSchools']);
    adminService.getSchools.and.returnValue(of(schools));
    adminService.getTeachers.and.returnValue(of(paged([teacher()], 45)));
    queryParams$ = new BehaviorSubject<ParamMap>(convertToParamMap(initialParams));

    const providers: (Provider | EnvironmentProviders)[] = [
      { provide: AdminService, useValue: adminService },
      provideRouter([]),
      provideNoopAnimations(),
      { provide: ActivatedRoute, useValue: { queryParamMap: queryParams$.asObservable(), snapshot: {} } },
    ];

    TestBed.configureTestingModule({
      imports: [
        AdminTeachersComponent,
        translocoTestingModule({
          langs: { 'admin/tr': adminTr },
          translocoConfig: { scopes: { keepCasing: true } },
        }),
      ],
      providers,
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
  }

  function create(): void {
    fixture = TestBed.createComponent(AdminTeachersComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function cellTexts(column: string): string[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll(`td.mat-column-${column}`)).map(
      (td) => (td.textContent ?? '').trim(),
    );
  }

  // ── Route ────────────────────────────────────────────────────────────────

  it('routes_AdminTeachersPath_IsGuardedByAuthAndAdminGuard', () => {
    const layoutRoute = routes.find((r) => Array.isArray(r.children));
    const route = layoutRoute?.children?.find((r) => r.path === 'admin/teachers');

    expect(route).withContext('route tanımı bulunamadı').toBeDefined();
    expect(route?.canActivate).toEqual([authGuard, adminGuard]);
  });

  // ── İlk yükleme ───────────────────────────────────────────────────────────

  it('init_NoQueryParams_RequestsFirstPageWithDefaultPageSizeAndNoFilter', () => {
    configure();
    create();

    expect(adminService.getTeachers).toHaveBeenCalledOnceWith({
      page: 1,
      pageSize: 20,
      schoolId: null,
      unassigned: false,
    });
  });

  it('init_WhileRequestPending_ShowsSpinner', () => {
    configure();
    adminService.getTeachers.and.returnValue(new Subject<Paged<AdminTeacherListItem>>());
    create();

    expect(fixture.nativeElement.querySelector('mat-spinner')).not.toBeNull();
    expect(text()).toContain(adminTr.teachers.loading);
  });

  it('init_DeepLinkQueryParams_RestoresFilterAndPage', () => {
    configure({ schoolId: '5', page: '3', pageSize: '50' });
    create();

    expect(adminService.getTeachers).toHaveBeenCalledOnceWith({
      page: 3,
      pageSize: 50,
      schoolId: 5,
      unassigned: false,
    });
    expect(component.filter()).toBe(5);
  });

  it('init_UnassignedQueryParam_SendsUnassignedTrue', () => {
    configure({ unassigned: 'true' });
    create();

    expect(adminService.getTeachers).toHaveBeenCalledOnceWith({
      page: 1,
      pageSize: 20,
      schoolId: null,
      unassigned: true,
    });
  });

  // ── Kolonlar ──────────────────────────────────────────────────────────────

  it('render_Rows_ShowsAllFiveColumns', () => {
    configure();
    create();

    const headers = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('th')).map((th) =>
      (th.textContent ?? '').trim(),
    );
    expect(headers).toEqual([
      adminTr.teachers.columns.fullName,
      adminTr.teachers.columns.email,
      adminTr.teachers.columns.school,
      adminTr.teachers.columns.approvalStatus,
      adminTr.teachers.columns.accountStatus,
    ]);
    expect(cellTexts('fullName')).toEqual(['Ayşe Yılmaz']);
    expect(cellTexts('email')).toEqual(['ayse@okul.k12.tr']);
    expect(cellTexts('school')).toEqual(['Ankara Lisesi']);
    expect(cellTexts('approvalStatus')).toEqual(['Onaylı']);
    expect(cellTexts('accountStatus')).toEqual(['Aktif']);
  });

  it('render_StatusVariantsAndMissingFields_ShowsTurkishLabelsAndFallbacks', () => {
    configure();
    adminService.getTeachers.and.returnValue(
      of(
        paged([
          teacher({ id: 1, approvalStatus: 'Pending', isEnabled: null, fullName: '', email: '' }),
          teacher({ id: 2, approvalStatus: 'Rejected', isEnabled: false, schoolId: null, schoolName: null, isIndependentTutor: true }),
          teacher({ id: 3, schoolId: null, schoolName: null, isIndependentTutor: false }),
        ]),
      ),
    );
    create();

    expect(cellTexts('approvalStatus')).toEqual(['Beklemede', 'Reddedildi', 'Onaylı']);
    expect(cellTexts('accountStatus')).toEqual(['Bilinmiyor', 'Pasif', 'Aktif']);
    expect(cellTexts('fullName')[0]).toBe('—');
    expect(cellTexts('email')[0]).toBe('—');
    expect(cellTexts('school')).toEqual(['Ankara Lisesi', 'Bağımsız', '—']);
  });

  // ── Boş / hata ───────────────────────────────────────────────────────────

  it('render_EmptyPage_ShowsEmptyStateAndNoPaginator', () => {
    configure();
    adminService.getTeachers.and.returnValue(of(paged([], 0)));
    create();

    expect(text()).toContain('Öğretmen bulunamadı');
    expect(fixture.nativeElement.querySelector('table')).toBeNull();
    expect(fixture.nativeElement.querySelector('mat-paginator')).toBeNull();
  });

  it('render_RequestFails_ShowsErrorWithRetry', () => {
    configure();
    adminService.getTeachers.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    create();

    const alert = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement;
    expect(alert).not.toBeNull();
    expect(alert.textContent).toContain(adminTr.teachers.loadFailed);

    adminService.getTeachers.and.returnValue(of(paged([teacher()])));
    (alert.querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(adminService.getTeachers).toHaveBeenCalledTimes(2);
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
    expect(cellTexts('fullName')).toEqual(['Ayşe Yılmaz']);
  });

  // ── Filtre ───────────────────────────────────────────────────────────────

  it('filterChange_FromSchoolFilter_RequestsFirstPageWithSchoolId', () => {
    configure({ page: '2' });
    create();
    adminService.getTeachers.calls.reset();

    const filter = fixture.debugElement.query(By.directive(SchoolFilterComponent))
      .componentInstance as SchoolFilterComponent;
    filter.select(5);
    fixture.detectChanges();

    expect(adminService.getTeachers).toHaveBeenCalledOnceWith({
      page: 1,
      pageSize: 20,
      schoolId: 5,
      unassigned: false,
    });
    expect(component.pageIndex()).toBe(0);
    expect(router.navigate).toHaveBeenCalledWith(
      [],
      jasmine.objectContaining({
        queryParams: { schoolId: 5, unassigned: null, page: null, pageSize: null },
      }),
    );
  });

  it('filterChange_Unassigned_RequestsUnassignedWithoutSchoolId', () => {
    configure();
    create();
    adminService.getTeachers.calls.reset();

    component.onFilterChange('unassigned');

    expect(adminService.getTeachers).toHaveBeenCalledOnceWith({
      page: 1,
      pageSize: 20,
      schoolId: null,
      unassigned: true,
    });
  });

  // ── Sayfalama ────────────────────────────────────────────────────────────

  it('paginator_NextPage_RequestsSecondPage', () => {
    configure();
    create();
    adminService.getTeachers.calls.reset();

    const paginator = fixture.debugElement.query(By.directive(MatPaginator)).componentInstance as MatPaginator;
    expect(paginator.length).toBe(45);
    paginator.nextPage();
    fixture.detectChanges();

    expect(adminService.getTeachers).toHaveBeenCalledOnceWith({
      page: 2,
      pageSize: 20,
      schoolId: null,
      unassigned: false,
    });
    expect(router.navigate).toHaveBeenCalledWith(
      [],
      jasmine.objectContaining({
        queryParams: { schoolId: null, unassigned: null, page: 2, pageSize: null },
      }),
    );
  });

  it('paginator_PageSizeChange_RequestsWithNewPageSize', () => {
    configure();
    create();
    adminService.getTeachers.calls.reset();

    component.onPage({ pageIndex: 0, pageSize: 50, length: 45, previousPageIndex: 0 });

    expect(adminService.getTeachers).toHaveBeenCalledOnceWith({
      page: 1,
      pageSize: 50,
      schoolId: null,
      unassigned: false,
    });
  });

  it('deepLink_PageBeyondLastPage_FallsBackToLastPage', () => {
    configure({ page: '9' });
    adminService.getTeachers.and.returnValues(of(paged([], 45, 9)), of(paged([teacher()], 45, 3)));
    create();

    expect(adminService.getTeachers.calls.argsFor(1)[0].page).toBe(3);
    expect(cellTexts('fullName')).toEqual(['Ayşe Yılmaz']);
  });

  it('deepLink_EmptyPageWithinRange_ShowsEmptyStateWithoutRedirect', () => {
    // pageIndex (1) son sayfadan (2) büyük değil → geri dönüş yok, boş durum; döngü riski yok.
    configure({ page: '2' });
    adminService.getTeachers.and.returnValue(of(paged([], 45, 2)));
    create();

    expect(router.navigate).not.toHaveBeenCalled();
    expect(adminService.getTeachers).toHaveBeenCalledTimes(1);
    expect(text()).toContain('Öğretmen bulunamadı');
  });

  it('paginator_Labels_AreTranslatedFromAdminScope', () => {
    configure();
    create();

    const label = fixture.nativeElement.querySelector('.mat-mdc-paginator-range-label') as HTMLElement;
    expect(label.textContent?.trim()).toBe('1–20 / 45');
    expect(text()).toContain(adminTr.paginator.itemsPerPage);
  });

  it('deepLink_HugePage_IsIgnored', () => {
    configure({ page: '100001' });
    create();

    expect(adminService.getTeachers.calls.first().args[0].page).toBe(1);
  });

  // ── URL tek doğruluk kaynağı ─────────────────────────────────────────────

  it('filterChange_OnlyUpdatesUrl_LoadIsTriggeredByQueryParamEmission', () => {
    configure();
    create();
    adminService.getTeachers.calls.reset();
    (router.navigate as jasmine.Spy).and.resolveTo(true); // URL emisyonu olmasın

    component.onFilterChange(5);

    expect(router.navigate).toHaveBeenCalled();
    expect(adminService.getTeachers).not.toHaveBeenCalled();
    expect(component.filter()).toBe('all');
  });

  it('reuse_MenuNavigationWithoutParams_ResetsFilterAndPage', () => {
    configure({ schoolId: '5', page: '2' });
    create();
    expect(component.filter()).toBe(5);
    adminService.getTeachers.calls.reset();

    // Menüden `/admin/teachers`: aynı komponent örneği, param'sız yeni queryParamMap.
    queryParams$.next(convertToParamMap({}));
    fixture.detectChanges();

    expect(component.filter()).toBe('all');
    expect(component.pageIndex()).toBe(0);
    expect(adminService.getTeachers).toHaveBeenCalledOnceWith({
      page: 1,
      pageSize: 20,
      schoolId: null,
      unassigned: false,
    });
  });

  it('deepLink_UnknownSchoolId_FilterResetsToAllAndReloads', () => {
    configure({ schoolId: '999' });
    create();
    fixture.detectChanges(); // okul listesi geldi → filtre efekti "Tümü"ye çeker

    expect(component.filter()).toBe('all');
    expect(adminService.getTeachers.calls.mostRecent().args[0]).toEqual({
      page: 1,
      pageSize: 20,
      schoolId: null,
      unassigned: false,
    });
  });
});
