import { EnvironmentProviders, Provider } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, ParamMap, Params, Router, convertToParamMap, provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { MatPaginator } from '@angular/material/paginator';
import { BehaviorSubject, Subject, of, throwError } from 'rxjs';

import { AdminStudentsComponent } from './admin-students.component';
import { AdminService } from '../../../services/admin.service';
import { AdminStudentListItem } from '../../../models/admin-student.model';
import { Paged } from '../../../models/test-instance';
import { School } from '../../../models/taxonomy';
import { SchoolFilterComponent } from '../../../shared/components/school-filter/school-filter.component';
import { routes } from '../../../app.routes';
import { authGuard } from '../../../shared/guards/auth.guard';
import { adminGuard } from '../../../shared/guards/admin.guard';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import adminTr from '../../../../../public/i18n/admin/tr.json';

describe('AdminStudentsComponent', () => {
  let fixture: ComponentFixture<AdminStudentsComponent>;
  let component: AdminStudentsComponent;
  let adminService: jasmine.SpyObj<AdminService>;
  let router: Router;

  const schools: School[] = [
    { id: 5, name: 'Ankara Lisesi', provinceId: null, provinceName: null, districtId: null, districtName: null, addressLine: null },
  ];

  function student(overrides: Partial<AdminStudentListItem> = {}): AdminStudentListItem {
    return {
      id: 7,
      fullName: 'Ali Veli',
      email: 'a***@ornek.com', // issue #246: backend listede maskeli döner
      studentNumber: '1234',
      schoolId: 5,
      schoolName: 'Ankara Lisesi',
      gradeId: 9,
      gradeName: '9. Sınıf',
      isEnabled: true,
      ...overrides,
    };
  }

  function paged(items: AdminStudentListItem[], totalCount = items.length, pageNumber = 1): Paged<AdminStudentListItem> {
    return { pageNumber, pageSize: 20, totalCount, items };
  }

  /** Router'ın query param akışını taklit eder: `navigate` çağrısı bu subject'e yeni param'ları iter. */
  let queryParams$: BehaviorSubject<ParamMap>;

  function paramsOf(map: ParamMap): Params {
    return map.keys.reduce<Params>((acc, key) => ({ ...acc, [key]: map.get(key) }), {});
  }

  /** TestBed'i kurar; komponent `create()` ile oluşturulur (constructor URL aboneliğiyle hemen istek atar). */
  function configure(initialParams: Params = {}): void {
    adminService = jasmine.createSpyObj<AdminService>('AdminService', ['getStudents', 'getSchools']);
    adminService.getSchools.and.returnValue(of(schools));
    adminService.getStudents.and.returnValue(of(paged([student()], 45)));
    queryParams$ = new BehaviorSubject<ParamMap>(convertToParamMap(initialParams));

    const providers: (Provider | EnvironmentProviders)[] = [
      { provide: AdminService, useValue: adminService },
      provideRouter([]),
      provideNoopAnimations(),
      { provide: ActivatedRoute, useValue: { queryParamMap: queryParams$.asObservable(), snapshot: {} } },
    ];

    TestBed.configureTestingModule({
      imports: [
        AdminStudentsComponent,
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
    fixture = TestBed.createComponent(AdminStudentsComponent);
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

  /** Ad hücresindeki yalnız ad (öğrenci numarası ikincil satırı hariç). */
  function nameTexts(): string[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('td.mat-column-fullName > span')).map(
      (el) => (el.textContent ?? '').trim(),
    );
  }

  // ── Route ────────────────────────────────────────────────────────────────

  it('routes_AdminStudentsPath_IsGuardedByAuthAndAdminGuard', () => {
    const layoutRoute = routes.find((r) => Array.isArray(r.children));
    const route = layoutRoute?.children?.find((r) => r.path === 'admin/students');

    expect(route).withContext('route tanımı bulunamadı').toBeDefined();
    expect(route?.canActivate).toEqual([authGuard, adminGuard]);
  });

  // ── İlk yükleme ───────────────────────────────────────────────────────────

  it('init_NoQueryParams_RequestsFirstPageWithDefaultPageSizeAndNoFilter', () => {
    configure();
    create();

    expect(adminService.getStudents).toHaveBeenCalledOnceWith({
      page: 1,
      pageSize: 20,
      schoolId: null,
      unassigned: false,
    });
  });

  it('init_WhileRequestPending_ShowsSpinner', () => {
    configure();
    adminService.getStudents.and.returnValue(new Subject<Paged<AdminStudentListItem>>());
    create();

    expect(fixture.nativeElement.querySelector('mat-spinner')).not.toBeNull();
    expect(text()).toContain(adminTr.students.loading);
  });

  it('init_DeepLinkQueryParams_RestoresFilterAndPage', () => {
    configure({ schoolId: '5', page: '3', pageSize: '50' });
    create();

    expect(adminService.getStudents).toHaveBeenCalledOnceWith({
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

    expect(adminService.getStudents).toHaveBeenCalledOnceWith({
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
      adminTr.students.columns.fullName,
      adminTr.students.columns.email,
      adminTr.students.columns.school,
      adminTr.students.columns.grade,
      adminTr.students.columns.accountStatus,
    ]);
    expect(cellTexts('fullName')[0]).toContain('Ali Veli');
    expect(cellTexts('fullName')[0]).toContain('No: 1234');
    expect(cellTexts('email')).toEqual(['a***@ornek.com']); // maskeli değer olduğu gibi gösterilir
    expect(cellTexts('school')).toEqual(['Ankara Lisesi']);
    expect(cellTexts('grade')).toEqual(['9. Sınıf']);
    expect(cellTexts('accountStatus')).toEqual(['Aktif']);
  });

  it('render_StatusVariantsAndMissingFields_ShowsTurkishLabelsAndFallbacks', () => {
    configure();
    adminService.getStudents.and.returnValue(
      of(
        paged([
          student({ id: 1, isEnabled: null, fullName: '', email: '', studentNumber: '', gradeId: null, gradeName: null }),
          student({ id: 2, isEnabled: false, schoolId: null, schoolName: null }),
          student({ id: 3, schoolId: 8, schoolName: null }),
        ]),
      ),
    );
    create();

    expect(cellTexts('accountStatus')).toEqual(['Bilinmiyor', 'Pasif', 'Aktif']);
    expect(cellTexts('fullName')[0]).toBe('—');
    expect(cellTexts('email')[0]).toBe('—');
    expect(cellTexts('grade')).toEqual(['—', '9. Sınıf', '9. Sınıf']);
    // Okula bağlı değil → "Okulsuz"; okula bağlı ama ad çözülemedi → "—".
    expect(cellTexts('school')).toEqual(['Ankara Lisesi', adminTr.schoolFilter.unassignedStudent, '—']);
  });

  // ── Boş / hata ───────────────────────────────────────────────────────────

  it('render_EmptyPage_ShowsEmptyStateAndNoPaginator', () => {
    configure();
    adminService.getStudents.and.returnValue(of(paged([], 0)));
    create();

    expect(text()).toContain(adminTr.students.empty);
    expect(fixture.nativeElement.querySelector('table')).toBeNull();
    expect(fixture.nativeElement.querySelector('mat-paginator')).toBeNull();
  });

  it('render_RateLimited_ShowsRateLimitMessage', () => {
    configure();
    adminService.getStudents.and.returnValue(throwError(() => new HttpErrorResponse({ status: 429 })));
    create();

    const alert = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement;
    expect(alert).not.toBeNull();
    expect(alert.textContent).toContain(adminTr.students.rateLimited);
  });

  it('render_RequestFails_ShowsErrorWithRetry', () => {
    configure();
    adminService.getStudents.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    create();

    const alert = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement;
    expect(alert).not.toBeNull();
    expect(alert.textContent).toContain(adminTr.students.loadFailed);

    adminService.getStudents.and.returnValue(of(paged([student()])));
    (alert.querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(adminService.getStudents).toHaveBeenCalledTimes(2);
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
    expect(nameTexts()).toEqual(['Ali Veli']);
  });

  // ── Filtre ───────────────────────────────────────────────────────────────

  it('filterChange_FromSchoolFilter_RequestsFirstPageWithSchoolId', () => {
    configure({ page: '2' });
    create();
    adminService.getStudents.calls.reset();

    const filter = fixture.debugElement.query(By.directive(SchoolFilterComponent))
      .componentInstance as SchoolFilterComponent;
    filter.select(5);
    fixture.detectChanges();

    expect(adminService.getStudents).toHaveBeenCalledOnceWith({
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
    adminService.getStudents.calls.reset();

    component.onFilterChange('unassigned');

    expect(adminService.getStudents).toHaveBeenCalledOnceWith({
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
    adminService.getStudents.calls.reset();

    const paginator = fixture.debugElement.query(By.directive(MatPaginator)).componentInstance as MatPaginator;
    expect(paginator.length).toBe(45);
    paginator.nextPage();
    fixture.detectChanges();

    expect(adminService.getStudents).toHaveBeenCalledOnceWith({
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
    adminService.getStudents.calls.reset();

    component.onPage({ pageIndex: 0, pageSize: 50, length: 45, previousPageIndex: 0 });

    expect(adminService.getStudents).toHaveBeenCalledOnceWith({
      page: 1,
      pageSize: 50,
      schoolId: null,
      unassigned: false,
    });
  });

  it('deepLink_PageBeyondLastPage_FallsBackToLastPage', () => {
    configure({ page: '9' });
    adminService.getStudents.and.returnValues(of(paged([], 45, 9)), of(paged([student()], 45, 3)));
    create();

    expect(adminService.getStudents.calls.argsFor(1)[0].page).toBe(3);
    expect(nameTexts()).toEqual(['Ali Veli']);
  });

  it('deepLink_EmptyPageWithinRange_ShowsEmptyStateWithoutRedirect', () => {
    // pageIndex (1) son sayfadan (2) büyük değil → geri dönüş yok, boş durum; döngü riski yok.
    configure({ page: '2' });
    adminService.getStudents.and.returnValue(of(paged([], 45, 2)));
    create();

    expect(router.navigate).not.toHaveBeenCalled();
    expect(adminService.getStudents).toHaveBeenCalledTimes(1);
    expect(text()).toContain(adminTr.students.empty);
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

    expect(adminService.getStudents.calls.first().args[0].page).toBe(1);
  });

  // ── URL tek doğruluk kaynağı ─────────────────────────────────────────────

  it('filterChange_OnlyUpdatesUrl_LoadIsTriggeredByQueryParamEmission', () => {
    configure();
    create();
    adminService.getStudents.calls.reset();
    (router.navigate as jasmine.Spy).and.resolveTo(true); // URL emisyonu olmasın

    component.onFilterChange(5);

    expect(router.navigate).toHaveBeenCalled();
    expect(adminService.getStudents).not.toHaveBeenCalled();
    expect(component.filter()).toBe('all');
  });

  it('reuse_MenuNavigationWithoutParams_ResetsFilterAndPage', () => {
    configure({ schoolId: '5', page: '2' });
    create();
    expect(component.filter()).toBe(5);
    adminService.getStudents.calls.reset();

    // Menüden `/admin/students`: aynı komponent örneği, param'sız yeni queryParamMap.
    queryParams$.next(convertToParamMap({}));
    fixture.detectChanges();

    expect(component.filter()).toBe('all');
    expect(component.pageIndex()).toBe(0);
    expect(adminService.getStudents).toHaveBeenCalledOnceWith({
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
    expect(adminService.getStudents.calls.mostRecent().args[0]).toEqual({
      page: 1,
      pageSize: 20,
      schoolId: null,
      unassigned: false,
    });
  });

  it('render_Forbidden_ShowsForbiddenMessage', () => {
    configure();
    adminService.getStudents.and.returnValue(throwError(() => new HttpErrorResponse({ status: 403 })));
    create();

    const alert = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement;
    expect(alert.textContent).toContain(adminTr.students.forbidden);
  });

  it('schoolFilter_UsesStudentUnassignedLabel', () => {
    configure();
    create();

    const filter = fixture.debugElement.query(By.directive(SchoolFilterComponent))
      .componentInstance as SchoolFilterComponent;
    expect(filter.unassignedLabel()).toBe('unassignedStudent');
  });
});
