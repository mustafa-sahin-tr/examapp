import type { Mock, MockedObject } from "vitest";
import { EnvironmentProviders, Provider } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, ParamMap, Params, Router, convertToParamMap, provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { MatPaginator } from '@angular/material/paginator';
import { BehaviorSubject, Subject, of, throwError } from 'rxjs';
import { MatDialog } from '@angular/material/dialog';
import { OverlayContainer } from '@angular/cdk/overlay';
import { AdminResetPasswordDialogComponent } from '../../../shared/components/admin-reset-password-dialog/admin-reset-password-dialog.component';
import { AdminAccountStatusDialogComponent } from '../../../shared/components/admin-account-status-dialog/admin-account-status-dialog.component';
import { AdminTeacherSuspensionDialogComponent } from '../../../shared/components/admin-teacher-suspension-dialog/admin-teacher-suspension-dialog.component';
import { AdminTeacherSuspensionResponse } from '../../../models/admin-teacher-suspension.model';
import { AdminTeacherSchoolDialogComponent } from '../../../shared/components/admin-teacher-school-dialog/admin-teacher-school-dialog.component';
import { AdminTeacherSchoolResponse } from '../../../models/admin-teacher-school.model';
import { SchoolService } from '../../../services/school.service';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltip } from '@angular/material/tooltip';

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
    let adminService: MockedObject<AdminService>;
    let snackBar: MockedObject<MatSnackBar>;
    let router: Router;

    const schools: School[] = [
        { id: 5, name: 'Ankara Lisesi', provinceId: null, provinceName: null, districtId: null, districtName: null, addressLine: null },
    ];
    const izmir: School = {
        id: 8,
        name: 'İzmir Lisesi',
        provinceId: null,
        provinceName: null,
        districtId: null,
        districtName: null,
        addressLine: null,
    };

    function teacher(overrides: Partial<AdminTeacherListItem> = {}): AdminTeacherListItem {
        return {
            id: 12,
            fullName: 'Ayşe Yılmaz',
            email: 'a***@okul.k12.tr', // issue #246: backend listede maskeli döner
            schoolId: 5,
            schoolName: 'Ankara Lisesi',
            isIndependentTutor: false,
            approvalStatus: 'Approved',
            isEnabled: true,
            accountApproved: true,
            accountSuspended: false,
            accountSuspendedAt: null,
            accountSuspensionReason: null,
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
        adminService = {
            getTeachers: vi.fn().mockName("AdminService.getTeachers"),
            getSchools: vi.fn().mockName("AdminService.getSchools"),
            resetPassword: vi.fn().mockName("AdminService.resetPassword"),
            setAccountStatus: vi.fn().mockName("AdminService.setAccountStatus"),
            suspendTeacher: vi.fn().mockName("AdminService.suspendTeacher"),
            unsuspendTeacher: vi.fn().mockName("AdminService.unsuspendTeacher"),
            changeTeacherSchool: vi.fn().mockName("AdminService.changeTeacherSchool")
        } as unknown as MockedObject<AdminService>;
        snackBar = {
            open: vi.fn().mockName("MatSnackBar.open")
        } as unknown as MockedObject<MatSnackBar>;
        adminService.getSchools.mockReturnValue(of(schools));
        adminService.getTeachers.mockReturnValue(of(paged([teacher()], 45)));
        queryParams$ = new BehaviorSubject<ParamMap>(convertToParamMap(initialParams));

        const providers: (Provider | EnvironmentProviders)[] = [
            { provide: AdminService, useValue: adminService },
            { provide: SchoolService, useValue: { getSchools: () => of([...schools, izmir]) } },
            { provide: MatSnackBar, useValue: snackBar },
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
        vi.spyOn(router, 'navigate').mockImplementation((_commands, extras) => {
            const merged: Params = { ...paramsOf(queryParams$.value), ...(extras?.queryParams ?? {}) };
            const next = Object.fromEntries(Object.entries(merged)
                .filter(([, v]) => v !== null && v !== undefined)
                .map(([k, v]) => [k, String(v)]));
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
        return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll(`td.mat-column-${column}`)).map((td) => (td.textContent ?? '').trim());
    }

    // ── Route ────────────────────────────────────────────────────────────────

    it('routes_AdminTeachersPath_IsGuardedByAuthAndAdminGuard', () => {
        const layoutRoute = routes.find((r) => Array.isArray(r.children));
        const route = layoutRoute?.children?.find((r) => r.path === 'admin/teachers');

        expect(route, 'route tanımı bulunamadı').toBeDefined();
        expect(route?.canActivate).toEqual([authGuard, adminGuard]);
    });

    // ── İlk yükleme ───────────────────────────────────────────────────────────

    it('init_NoQueryParams_RequestsFirstPageWithDefaultPageSizeAndNoFilter', () => {
        configure();
        create();

        expect(adminService.getTeachers).toHaveBeenCalledTimes(1);

        expect(adminService.getTeachers).toHaveBeenCalledWith({
            page: 1,
            pageSize: 20,
            schoolId: null,
            unassigned: false,
        });
    });

    it('init_WhileRequestPending_ShowsSpinner', () => {
        configure();
        adminService.getTeachers.mockReturnValue(new Subject<Paged<AdminTeacherListItem>>());
        create();

        expect(fixture.nativeElement.querySelector('mat-spinner')).not.toBeNull();
        expect(text()).toContain(adminTr.teachers.loading);
    });

    it('init_DeepLinkQueryParams_RestoresFilterAndPage', () => {
        configure({ schoolId: '5', page: '3', pageSize: '50' });
        create();

        expect(adminService.getTeachers).toHaveBeenCalledTimes(1);

        expect(adminService.getTeachers).toHaveBeenCalledWith({
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

        expect(adminService.getTeachers).toHaveBeenCalledTimes(1);

        expect(adminService.getTeachers).toHaveBeenCalledWith({
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

        const headers = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('th')).map((th) => (th.textContent ?? '').trim());
        expect(headers).toEqual([
            adminTr.teachers.columns.fullName,
            adminTr.teachers.columns.email,
            adminTr.teachers.columns.school,
            adminTr.teachers.columns.approvalStatus,
            adminTr.teachers.columns.accountStatus,
            adminTr.teachers.columns.actions, // görsel olarak gizli başlık (#156)
        ]);
        expect(cellTexts('fullName')).toEqual(['Ayşe Yılmaz']);
        expect(cellTexts('email')).toEqual(['a***@okul.k12.tr']); // maskeli değer olduğu gibi gösterilir
        expect(cellTexts('school')).toEqual(['Ankara Lisesi']);
        expect(cellTexts('approvalStatus')).toEqual(['Onaylı']);
        expect(cellTexts('accountStatus')).toEqual(['Aktif']);
    });

    it('render_StatusVariantsAndMissingFields_ShowsTurkishLabelsAndFallbacks', () => {
        configure();
        adminService.getTeachers.mockReturnValue(of(paged([
            teacher({ id: 1, approvalStatus: 'Pending', isEnabled: null, fullName: '', email: '' }),
            teacher({ id: 2, approvalStatus: 'Rejected', isEnabled: false, schoolId: null, schoolName: null, isIndependentTutor: true }),
            teacher({ id: 3, schoolId: null, schoolName: null, isIndependentTutor: false }),
        ])));
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
        adminService.getTeachers.mockReturnValue(of(paged([], 0)));
        create();

        expect(text()).toContain('Öğretmen bulunamadı');
        expect(fixture.nativeElement.querySelector('table')).toBeNull();
        expect(fixture.nativeElement.querySelector('mat-paginator')).toBeNull();
    });

    it('render_RateLimited_ShowsRateLimitMessage', () => {
        configure();
        adminService.getTeachers.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 429 })));
        create();

        const alert = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement;
        expect(alert).not.toBeNull();
        expect(alert.textContent).toContain(adminTr.teachers.rateLimited);
    });

    it('render_RequestFails_ShowsErrorWithRetry', () => {
        configure();
        adminService.getTeachers.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
        create();

        const alert = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement;
        expect(alert).not.toBeNull();
        expect(alert.textContent).toContain(adminTr.teachers.loadFailed);

        adminService.getTeachers.mockReturnValue(of(paged([teacher()])));
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
        adminService.getTeachers.mockClear();

        const filter = fixture.debugElement.query(By.directive(SchoolFilterComponent))
            .componentInstance as SchoolFilterComponent;
        filter.select(5);
        fixture.detectChanges();

        expect(adminService.getTeachers).toHaveBeenCalledTimes(1);

        expect(adminService.getTeachers).toHaveBeenCalledWith({
            page: 1,
            pageSize: 20,
            schoolId: 5,
            unassigned: false,
        });
        expect(component.pageIndex()).toBe(0);
        expect(router.navigate).toHaveBeenCalledWith([], expect.objectContaining({
            queryParams: { schoolId: 5, unassigned: null, page: null, pageSize: null },
        }));
    });

    it('filterChange_Unassigned_RequestsUnassignedWithoutSchoolId', () => {
        configure();
        create();
        adminService.getTeachers.mockClear();

        component.onFilterChange('unassigned');

        expect(adminService.getTeachers).toHaveBeenCalledTimes(1);

        expect(adminService.getTeachers).toHaveBeenCalledWith({
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
        adminService.getTeachers.mockClear();

        const paginator = fixture.debugElement.query(By.directive(MatPaginator)).componentInstance as MatPaginator;
        expect(paginator.length).toBe(45);
        paginator.nextPage();
        fixture.detectChanges();

        expect(adminService.getTeachers).toHaveBeenCalledTimes(1);

        expect(adminService.getTeachers).toHaveBeenCalledWith({
            page: 2,
            pageSize: 20,
            schoolId: null,
            unassigned: false,
        });
        expect(router.navigate).toHaveBeenCalledWith([], expect.objectContaining({
            queryParams: { schoolId: null, unassigned: null, page: 2, pageSize: null },
        }));
    });

    it('paginator_PageSizeChange_RequestsWithNewPageSize', () => {
        configure();
        create();
        adminService.getTeachers.mockClear();

        component.onPage({ pageIndex: 0, pageSize: 50, length: 45, previousPageIndex: 0 });

        expect(adminService.getTeachers).toHaveBeenCalledTimes(1);

        expect(adminService.getTeachers).toHaveBeenCalledWith({
            page: 1,
            pageSize: 50,
            schoolId: null,
            unassigned: false,
        });
    });

    it('deepLink_PageBeyondLastPage_FallsBackToLastPage', () => {
        configure({ page: '9' });
        adminService.getTeachers.mockReturnValueOnce(of(paged([], 45, 9))).mockReturnValueOnce(of(paged([teacher()], 45, 3)));
        create();

        expect(vi.mocked(adminService.getTeachers).mock.calls[1][0].page).toBe(3);
        expect(cellTexts('fullName')).toEqual(['Ayşe Yılmaz']);
    });

    it('deepLink_EmptyPageWithinRange_ShowsEmptyStateWithoutRedirect', () => {
        // pageIndex (1) son sayfadan (2) büyük değil → geri dönüş yok, boş durum; döngü riski yok.
        configure({ page: '2' });
        adminService.getTeachers.mockReturnValue(of(paged([], 45, 2)));
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

        expect(vi.mocked(adminService.getTeachers).mock.calls[0][0].page).toBe(1);
    });

    // ── URL tek doğruluk kaynağı ─────────────────────────────────────────────

    it('filterChange_OnlyUpdatesUrl_LoadIsTriggeredByQueryParamEmission', () => {
        configure();
        create();
        adminService.getTeachers.mockClear();
        (router.navigate as Mock).mockResolvedValue(true); // URL emisyonu olmasın

        component.onFilterChange(5);

        expect(router.navigate).toHaveBeenCalled();
        expect(adminService.getTeachers).not.toHaveBeenCalled();
        expect(component.filter()).toBe('all');
    });

    it('reuse_MenuNavigationWithoutParams_ResetsFilterAndPage', () => {
        configure({ schoolId: '5', page: '2' });
        create();
        expect(component.filter()).toBe(5);
        adminService.getTeachers.mockClear();

        // Menüden `/admin/teachers`: aynı komponent örneği, param'sız yeni queryParamMap.
        queryParams$.next(convertToParamMap({}));
        fixture.detectChanges();

        expect(component.filter()).toBe('all');
        expect(component.pageIndex()).toBe(0);
        expect(adminService.getTeachers).toHaveBeenCalledTimes(1);
        expect(adminService.getTeachers).toHaveBeenCalledWith({
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
        expect(vi.mocked(adminService.getTeachers).mock.lastCall![0]).toEqual({
            page: 1,
            pageSize: 20,
            schoolId: null,
            unassigned: false,
        });
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
        const openSpy = vi.spyOn(dialog, 'open');

        expect(resetButtons().length).toBe(1);
        resetButtons()[0].click();
        fixture.detectChanges();
        await fixture.whenStable();

        expect(openSpy).toHaveBeenCalledTimes(1);
        const config = vi.mocked(openSpy).mock.lastCall![1];
        expect(vi.mocked(openSpy).mock.lastCall![0]).toBe(AdminResetPasswordDialogComponent);
        expect(config?.data).toEqual({ target: 'teacher', id: 12, displayName: 'Ayşe Yılmaz' });
        expect(config?.disableClose).toBe(true);
        expect(overlay().textContent).toContain(adminTr.passwordReset.confirmTitle);
        expect(overlay().textContent).toContain('Ayşe Yılmaz');
        // Onay verilmeden istek atılmaz.
        expect(adminService.resetPassword).not.toHaveBeenCalled();
    });

    it('resetPassword_CancelInDialog_SendsNoRequestAndReenablesAction', async () => {
        configure();
        create();

        resetButtons()[0].click();
        fixture.detectChanges();
        await fixture.whenStable();
        expect(component.resetDialogOpen()).toBe(true);

        (overlay().querySelector('button[data-testid="cancel"]') as HTMLButtonElement).click();
        fixture.detectChanges();
        await fixture.whenStable();
        fixture.detectChanges();

        expect(adminService.resetPassword).not.toHaveBeenCalled();
        expect(component.resetDialogOpen()).toBe(false);
        expect(resetButtons()[0].disabled).toBe(false);
    });

    it('resetPassword_DoubleClick_OpensSingleDialog', () => {
        configure();
        create();
        const dialog = fixture.debugElement.injector.get(MatDialog);
        const openSpy = vi.spyOn(dialog, 'open');

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
        const openSpy = vi.spyOn(dialog, 'open');

        expect(statusButtons().length).toBe(1);
        expect(statusButtons()[0].getAttribute('aria-label')).toContain('devre dışı bırak');
        statusButtons()[0].click();
        await settle();

        expect(vi.mocked(openSpy).mock.lastCall![0]).toBe(AdminAccountStatusDialogComponent);
        const config = vi.mocked(openSpy).mock.lastCall![1];
        expect(config?.data).toEqual({ target: 'teacher', id: 12, displayName: 'Ayşe Yılmaz', enable: false });
        expect(config?.disableClose).toBe(true);
        const container = TestBed.inject(OverlayContainer).getContainerElement();
        expect(container.textContent).toContain(adminTr.accountStatus.disable.confirmTitle);
        expect(container.textContent).toContain(adminTr.accountStatus.disable.confirmSessions);
        expect(adminService.setAccountStatus).not.toHaveBeenCalled();
    });

    it('accountStatus_ConfirmDisable_UpdatesRowChipInstantlyWithoutReload', async () => {
        configure();
        adminService.setAccountStatus.mockReturnValue(of({ enabled: false }));
        create();
        expect(cellTexts('accountStatus')).toEqual([adminTr.teachers.account.active]);
        adminService.getTeachers.mockClear();

        statusButtons()[0].click();
        await settle();
        overlayButton('confirm').click();
        await settle();

        expect(adminService.setAccountStatus).toHaveBeenCalledTimes(1);

        expect(adminService.setAccountStatus).toHaveBeenCalledWith('teacher', 12, false);
        expect(cellTexts('accountStatus')).toEqual([adminTr.teachers.account.inactive]);
        expect(component.rows()[0].accountStatus).toBe('inactive');
        expect(adminService.getTeachers).not.toHaveBeenCalled();
        expect(component.statusDialogOpen()).toBe(false);
        // Aksiyon artık "Etkinleştir".
        expect(statusButtons()[0].getAttribute('aria-label')).toContain('etkinleştir');
    });

    it('accountStatus_InactiveRow_EnablesAndRowBecomesActive', async () => {
        configure();
        adminService.getTeachers.mockReturnValue(of(paged([teacher({ isEnabled: false })])));
        adminService.setAccountStatus.mockReturnValue(of({ enabled: true }));
        create();

        statusButtons()[0].click();
        await settle();
        expect(TestBed.inject(OverlayContainer).getContainerElement().textContent).toContain(adminTr.accountStatus.enable.confirmTitle);
        overlayButton('confirm').click();
        await settle();

        expect(adminService.setAccountStatus).toHaveBeenCalledTimes(1);

        expect(adminService.setAccountStatus).toHaveBeenCalledWith('teacher', 12, true);
        expect(cellTexts('accountStatus')).toEqual([adminTr.teachers.account.active]);
    });

    it('accountStatus_Cancel_SendsNoRequestAndKeepsRow', async () => {
        configure();
        create();

        statusButtons()[0].click();
        await settle();
        expect(component.statusDialogOpen()).toBe(true);
        overlayButton('cancel').click();
        await settle();

        expect(adminService.setAccountStatus).not.toHaveBeenCalled();
        expect(component.statusDialogOpen()).toBe(false);
        expect(cellTexts('accountStatus')).toEqual([adminTr.teachers.account.active]);
    });

    it('accountStatus_UnknownStatus_HasNoAction', () => {
        configure();
        adminService.getTeachers.mockReturnValue(of(paged([teacher({ isEnabled: null })])));
        create();

        expect(statusButtons().length).toBe(0);
        component.toggleAccountStatus(component.rows()[0]);
        expect(component.statusDialogOpen()).toBe(false);
    });

    it('accountStatus_DoubleClick_OpensSingleDialog', () => {
        configure();
        create();
        const dialog = fixture.debugElement.injector.get(MatDialog);
        const openSpy = vi.spyOn(dialog, 'open');

        const row = component.rows()[0];
        component.toggleAccountStatus(row);
        component.toggleAccountStatus(row);

        expect(openSpy).toHaveBeenCalledTimes(1);
    });

    it('accountStatus_ErrorThenCancel_ReloadsListSoRowIsNotStale', async () => {
        configure();
        adminService.setAccountStatus.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 502, error: { message: 'oturumlar kapatılamadı' } })));
        create();
        adminService.getTeachers.mockClear();
        adminService.getTeachers.mockReturnValue(of(paged([teacher({ isEnabled: false })])));

        statusButtons()[0].click();
        await settle();
        overlayButton('confirm').click();
        await settle();
        expect(adminService.getTeachers).not.toHaveBeenCalled();
        overlayButton('cancel').click();
        await settle();

        expect(adminService.setAccountStatus).toHaveBeenCalledTimes(1);

        expect(adminService.setAccountStatus).toHaveBeenCalledWith('teacher', 12, false);
        expect(adminService.getTeachers).toHaveBeenCalledTimes(1);
        expect(cellTexts('accountStatus')).toEqual([adminTr.teachers.account.inactive]);
    });

    // ── Öğretmen hesap onayı askıya alma / kaldırma (issue #289) ─────────────

    const SUSPENDED_AT = '2026-09-29T19:03:20Z';
    const REASON = 'Şikâyet inceleniyor';

    function suspendedTeacher(): AdminTeacherListItem {
        return teacher({
            accountApproved: false,
            accountSuspended: true,
            accountSuspendedAt: SUSPENDED_AT,
            accountSuspensionReason: REASON,
        });
    }

    function suspensionButtons(): HTMLButtonElement[] {
        return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button[data-testid="suspension"]'));
    }

    function suspensionResponse(overrides: Partial<AdminTeacherSuspensionResponse> = {}): AdminTeacherSuspensionResponse {
        return {
            teacherId: 12,
            accountApproved: false,
            accountSuspended: true,
            accountApprovedAt: null,
            accountSuspendedAt: SUSPENDED_AT,
            ...overrides,
        };
    }

    function typeReason(value: string): void {
        const textarea = TestBed.inject(OverlayContainer)
            .getContainerElement()
            .querySelector('textarea[data-testid="reason"]') as HTMLTextAreaElement;
        textarea.value = value;
        textarea.dispatchEvent(new Event('input'));
        fixture.detectChanges();
    }

    const actionFor = (template: string): string => template.replace('{{name}}', 'Ayşe Yılmaz');

    it('suspension_ApprovedRow_ShowsSuspendActionAndNoSuspendedChip', () => {
        configure();
        create();

        expect(suspensionButtons().length).toBe(1);
        expect(suspensionButtons()[0].getAttribute('aria-label')).toBe(actionFor(adminTr.teacherSuspension.suspendActionFor));
        expect(fixture.nativeElement.querySelector('[data-testid="suspended-chip"]')).toBeNull();
    });

    it('suspension_SuspendedRow_ShowsChipReasonDateAndUnsuspendAction', () => {
        configure();
        adminService.getTeachers.mockReturnValue(of(paged([suspendedTeacher()])));
        create();

        const chip = fixture.nativeElement.querySelector('[data-testid="suspended-chip"]') as HTMLElement;
        expect(chip.textContent?.trim()).toBe(adminTr.teachers.suspension.chip);
        const detail = (fixture.nativeElement.querySelector('[data-testid="suspension-detail"]') as HTMLElement).textContent ?? '';
        expect(detail).toContain(REASON);
        expect(detail).toMatch(/\d{1,2}\/\d{1,2}\/\d{2}/); // DatePipe 'short'
        expect(suspensionButtons().length).toBe(1);
        expect(suspensionButtons()[0].getAttribute('aria-label')).toBe(actionFor(adminTr.teacherSuspension.unsuspendActionFor));
    });

    it('suspension_PendingOrRejectedRow_HasNoSuspensionAction', () => {
        configure();
        adminService.getTeachers.mockReturnValue(of(paged([
            teacher({ id: 1, approvalStatus: 'Pending', accountApproved: false }),
            teacher({ id: 2, approvalStatus: 'Rejected', accountApproved: false }),
        ])));
        create();

        expect(suspensionButtons().length).toBe(0);
        component.toggleSuspension(component.rows()[0]);
        expect(component.suspensionDialogOpen()).toBe(false);
    });

    it('suspend_OpensReasonDialog_ConfirmDisabledUntilNonBlankReason', async () => {
        configure();
        create();
        const dialog = fixture.debugElement.injector.get(MatDialog);
        const openSpy = vi.spyOn(dialog, 'open');

        suspensionButtons()[0].click();
        await settle();

        expect(vi.mocked(openSpy).mock.lastCall![0]).toBe(AdminTeacherSuspensionDialogComponent);
        const config = vi.mocked(openSpy).mock.lastCall![1];
        expect(config?.data).toEqual({ teacherId: 12, displayName: 'Ayşe Yılmaz', mode: 'suspend' });
        expect(config?.disableClose).toBe(true);
        const container = TestBed.inject(OverlayContainer).getContainerElement();
        expect(container.textContent).toContain(adminTr.teacherSuspension.suspend.confirmTitle);
        expect(container.querySelector('textarea')?.getAttribute('maxlength')).toBe('500');
        expect(overlayButton('confirm').disabled).toBe(true);

        typeReason('   ');
        expect(overlayButton('confirm').disabled).toBe(true);
        expect(adminService.suspendTeacher).not.toHaveBeenCalled();
    });

    it('suspend_ConfirmWithReason_SendsTrimmedReasonAndUpdatesRowWithoutReload', async () => {
        configure();
        adminService.suspendTeacher.mockReturnValue(of(suspensionResponse()));
        create();
        adminService.getTeachers.mockClear();

        suspensionButtons()[0].click();
        await settle();
        typeReason(`  ${REASON}  `);
        const counter = TestBed.inject(OverlayContainer).getContainerElement().querySelector('[data-testid="reason-counter"]');
        expect(counter?.textContent?.trim()).toBe(`${REASON.length + 4} / 500`);
        overlayButton('confirm').click();
        await settle();

        expect(adminService.suspendTeacher).toHaveBeenCalledTimes(1);

        expect(adminService.suspendTeacher).toHaveBeenCalledWith(12, REASON);
        expect(adminService.getTeachers).not.toHaveBeenCalled();
        const row = component.rows()[0];
        expect(row.accountSuspended).toBe(true);
        expect(row.accountApproved).toBe(false);
        expect(row.suspensionReason).toBe(REASON);
        expect(row.suspendedAt).toBe(SUSPENDED_AT);
        expect(fixture.nativeElement.querySelector('[data-testid="suspended-chip"]')).not.toBeNull();
        expect(suspensionButtons()[0].getAttribute('aria-label')).toBe(actionFor(adminTr.teacherSuspension.unsuspendActionFor));
        expect(component.suspensionDialogOpen()).toBe(false);
    });

    it('unsuspend_SimpleConfirm_CallsUnsuspendAndRowBecomesApproved', async () => {
        configure();
        adminService.getTeachers.mockReturnValue(of(paged([suspendedTeacher()])));
        adminService.unsuspendTeacher.mockReturnValue(of(suspensionResponse({
            accountApproved: true,
            accountSuspended: false,
            accountSuspendedAt: null,
            accountApprovedAt: SUSPENDED_AT,
        })));
        create();

        suspensionButtons()[0].click();
        await settle();
        const container = TestBed.inject(OverlayContainer).getContainerElement();
        expect(container.textContent).toContain(adminTr.teacherSuspension.unsuspend.confirmTitle);
        expect(container.querySelector('textarea')).toBeNull();
        overlayButton('confirm').click();
        await settle();

        expect(adminService.unsuspendTeacher).toHaveBeenCalledTimes(1);

        expect(adminService.unsuspendTeacher).toHaveBeenCalledWith(12);
        expect(adminService.suspendTeacher).not.toHaveBeenCalled();
        const row = component.rows()[0];
        expect(row.accountApproved).toBe(true);
        expect(row.accountSuspended).toBe(false);
        expect(row.suspensionReason).toBeNull();
        expect(fixture.nativeElement.querySelector('[data-testid="suspended-chip"]')).toBeNull();
        expect(suspensionButtons()[0].getAttribute('aria-label')).toBe(actionFor(adminTr.teacherSuspension.suspendActionFor));
    });

    it('suspend_Conflict_ShowsBackendMessage_CancelReloadsList', async () => {
        configure();
        const message = 'Öğretmen hesabı zaten askıda.';
        adminService.suspendTeacher.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409, error: { message, errorCode: 'TeacherAlreadySuspended' } })));
        create();
        adminService.getTeachers.mockClear();
        adminService.getTeachers.mockReturnValue(of(paged([suspendedTeacher()])));

        suspensionButtons()[0].click();
        await settle();
        typeReason(REASON);
        overlayButton('confirm').click();
        await settle();

        const container = TestBed.inject(OverlayContainer).getContainerElement();
        expect(container.querySelector('[data-testid="suspension-error"]')?.textContent).toContain(message);
        expect(overlayButton('confirm').textContent).toContain(adminTr.teacherSuspension.retry);
        expect(adminService.getTeachers).not.toHaveBeenCalled();
        overlayButton('cancel').click();
        await settle();

        expect(adminService.getTeachers).toHaveBeenCalledTimes(1);
        expect(component.rows()[0].accountSuspended).toBe(true);
    });

    it('suspend_RateLimited_ShowsRateLimitText', async () => {
        configure();
        adminService.suspendTeacher.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 429, error: 'plain' })));
        create();

        suspensionButtons()[0].click();
        await settle();
        typeReason(REASON);
        overlayButton('confirm').click();
        await settle();

        const container = TestBed.inject(OverlayContainer).getContainerElement();
        expect(container.querySelector('[data-testid="suspension-error"]')?.textContent).toContain(adminTr.teacherSuspension.errors.rateLimited);
    });

    it('suspension_DoubleClick_OpensSingleDialog', () => {
        configure();
        create();
        const dialog = fixture.debugElement.injector.get(MatDialog);
        const openSpy = vi.spyOn(dialog, 'open');

        const row = component.rows()[0];
        component.toggleSuspension(row);
        component.toggleSuspension(row);

        expect(openSpy).toHaveBeenCalledTimes(1);
    });
    // ── Okula bağla / okulu değiştir (issue #313) ────────────────────────────

    const schoolTexts = adminTr.teacherSchool;

    function schoolButtons(): HTMLButtonElement[] {
        return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button[data-testid="change-school"]'));
    }

    function schoolTooltips(): string[] {
        return fixture.debugElement
            .queryAll(By.css('button[data-testid="change-school"]'))
            .map((de) => de.injector.get(MatTooltip).message);
    }

    /** Açık dialog'da okul seçer (seçim bileşeninin kendi testi ayrı; burada sayfa entegrasyonu doğrulanır). */
    async function openSchoolDialogAndPick(pickSchool: School): Promise<void> {
        schoolButtons()[0].click();
        await settle();
        const dialog = fixture.debugElement.injector.get(MatDialog);
        const instance = dialog.openDialogs[0].componentInstance as AdminTeacherSchoolDialogComponent;
        instance.selectedSchoolId.set(pickSchool.id);
        instance.onSchoolSelected(pickSchool);
        await settle();
    }

    function schoolResponse(overrides: Partial<AdminTeacherSchoolResponse> = {}): AdminTeacherSchoolResponse {
        return { teacherId: 12, schoolId: 8, previousSchoolId: 5, changed: true, ...overrides };
    }

    it('schoolAction_SchooledAndUnassignedRows_ShowChangeAndAssignLabels', () => {
        configure();
        adminService.getTeachers.mockReturnValue(of(paged([
            teacher({ id: 1 }),
            teacher({ id: 2, schoolId: null, schoolName: null, isIndependentTutor: true, approvalStatus: 'Rejected' }),
        ])));
        create();

        const labels = schoolButtons().map((b) => b.getAttribute('aria-label'));
        expect(labels).toEqual([actionFor(schoolTexts.changeActionFor), actionFor(schoolTexts.assignActionFor)]);
        expect(schoolButtons().map((b) => b.getAttribute('aria-disabled'))).toEqual([null, null]);
        expect(schoolTooltips()).toEqual([schoolTexts.changeAction, schoolTexts.assignAction]);
    });

    it('schoolAction_SuspendedNotApprovedAndApprovedIndependent_DisabledWithReasonTooltip', () => {
        configure();
        adminService.getTeachers.mockReturnValue(of(paged([
            suspendedTeacher(),
            teacher({ id: 2, approvalStatus: 'Pending', accountApproved: false }),
            teacher({ id: 3, schoolId: null, schoolName: null, isIndependentTutor: true, approvalStatus: 'Approved' }),
        ])));
        create();
        const dialog = fixture.debugElement.injector.get(MatDialog);
        const openSpy = vi.spyOn(dialog, 'open');

        // Gizlenmez: üç satırda da buton var, devre dışı (disabledInteractive → aria-disabled) ve tooltip nedeni söyler.
        expect(schoolButtons().length).toBe(3);
        expect(schoolButtons().map((b) => b.getAttribute('aria-disabled'))).toEqual(['true', 'true', 'true']);
        expect(schoolTooltips()).toEqual([
            schoolTexts.blocked.suspended,
            schoolTexts.blocked.accountNotApproved,
            schoolTexts.blocked.approvedIndependent,
        ]);
        expect(component.rows().map((r) => r.schoolBlockedReason)).toEqual([
            'suspended',
            'accountNotApproved',
            'approvedIndependent',
        ]);

        schoolButtons().forEach((b) => b.click());
        component.rows().forEach((r) => component.changeSchool(r));

        expect(openSpy).not.toHaveBeenCalled();
        expect(component.schoolDialogOpen()).toBe(false);
    });

    it('changeSchool_OpensDialogWithRowIdentityAndSendsNoRequestYet', async () => {
        configure();
        create();
        const dialog = fixture.debugElement.injector.get(MatDialog);
        const openSpy = vi.spyOn(dialog, 'open');

        schoolButtons()[0].click();
        await settle();

        expect(vi.mocked(openSpy).mock.lastCall![0]).toBe(AdminTeacherSchoolDialogComponent);
        const config = vi.mocked(openSpy).mock.lastCall![1];
        expect(config?.data).toEqual({
            teacherId: 12,
            displayName: 'Ayşe Yılmaz',
            currentSchoolId: 5,
            currentSchoolName: 'Ankara Lisesi',
            independent: false,
            accountApproved: true,
        });
        expect(config?.disableClose).toBe(true);
        expect(overlay().textContent).toContain(schoolTexts.changeTitle);
        expect(overlay().textContent).toContain(schoolTexts.effectPendingRequestApproved);
        expect(overlayButton('confirm').disabled).toBe(true);
        expect(adminService.changeTeacherSchool).not.toHaveBeenCalled();
    });

    it('changeSchool_ChangedTrue_ShowsChangedSnackbarAndReloadsList', async () => {
        configure();
        adminService.changeTeacherSchool.mockReturnValue(of(schoolResponse()));
        create();
        adminService.getTeachers.mockClear();
        adminService.getTeachers.mockReturnValue(of(paged([teacher({ schoolId: 8, schoolName: 'İzmir Lisesi' })], 45)));

        await openSchoolDialogAndPick(izmir);
        overlayButton('confirm').click();
        await settle();

        expect(adminService.changeTeacherSchool).toHaveBeenCalledTimes(1);

        expect(adminService.changeTeacherSchool).toHaveBeenCalledWith(12, 8);
        expect(adminService.getTeachers).toHaveBeenCalledTimes(1);
        expect(cellTexts('school')).toEqual(['İzmir Lisesi']);
        expect(component.rows()[0].schoolId).toBe(8);
        expect(snackBar.open).toHaveBeenCalledTimes(1);
        expect(snackBar.open).toHaveBeenCalledWith(schoolTexts.success.changed.replace('{{name}}', 'Ayşe Yılmaz').replace('{{school}}', 'İzmir Lisesi'), schoolTexts.close, expect.any(Object));
        expect(component.schoolDialogOpen()).toBe(false);
    });

    it('assignSchool_UnapprovedIndependentTeacher_ShowsNoteAssignedSnackbarAndReloads', async () => {
        configure();
        const independent = teacher({ schoolId: null, schoolName: null, isIndependentTutor: true, approvalStatus: 'Rejected' });
        adminService.getTeachers.mockReturnValue(of(paged([independent])));
        adminService.changeTeacherSchool.mockReturnValue(of(schoolResponse({ previousSchoolId: null })));
        create();
        expect(cellTexts('school')).toEqual([adminTr.teachers.independent]);
        adminService.getTeachers.mockClear();
        adminService.getTeachers.mockReturnValue(of(paged([{ ...independent, schoolId: 8, schoolName: 'İzmir Lisesi' }])));

        await openSchoolDialogAndPick(izmir);
        expect(overlay().querySelector('[data-testid="independent-note"]')?.textContent).toContain(schoolTexts.independentNote);
        expect(overlay().textContent).toContain(schoolTexts.effectPendingRequest);
        expect(overlay().textContent).not.toContain(schoolTexts.effectPendingRequestApproved);
        overlayButton('confirm').click();
        await settle();

        expect(adminService.getTeachers).toHaveBeenCalledTimes(1);
        expect(component.rows()[0].schoolId).toBe(8);
        expect(component.rows()[0].independent).toBe(true);
        expect(snackBar.open).toHaveBeenCalledTimes(1);
        expect(snackBar.open).toHaveBeenCalledWith(schoolTexts.success.assigned.replace('{{name}}', 'Ayşe Yılmaz').replace('{{school}}', 'İzmir Lisesi'), schoolTexts.close, expect.any(Object));
        expect(schoolButtons()[0].getAttribute('aria-label')).toBe(actionFor(schoolTexts.changeActionFor));
    });

    it('changeSchool_ChangedFalse_ShowsAlreadyInSchoolSnackbarWithoutReload', async () => {
        configure();
        adminService.changeTeacherSchool.mockReturnValue(of(schoolResponse({ schoolId: 5, previousSchoolId: 5, changed: false })));
        create();
        adminService.getTeachers.mockClear();

        await openSchoolDialogAndPick(izmir);
        overlayButton('confirm').click();
        await settle();

        expect(snackBar.open).toHaveBeenCalledTimes(1);

        expect(snackBar.open).toHaveBeenCalledWith(schoolTexts.success.unchanged.replace('{{name}}', 'Ayşe Yılmaz'), schoolTexts.close, expect.any(Object));
        expect(adminService.getTeachers).not.toHaveBeenCalled();
    });

    it('changeSchool_ProfileCacheStale_ShowsWarningInsteadOfSuccess', async () => {
        configure();
        adminService.changeTeacherSchool.mockReturnValue(of(schoolResponse({ profileCacheStale: true })));
        create();
        adminService.getTeachers.mockClear();

        await openSchoolDialogAndPick(izmir);
        overlayButton('confirm').click();
        await settle();

        expect(snackBar.open).toHaveBeenCalledTimes(1);

        expect(snackBar.open).toHaveBeenCalledWith(schoolTexts.success.cacheStale, schoolTexts.close, expect.objectContaining({ duration: 8000 }));
        expect(adminService.getTeachers).toHaveBeenCalledTimes(1);
    });

    it('changeSchool_BackendConflictMessage409_ShowsServerMessageAndReloadButtonReloadsList', async () => {
        configure();
        const message = 'Onaylı bağımsız öğretmen okula bağlanamaz.';
        adminService.changeTeacherSchool.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409, error: { message } })));
        create();
        adminService.getTeachers.mockClear();

        await openSchoolDialogAndPick(izmir);
        overlayButton('confirm').click();
        await settle();

        expect(overlay().querySelector('[data-testid="school-error"]')?.textContent).toContain(message);
        expect(snackBar.open).toHaveBeenCalledWith(message, schoolTexts.close, expect.any(Object));
        expect(adminService.getTeachers).not.toHaveBeenCalled();

        overlayButton('reload').click();
        await settle();

        expect(adminService.changeTeacherSchool).toHaveBeenCalledTimes(1);
        expect(adminService.getTeachers).toHaveBeenCalledTimes(1);
        expect(component.schoolDialogOpen()).toBe(false);
    });

    it('changeSchool_Upstream502ThenCancel_ReloadsListSoRowIsNotStale', async () => {
        configure();
        adminService.changeTeacherSchool.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 502 })));
        create();
        adminService.getTeachers.mockClear();

        await openSchoolDialogAndPick(izmir);
        overlayButton('confirm').click();
        await settle();
        expect(overlay().querySelector('[data-testid="school-error"]')?.textContent).toContain(schoolTexts.errors.upstream);

        overlayButton('cancel').click();
        await settle();

        expect(adminService.getTeachers).toHaveBeenCalledTimes(1);
        expect(cellTexts('school')).toEqual(['Ankara Lisesi']);
    });

    it('changeSchool_DoubleClick_OpensSingleDialog', () => {
        configure();
        create();
        const dialog = fixture.debugElement.injector.get(MatDialog);
        const openSpy = vi.spyOn(dialog, 'open');

        const row = component.rows()[0];
        component.changeSchool(row);
        component.changeSchool(row);

        expect(openSpy).toHaveBeenCalledTimes(1);
    });
});
