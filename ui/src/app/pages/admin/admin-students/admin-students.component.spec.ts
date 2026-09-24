import { EnvironmentProviders, Provider } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, ParamMap, Params, Router, convertToParamMap, provideRouter } from '@angular/router';
import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { MatPaginator } from '@angular/material/paginator';
import { BehaviorSubject, Subject, of, throwError } from 'rxjs';
import { MatDialog } from '@angular/material/dialog';
import { OverlayContainer } from '@angular/cdk/overlay';
import { AdminResetPasswordDialogComponent } from '../../../shared/components/admin-reset-password-dialog/admin-reset-password-dialog.component';
import { AdminAccountStatusDialogComponent } from '../../../shared/components/admin-account-status-dialog/admin-account-status-dialog.component';
import { AdminStudentSchoolDialogComponent } from '../../../shared/components/admin-student-school-dialog/admin-student-school-dialog.component';
import { SchoolService } from '../../../services/school.service';

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

  /** Issue #277: okul değişikliği dialog'unda seçilecek hedef okul. */
  const izmir: School = {
    id: 8,
    name: 'İzmir Lisesi',
    provinceId: null,
    provinceName: null,
    districtId: null,
    districtName: null,
    addressLine: null,
  };

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
    adminService = jasmine.createSpyObj<AdminService>('AdminService', [
      'getStudents',
      'getSchools',
      'resetPassword',
      'setAccountStatus',
      'changeStudentSchool',
    ]);
    adminService.getSchools.and.returnValue(of(schools));
    adminService.getStudents.and.returnValue(of(paged([student()], 45)));
    queryParams$ = new BehaviorSubject<ParamMap>(convertToParamMap(initialParams));

    const providers: (Provider | EnvironmentProviders)[] = [
      { provide: AdminService, useValue: adminService },
      { provide: SchoolService, useValue: { getSchools: () => of([...schools, izmir]) } },
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
      adminTr.students.columns.actions, // görsel olarak gizli başlık (#156)
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

  // ── Şifre sıfırlama (issue #156) ─────────────────────────────────────────

  function overlay(): HTMLElement {
    return TestBed.inject(OverlayContainer).getContainerElement();
  }

  function resetButtons(): HTMLButtonElement[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button[data-testid="reset-password"]'));
  }

  it('resetPassword_RowAction_OpensConfirmDialogWithRowIdentity', async () => {
    configure();
    create();
    const dialog = fixture.debugElement.injector.get(MatDialog);
    const openSpy = spyOn(dialog, 'open').and.callThrough();

    expect(resetButtons().length).toBe(1);
    resetButtons()[0].click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(openSpy).toHaveBeenCalledTimes(1);
    const config = openSpy.calls.mostRecent().args[1];
    expect(openSpy.calls.mostRecent().args[0]).toBe(AdminResetPasswordDialogComponent);
    expect(config?.data).toEqual({ target: 'student', id: 7, displayName: 'Ali Veli' });
    expect(config?.disableClose).toBeTrue();
    expect(overlay().textContent).toContain(adminTr.passwordReset.confirmTitle);
    expect(overlay().textContent).toContain('Ali Veli');
    // Onay verilmeden istek atılmaz.
    expect(adminService.resetPassword).not.toHaveBeenCalled();
  });

  it('resetPassword_CancelInDialog_SendsNoRequestAndReenablesAction', async () => {
    configure();
    create();

    resetButtons()[0].click();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(component.resetDialogOpen()).toBeTrue();

    (overlay().querySelector('button[data-testid="cancel"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(adminService.resetPassword).not.toHaveBeenCalled();
    expect(component.resetDialogOpen()).toBeFalse();
    expect(resetButtons()[0].disabled).toBeFalse();
  });

  it('resetPassword_DoubleClick_OpensSingleDialog', () => {
    configure();
    create();
    const dialog = fixture.debugElement.injector.get(MatDialog);
    const openSpy = spyOn(dialog, 'open').and.callThrough();

    const row = component.rows()[0];
    component.resetPassword(row);
    component.resetPassword(row);

    expect(openSpy).toHaveBeenCalledTimes(1);
  });

  // ── Hesap durumu: devre dışı bırak / etkinleştir (issue #155) ─────────────

  function statusButtons(): HTMLButtonElement[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button[data-testid="account-status"]'));
  }

  function overlayButton(testId: string): HTMLButtonElement {
    return TestBed.inject(OverlayContainer).getContainerElement().querySelector(`button[data-testid="${testId}"]`)!;
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('accountStatus_ActiveRow_OpensDisableDialogWithSessionsNoteAndSendsNoRequestYet', async () => {
    configure();
    create();
    const dialog = fixture.debugElement.injector.get(MatDialog);
    const openSpy = spyOn(dialog, 'open').and.callThrough();

    expect(statusButtons().length).toBe(1);
    expect(statusButtons()[0].getAttribute('aria-label')).toContain('devre dışı bırak');
    statusButtons()[0].click();
    await settle();

    expect(openSpy.calls.mostRecent().args[0]).toBe(AdminAccountStatusDialogComponent);
    const config = openSpy.calls.mostRecent().args[1];
    expect(config?.data).toEqual({ target: 'student', id: 7, displayName: 'Ali Veli', enable: false });
    expect(config?.disableClose).toBeTrue();
    const container = TestBed.inject(OverlayContainer).getContainerElement();
    expect(container.textContent).toContain(adminTr.accountStatus.disable.confirmTitle);
    expect(container.textContent).toContain(adminTr.accountStatus.disable.confirmSessions);
    expect(adminService.setAccountStatus).not.toHaveBeenCalled();
  });

  it('accountStatus_ConfirmDisable_UpdatesRowChipInstantlyWithoutReload', async () => {
    configure();
    adminService.setAccountStatus.and.returnValue(of({ enabled: false }));
    create();
    expect(cellTexts('accountStatus')).toEqual([adminTr.students.account.active]);
    adminService.getStudents.calls.reset();

    statusButtons()[0].click();
    await settle();
    overlayButton('confirm').click();
    await settle();

    expect(adminService.setAccountStatus).toHaveBeenCalledOnceWith('student', 7, false);
    expect(cellTexts('accountStatus')).toEqual([adminTr.students.account.inactive]);
    expect(component.rows()[0].accountStatus).toBe('inactive');
    expect(adminService.getStudents).not.toHaveBeenCalled();
    expect(component.statusDialogOpen()).toBeFalse();
    // Aksiyon artık "Etkinleştir".
    expect(statusButtons()[0].getAttribute('aria-label')).toContain('etkinleştir');
  });

  it('accountStatus_InactiveRow_EnablesAndRowBecomesActive', async () => {
    configure();
    adminService.getStudents.and.returnValue(of(paged([student({ isEnabled: false })])));
    adminService.setAccountStatus.and.returnValue(of({ enabled: true }));
    create();

    statusButtons()[0].click();
    await settle();
    expect(TestBed.inject(OverlayContainer).getContainerElement().textContent).toContain(
      adminTr.accountStatus.enable.confirmTitle,
    );
    overlayButton('confirm').click();
    await settle();

    expect(adminService.setAccountStatus).toHaveBeenCalledOnceWith('student', 7, true);
    expect(cellTexts('accountStatus')).toEqual([adminTr.students.account.active]);
  });

  it('accountStatus_Cancel_SendsNoRequestAndKeepsRow', async () => {
    configure();
    create();

    statusButtons()[0].click();
    await settle();
    expect(component.statusDialogOpen()).toBeTrue();
    overlayButton('cancel').click();
    await settle();

    expect(adminService.setAccountStatus).not.toHaveBeenCalled();
    expect(component.statusDialogOpen()).toBeFalse();
    expect(cellTexts('accountStatus')).toEqual([adminTr.students.account.active]);
  });

  it('accountStatus_UnknownStatus_HasNoAction', () => {
    configure();
    adminService.getStudents.and.returnValue(of(paged([student({ isEnabled: null })])));
    create();

    expect(statusButtons().length).toBe(0);
    component.toggleAccountStatus(component.rows()[0]);
    expect(component.statusDialogOpen()).toBeFalse();
  });

  it('accountStatus_DoubleClick_OpensSingleDialog', () => {
    configure();
    create();
    const dialog = fixture.debugElement.injector.get(MatDialog);
    const openSpy = spyOn(dialog, 'open').and.callThrough();

    const row = component.rows()[0];
    component.toggleAccountStatus(row);
    component.toggleAccountStatus(row);

    expect(openSpy).toHaveBeenCalledTimes(1);
  });

  it('accountStatus_ErrorThenCancel_ReloadsListSoRowIsNotStale', async () => {
    configure();
    adminService.setAccountStatus.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 502, error: { message: 'oturumlar kapatılamadı' } })),
    );
    create();
    adminService.getStudents.calls.reset();
    adminService.getStudents.and.returnValue(of(paged([student({ isEnabled: false })])));

    statusButtons()[0].click();
    await settle();
    overlayButton('confirm').click();
    await settle();
    expect(adminService.getStudents).not.toHaveBeenCalled();
    overlayButton('cancel').click();
    await settle();

    expect(adminService.setAccountStatus).toHaveBeenCalledOnceWith('student', 7, false);
    expect(adminService.getStudents).toHaveBeenCalledTimes(1);
    expect(cellTexts('accountStatus')).toEqual([adminTr.students.account.inactive]);
  });
  // ── Okul değişikliği (issue #277 madde 8) ─────────────────────────────────

  function schoolButtons(): HTMLButtonElement[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button[data-testid="change-school"]'));
  }

  /** Açık dialog'da okul seçer (seçim bileşeninin kendi testi ayrı; burada sayfa entegrasyonu doğrulanır). */
  async function openSchoolDialogAndPick(school: School): Promise<AdminStudentSchoolDialogComponent> {
    schoolButtons()[0].click();
    await settle();
    const dialog = fixture.debugElement.injector.get(MatDialog);
    const instance = dialog.openDialogs[0].componentInstance as AdminStudentSchoolDialogComponent;
    instance.selectedSchoolId.set(school.id);
    instance.onSchoolSelected(school);
    await settle();
    return instance;
  }

  it('changeSchool_OpensDialogWithCurrentSchoolAndEffects_NoRequestYet', async () => {
    configure();
    create();
    const dialog = fixture.debugElement.injector.get(MatDialog);
    const openSpy = spyOn(dialog, 'open').and.callThrough();

    expect(schoolButtons().length).toBe(1);
    expect(schoolButtons()[0].getAttribute('aria-label')).toContain('Ali Veli');
    schoolButtons()[0].click();
    await settle();

    expect(openSpy.calls.mostRecent().args[0]).toBe(AdminStudentSchoolDialogComponent);
    const config = openSpy.calls.mostRecent().args[1];
    expect(config?.data).toEqual({ id: 7, displayName: 'Ali Veli', currentSchoolId: 5, currentSchoolName: 'Ankara Lisesi' });
    expect(config?.disableClose).toBeTrue();
    expect(overlay().textContent).toContain(adminTr.studentSchool.effectAssignments);
    expect(overlay().textContent).toContain(adminTr.studentSchool.effectOldTeachers);
    expect(overlayButton('confirm').disabled).toBeTrue();
    expect(adminService.changeStudentSchool).not.toHaveBeenCalled();
  });

  it('changeSchool_Success_UpdatesRowInstantlyWithoutReload', async () => {
    configure();
    adminService.changeStudentSchool.and.returnValue(
      of({ studentId: 7, schoolId: 8, previousSchoolId: 5, changed: true }),
    );
    create();
    adminService.getStudents.calls.reset();

    await openSchoolDialogAndPick(izmir);
    overlayButton('confirm').click();
    await settle();

    expect(adminService.changeStudentSchool).toHaveBeenCalledOnceWith(7, 8);
    expect(cellTexts('school')).toEqual(['İzmir Lisesi']);
    expect(component.rows()[0].schoolId).toBe(8);
    expect(adminService.getStudents).not.toHaveBeenCalled();
    expect(component.schoolDialogOpen()).toBeFalse();
  });

  it('changeSchool_Conflict409_ShowsMessageAndReloadButtonReloadsList', async () => {
    configure();
    adminService.changeStudentSchool.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: { message: 'Okul eşzamanlı değiştirildi.' } })),
    );
    create();
    adminService.getStudents.calls.reset();

    await openSchoolDialogAndPick(izmir);
    overlayButton('confirm').click();
    await settle();

    expect(overlay().querySelector('[data-testid="school-error"]')?.textContent).toContain('Okul eşzamanlı değiştirildi.');
    expect(overlayButton('confirm')).toBeNull();
    expect(adminService.getStudents).not.toHaveBeenCalled();

    overlayButton('reload').click();
    await settle();

    expect(adminService.changeStudentSchool).toHaveBeenCalledTimes(1);
    expect(adminService.getStudents).toHaveBeenCalledTimes(1);
    expect(component.schoolDialogOpen()).toBeFalse();
  });

  it('changeSchool_RateLimited429_ShowsRetryAfterSecondsAndAllowsRetry', async () => {
    configure();
    adminService.changeStudentSchool.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 429,
            error: 'Too many requests',
            headers: new HttpHeaders({ 'Retry-After': '42' }),
          }),
      ),
    );
    create();

    await openSchoolDialogAndPick(izmir);
    overlayButton('confirm').click();
    await settle();

    expect(overlay().querySelector('[data-testid="school-error"]')?.textContent).toContain(
      adminTr.studentSchool.errors.rateLimitedSeconds.replace('{{seconds}}', '42'),
    );
    expect(overlayButton('confirm').disabled).toBeFalse();
    expect(overlayButton('confirm').textContent).toContain(adminTr.studentSchool.retry);
    expect(cellTexts('school')).toEqual(['Ankara Lisesi']);
  });
});
