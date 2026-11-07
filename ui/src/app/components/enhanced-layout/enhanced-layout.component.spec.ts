import { BookingService } from '../../services/booking.service';
import { OpenTextReviewService } from '../../services/open-text-review.service';
import { SidenavService } from '../../services/sidenav.service';
import type { Mock } from "vitest";
import { Component, WritableSignal, signal, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideHttpClient, withXhr } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router, Routes } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { BreakpointObserver } from '@angular/cdk/layout';
import { BehaviorSubject, NEVER, of, Subject, throwError } from 'rxjs';

import { EnhancedLayoutComponent } from './enhanced-layout.component';
import { AuthService, UserProfile } from '../../services/auth.service';
import { SignalRService } from '../../services/signalr.service';
import { WorksheetAccessRequestService } from '../../services/worksheet-access-request.service';
import { StudentSchoolRequestService } from '../../services/student-school-request.service';
import { AdminPendingService } from '../../services/admin-pending.service';
import { resolveActiveMenuItemId } from './active-menu-item';
import { NotificationService } from '../../services/notification.service';
import { DirectMessageService } from '../../services/direct-message.service';
import { UserThemeService } from '../../services/user-theme.service';
import { ThemeConfigService } from '../../services/theme-config.service';
import { ParentStatusStore } from '../../services/parent-status.store';
import { awaitingStatusOf, parentStatusOf } from '../../shared/testing/parent-status-testing';
import { routes } from '../../app.routes';
import { adminGuard } from '../../shared/guards/admin.guard';
import { studentGuard } from '../../shared/guards/student.guard';
import { settingsGuard } from '../../shared/guards/settings.guard';
import { independentTeacherGuard } from '../../shared/guards/independent-teacher.guard';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';

/** Issue #106: DM rozet servisi stub'ı (HttpClient gerektirmesin). */
function directMessageStub(count = 0) {
    const unreadCount = signal(count);
    return {
        unreadCount: unreadCount.asReadonly(),
        refreshUnreadCount: vi.fn().mockName('refreshUnreadCount').mockReturnValue(of(count)),
        resetUnreadCount: () => unreadCount.set(0),
    };
}

/**
 * Issue #154: admin menüsündeki "Öğretmenler"/"Öğrenciler" girişleri.
 * Menü rol filtresi sınıf alanında (`visibleMenuItems`) hesaplandığı için komponent render
 * edilmeden (ngOnInit / SignalR / refresh akışı tetiklenmeden) doğrulanır.
 */
describe('EnhancedLayoutComponent menu (issue #154)', () => {
    const ADMIN_LIST_ENTRIES = [
        { id: 'admin-teachers', route: '/admin/teachers', labelKey: 'menu.adminTeachers', tr: 'Öğretmenler' },
        { id: 'admin-students', route: '/admin/students', labelKey: 'menu.adminStudents', tr: 'Öğrenciler' },
    ];

    function create(roles: string[], unapprovedTeacher = false): EnhancedLayoutComponent {
        const authStub: Partial<AuthService> = {
            hasRealmRole: (role: string) => roles.includes(role),
            isAuthenticated: () => of(true),
            isUnapprovedTeacher: signal(unapprovedTeacher),
            user: signal(null),
        };
        TestBed.configureTestingModule({
            imports: [EnhancedLayoutComponent, translocoTestingModule()],
            providers: [
                provideRouter([]),
                { provide: AuthService, useValue: authStub },
                { provide: SignalRService, useValue: { accessRequestUpdates$: new Subject() } },
                { provide: WorksheetAccessRequestService, useValue: { pendingCount: signal(0) } },
                { provide: StudentSchoolRequestService, useValue: { pendingCount: signal(0), refreshPendingCount: () => of(0) } },
                { provide: DirectMessageService, useValue: directMessageStub() },
                { provide: NotificationService, useValue: { unreadCount: signal(0) } },
                { provide: BookingService, useValue: { pendingCount: signal(0), refreshPendingCount: () => of(0) } },
                { provide: OpenTextReviewService, useValue: { pendingCount: signal(0), refreshPendingCount: () => of(0) } },
                { provide: UserThemeService, useValue: {} },
                { provide: ThemeConfigService, useValue: {} },
            ],
        });
        return TestBed.createComponent(EnhancedLayoutComponent).componentInstance;
    }

    it('visibleMenuItems_AdminRole_ContainsTeachersAndStudentsEntriesWithListRoutes', () => {
        const component = create(['Admin']);

        for (const entry of ADMIN_LIST_ENTRIES) {
            const item = component.visibleMenuItems().find((i) => i.id === entry.id);
            expect(item, entry.id).toBeDefined();
            expect(item?.type, entry.id).toBe('menu');
            expect(item?.route, entry.id).toBe(entry.route);
            expect(item?.labelKey, entry.id).toBe(entry.labelKey);
            expect(item?.icon, entry.id).toBeTruthy();
        }
    });

    for (const role of ['Teacher', 'Student']) {
        it(`visibleMenuItems_${role}Role_DoesNotContainAdminListEntries`, () => {
            const component = create([role]);
            const routesShown = component.visibleMenuItems().map((i) => i.route);

            for (const entry of ADMIN_LIST_ENTRIES) {
                expect(routesShown, role).not.toContain(entry.route);
            }
        });
    }

    it('menuLabels_AdminListEntries_ResolveToTurkishTexts', () => {
        create(['Admin']);
        const transloco = TestBed.inject(TranslocoService);

        for (const entry of ADMIN_LIST_ENTRIES) {
            // Şablon `layout` prefix'i ile çevirir: t(item.labelKey)
            expect(transloco.translate(`layout.${entry.labelKey}`)).toBe(entry.tr);
        }
    });

    it('navigateTo_AdminListEntries_NavigatesToListRoute', () => {
        const component = create(['Admin']);
        const router = TestBed.inject(Router);
        const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

        for (const entry of ADMIN_LIST_ENTRIES) {
            component.navigateTo(entry.route);
            expect(navigateSpy).toHaveBeenCalledWith([entry.route], {});
        }
    });

    it('routes_AdminListEntryRoutes_ExistInAppRoutes', () => {
        const layoutRoute = routes.find((r) => Array.isArray(r.children));
        const childPaths = layoutRoute?.children?.map((r) => `/${r.path}`) ?? [];

        for (const entry of ADMIN_LIST_ENTRIES) {
            expect(childPaths).toContain(entry.route);
        }
    });
    it('visibleMenuItems_StudyLinksEntry_OnlyForTeacherWithTranslatedLabel (issue #61)', () => {
        const component = create(['Teacher']);
        const item = component.visibleSectionTabs().flat().find((i) => i.id === 'tab-study-links');
        expect(item?.route).toBe('/study-links');
        expect(TestBed.inject(TranslocoService).translate('layout.menu.studyLinks')).toBe('Çalışma Linkleri');

        TestBed.resetTestingModule();
        const student = create(['Student']);
        expect(student.visibleMenuItems().map((i) => i.route)).not.toContain('/study-links');
    });

    it('visibleMenuItems_QuestionTransfer_OnlyForAdmin (issue #365)', () => {
        const admin = create(['Admin']);
        const item = admin.visibleMenuItems().find((i) => i.id === 'questiontransfer');
        expect(item?.route).toBe('/question-transfer');
        expect(item?.roles).toEqual(['Admin']);

        for (const role of ['Teacher', 'Student']) {
            TestBed.resetTestingModule();
            const other = create([role]);
            expect(other.visibleMenuItems().map((i) => i.route), role).not.toContain('/question-transfer');
        }
    });

    it('routes_QuestionTransfer_UsesAdminGuard (issue #365)', () => {
        const children = routes.find((r) => Array.isArray(r.children))?.children ?? [];
        const route = children.find((r) => r.path === 'question-transfer');
        expect(route?.canActivate).toContain(adminGuard);
    });

    // ── Issue #287: onaysız öğretmen menüsü ─────────────────────────────────────

    const TEACHER_ONLY_ROUTES = [
        '/dashboard',
        '/tests',
        '/exam',
        '/study-pages',
        '/study-links',
        '/availability',
        '/booking-requests',
        '/assignment-permission-requests',
        '/student-school-requests',
        '/my-calendar',
    ];

    it('visibleMenuItems_UnapprovedTeacher_HidesTeacherItemsAndShowsSingleStatusEntry', () => {
        const component = create(['Teacher'], true);
        const items = component.visibleMenuItems();
        const routesShown = items.map((i) => i.route);

        for (const route of TEACHER_ONLY_ROUTES) {
            expect(routesShown, route).not.toContain(route);
        }
        const status = items.filter((i) => i.id === 'teacher-approval-status');
        expect(status.length).toBe(1);
        expect(status[0].route).toBe('/teacher-approval-pending');
        // Rolsüz (herkese açık) öğeler kalır; baştaki/sondaki ayırıcılar temizlenir.
        // Issue #713: rozet sayfası artık yalnız öğrencinin menüsünde.
        expect(routesShown).not.toContain('/certificates');
        // Issue #373: /student-profile yalnız öğrenciye; #417: onay bekleyen öğretmen de Ayarlar'a (/settings) ulaşır.
        expect(routesShown).not.toContain('/student-profile');
        expect(routesShown).toContain('/settings');
        expect(items[0].type).toBe('menu');
        expect(items[items.length - 1].type).toBe('menu');
    });

    it('visibleMenuItems_UnapprovedTeacher_KeepsTutorProfileApplicationForm', () => {
        const component = create(['Teacher'], true);
        expect(component.visibleMenuItems().map((i) => i.route)).toContain('/tutor-profile');

        TestBed.resetTestingModule();
        const student = create(['Student'], false);
        expect(student.visibleMenuItems().map((i) => i.route)).not.toContain('/tutor-profile');
    });

    it('visibleBottomNavItems_UnapprovedTeacher_StatusEntryInsteadOfDashboardAndExams', () => {
        const component = create(['Teacher'], true);
        const routesShown = component.visibleBottomNavItems().map((i) => i.route);

        expect(routesShown).toContain('/teacher-approval-pending');
        expect(routesShown).not.toContain('/dashboard');
        expect(routesShown).not.toContain('/tests');
    });

    it('visibleMenuItems_ApprovedTeacher_ShowsTeacherItemsWithoutStatusEntry', () => {
        const component = create(['Teacher'], false);
        const ids = component.visibleMenuItems().map((i) => i.id);

        expect(ids).toContain('exams');
        expect(ids).toContain('study-pages');
        expect(ids).not.toContain('teacher-approval-status');
        expect(component.visibleBottomNavItems().map((i) => i.id)).not.toContain('teacher-approval-status');
    });

    for (const role of ['Student', 'Admin']) {
        it(`visibleMenuItems_${role}_NeverShowsStatusEntry`, () => {
            const component = create([role], false);
            expect(component.visibleMenuItems().map((i) => i.id)).not.toContain('teacher-approval-status');
        });
    }

    it('visibleMenuItems_ReactsWhenApprovalChanges', () => {
        const unapproved = signal(true);
        const authStub: Partial<AuthService> = {
            hasRealmRole: (role: string) => role === 'Teacher',
            isAuthenticated: () => of(true),
            isUnapprovedTeacher: unapproved,
            user: signal(null),
        };
        TestBed.configureTestingModule({
            imports: [EnhancedLayoutComponent, translocoTestingModule()],
            providers: [
                provideRouter([]),
                { provide: AuthService, useValue: authStub },
                { provide: SignalRService, useValue: { accessRequestUpdates$: new Subject() } },
                { provide: WorksheetAccessRequestService, useValue: { pendingCount: signal(0) } },
                { provide: StudentSchoolRequestService, useValue: { pendingCount: signal(0), refreshPendingCount: () => of(0) } },
                { provide: DirectMessageService, useValue: directMessageStub() },
                { provide: NotificationService, useValue: { unreadCount: signal(0) } },
                { provide: BookingService, useValue: { pendingCount: signal(0), refreshPendingCount: () => of(0) } },
                { provide: OpenTextReviewService, useValue: { pendingCount: signal(0), refreshPendingCount: () => of(0) } },
                { provide: UserThemeService, useValue: {} },
                { provide: ThemeConfigService, useValue: {} },
            ],
        });
        const component = TestBed.createComponent(EnhancedLayoutComponent).componentInstance;
        expect(component.visibleMenuItems().map((i) => i.id)).not.toContain('exams');

        unapproved.set(false);

        expect(component.visibleMenuItems().map((i) => i.id)).toContain('exams');
    });

    it('menuLabel_TeacherApprovalStatus_TranslatedTrAndEn', () => {
        create(['Teacher'], true);
        const transloco = TestBed.inject(TranslocoService);
        expect(transloco.translate('layout.menu.teacherApprovalStatus')).toBe('Başvuru Durumu');
        expect(transloco.translate('layout.menu.teacherApprovalStatus', {}, 'en')).toBe('Application Status');
    });

    it('routes_TeacherApprovalPendingRoute_Exists', () => {
        const layoutRoute = routes.find((r) => Array.isArray(r.children));
        expect(layoutRoute?.children?.map((r) => r.path)).toContain('teacher-approval-pending');
    });

    // ── Issue #361: bekleyen öğrenci okul başvuruları ───────────────────────────

    it('visibleMenuItems_StudentSchoolRequests_TeacherAndAdminOnly (issue #361)', () => {
        expect(create(['Teacher']).visibleSectionTabs().flat().map((i) => i.route)).toContain('/student-school-requests');

        TestBed.resetTestingModule();
        // #861: admin'de "Bekleyenler" sekmesi.
        const admin = create(['Admin']).visibleSectionTabs().flat().map((i) => i.route);
        expect(admin).toContain('/admin/student-school-requests');
        expect(admin).not.toContain('/student-school-requests');

        TestBed.resetTestingModule();
        const student = create(['Student']).visibleMenuItems().map((i) => i.route);
        expect(student).not.toContain('/student-school-requests');
        expect(student).not.toContain('/admin/student-school-requests');
    });

    it('routes_StudentSchoolRequests_GuardedByRole (issue #361)', () => {
        const children = routes.find((r) => Array.isArray(r.children))?.children ?? [];
        const teacherRoute = children.find((r) => r.path === 'student-school-requests');
        const adminRoute = children.find((r) => r.path === 'admin/student-school-requests');
        expect(teacherRoute?.canActivate?.length).toBe(3);
        expect(adminRoute?.canActivate).toContain(adminGuard);
    });

    it('isParentLocked_AwaitingActivation_LocksMessagesAndTutorsOnly (issue #845)', () => {
        const component = create(['Student']);
        const store = TestBed.inject(ParentStatusStore);
        expect(component.isParentLocked('teacher-messages')).toBe(false);

        store.set(awaitingStatusOf());
        expect(component.isParentLocked('teacher-messages')).toBe(true);
        expect(component.isParentLocked('tutors')).toBe(true);
        expect(component.isParentLocked('exams')).toBe(false);

        store.set(parentStatusOf({ mode: 'Ok' }));
        expect(component.isParentLocked('teacher-messages')).toBe(false);
        expect(TestBed.inject(TranslocoService).translate('layout.menuLocked')).toBe('Velin onaylayınca açılır');
    });

    it('menuBadgeCount_StudentSchoolRequests_UsesPendingCount (issue #361)', () => {
        const component = create(['Teacher']);
        const service = TestBed.inject(StudentSchoolRequestService) as unknown as {
            pendingCount: WritableSignal<number>;
        };
        service.pendingCount.set(3);
        expect(component.menuBadgeCount('student-school-requests')).toBe(3);
        expect(component.menuBadgeCount('access-requests')).toBe(0);
    });

    it('menuBadgeCount_Requests_SumsAllFourTypes (issue #723)', () => {
        const component = create(['Teacher']);
        const set = (token: unknown, value: number) =>
            (TestBed.inject(token as never) as unknown as { pendingCount: WritableSignal<number> }).pendingCount.set(value);
        set(WorksheetAccessRequestService, 1);
        set(StudentSchoolRequestService, 2);
        set(BookingService, 3);
        set(OpenTextReviewService, 4);

        expect(component.menuBadgeCount('teacher-requests')).toBe(10);
        expect(component.menuBadgeCount('tab-booking-requests')).toBe(3);
        expect(component.menuBadgeCount('tab-open-text-reviews')).toBe(4);
    });

    it('menuLabel_StudentSchoolRequests_TranslatedTrAndEn (issue #361)', () => {
        create(['Teacher']);
        const transloco = TestBed.inject(TranslocoService);
        expect(transloco.translate('layout.menu.studentSchoolRequests')).toBe('Öğrenci Okul Başvuruları');
        expect(transloco.translate('layout.menu.studentSchoolRequests', {}, 'en')).toBe('Student School Requests');
    });
});

/**
 * Issue #146: zil butonundaki okunmamış bildirim sayacı. Komponent render edilir (ngOnInit çalışır);
 * profil yenileme akışı `NEVER` ile askıda bırakılır, SignalR yalnızca `notificationsChanged$` akışı sağlar.
 */
describe('EnhancedLayoutComponent notification badge (issue #146)', () => {
    beforeEach(() => {
        vi.useFakeTimers();
    });
    afterEach(() => {
        vi.useRealTimers();
    });
    let fixture: ComponentFixture<EnhancedLayoutComponent>;
    let notificationsChanged$: Subject<void>;
    let authenticated$: BehaviorSubject<boolean>;
    let unreadCount: WritableSignal<number>;
    let refreshUnreadCount: Mock;
    let navigateSpy: Mock;

    function setup(isAuthenticated = true, roles: string[] = [], desktop = false): void {
        notificationsChanged$ = new Subject<void>();
        authenticated$ = new BehaviorSubject<boolean>(isAuthenticated);
        unreadCount = signal(0);
        refreshUnreadCount = vi.fn().mockName('refreshUnreadCount').mockImplementation(() => of(unreadCount()));

        const authStub: Partial<AuthService> = {
            hasRealmRole: (role: string) => roles.includes(role),
            isAuthenticated: () => authenticated$.asObservable(),
            isUnapprovedTeacher: signal(false),
            user: signal(null),
            isCachedUserCurrent: () => true,
            refreshProfile: () => NEVER,
        };
        TestBed.configureTestingModule({
            imports: [EnhancedLayoutComponent, NoopAnimationsModule, translocoTestingModule()],
            providers: [
                // Dil seçici (LocalePreferenceService) HttpClient ister; bu testte istek beklenmez.
                provideHttpClient(withXhr()),
                provideHttpClientTesting(),
                provideRouter([]),
                { provide: AuthService, useValue: authStub },
                {
                    provide: SignalRService,
                    useValue: {
                        startConnection: () => undefined,
                        accessRequestUpdates$: new Subject(),
                        notificationsChanged$: notificationsChanged$.asObservable(),
                    },
                },
                { provide: WorksheetAccessRequestService, useValue: { pendingCount: signal(0) } },
                { provide: StudentSchoolRequestService, useValue: { pendingCount: signal(0), refreshPendingCount: () => of(0) } },
                { provide: DirectMessageService, useValue: directMessageStub() },
                {
                    provide: NotificationService,
                    useValue: {
                        unreadCount: unreadCount.asReadonly(),
                        refreshUnreadCount,
                        resetUnreadCount: () => unreadCount.set(0),
                    },
                },
                { provide: UserThemeService, useValue: { userTheme$: of(null) } },
                { provide: ThemeConfigService, useValue: {} },
            ],
        });
        if (desktop) {
            // Headless pencere dar: masaüstü araç çubuğunu çizdirmek için mobil kırılım eşleşmesin.
            TestBed.overrideProvider(BreakpointObserver, { useValue: { observe: () => of({ matches: false, breakpoints: {} }) } });
        }
        navigateSpy = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
        fixture = TestBed.createComponent(EnhancedLayoutComponent);
    }

    function bellButton(): HTMLButtonElement | null {
        return (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.notification-bell');
    }

    function badgeText(): string {
        return bellButton()?.querySelector('.mat-badge-content')?.textContent?.trim() ?? '';
    }

    afterEach(() => localStorage.removeItem('user'));

    it('toolbar_ParentOnly_HidesNewestPopularAndWorksheetSearch (#421)', () => {
        setup(true, ['Parent'], true);
        fixture.detectChanges();
        const el = fixture.nativeElement as HTMLElement;
        expect(fixture.componentInstance.isParentOnly).toBe(true);
        expect(el.querySelector('mat-toolbar .section-nav')).not.toBeNull(); // masaüstü araç çubuğu çizildi
        expect(el.querySelector('[data-test="toolbar-search"]')).toBeNull();
    });

    it('toolbar_ParentWithAnotherRoleOrStudent_KeepsNewestPopularAndSearch (#421)', () => {
        setup(true, ['Parent', 'Teacher'], true);
        fixture.detectChanges();
        const el = fixture.nativeElement as HTMLElement;
        expect(fixture.componentInstance.isParentOnly).toBe(false);
        expect(el.querySelector('[data-test="toolbar-search"]')).not.toBeNull();
    });

    it('init_Authenticated_LoadsUnreadCountOnceAndShowsBadge', () => {
        setup();
        unreadCount.set(3);
        fixture.detectChanges();

        expect(refreshUnreadCount).toHaveBeenCalledTimes(1);
        expect(bellButton()).not.toBeNull();
        expect(badgeText()).toBe('3');
        expect(bellButton()?.querySelector('.mat-badge-hidden')).toBeNull();
        expect(bellButton()?.getAttribute('aria-label')).toBe('Bildirimler (3 okunmamış)');
    });

    it('zeroUnread_HidesBadge', () => {
        setup();
        fixture.detectChanges();

        expect(bellButton()?.querySelector('.mat-badge-hidden')).not.toBeNull();
        expect(bellButton()?.getAttribute('aria-label')).toBe('Bildirimler');
    });

    it('moreThan99Unread_ShowsCappedBadge', () => {
        setup();
        unreadCount.set(150);
        fixture.detectChanges();

        expect(badgeText()).toBe('99+');
    });

    it('notAuthenticated_DoesNotRequestUnreadCount', () => {
        setup(false);
        fixture.detectChanges();

        expect(refreshUnreadCount).not.toHaveBeenCalled();
    });

    it('logout_ResetsCountAndLoginAgainRefetches', () => {
        setup();
        unreadCount.set(4);
        fixture.detectChanges();
        expect(badgeText()).toBe('4');
        expect(refreshUnreadCount).toHaveBeenCalledTimes(1);

        authenticated$.next(false);
        fixture.detectChanges();
        expect(unreadCount()).toBe(0);

        // Aynı değerin tekrar yayılması ek istek üretmez.
        authenticated$.next(false);
        authenticated$.next(true);
        authenticated$.next(true);
        expect(refreshUnreadCount).toHaveBeenCalledTimes(2);
    });

    it('logout_StopsRefreshingOnHubPushes', async () => {
        setup();
        fixture.detectChanges();
        authenticated$.next(false);

        notificationsChanged$.next();
        await vi.advanceTimersByTimeAsync(EnhancedLayoutComponent.NOTIFICATION_REFRESH_DEBOUNCE_MS);

        expect(refreshUnreadCount).toHaveBeenCalledTimes(1);
        fixture.destroy();
        await vi.runAllTimersAsync();
    });

    it('hubPushes_AreDebouncedIntoSingleRefresh', async () => {
        setup();
        fixture.detectChanges();
        expect(refreshUnreadCount).toHaveBeenCalledTimes(1);

        notificationsChanged$.next();
        notificationsChanged$.next();
        notificationsChanged$.next();
        await vi.advanceTimersByTimeAsync(EnhancedLayoutComponent.NOTIFICATION_REFRESH_DEBOUNCE_MS - 1);
        expect(refreshUnreadCount).toHaveBeenCalledTimes(1);

        await vi.advanceTimersByTimeAsync(1);
        expect(refreshUnreadCount).toHaveBeenCalledTimes(2);

        fixture.destroy();
        await vi.runAllTimersAsync();
    });

    it('refreshError_IsSwallowedAndLaterPushStillRefreshes', async () => {
        setup();
        refreshUnreadCount.mockReturnValueOnce(throwError(() => new Error('down'))).mockReturnValueOnce(of(2));
        fixture.detectChanges();

        notificationsChanged$.next();
        await vi.advanceTimersByTimeAsync(EnhancedLayoutComponent.NOTIFICATION_REFRESH_DEBOUNCE_MS);
        expect(refreshUnreadCount).toHaveBeenCalledTimes(2);

        fixture.destroy();
        await vi.runAllTimersAsync();
    });

    it('bellClick_NavigatesToNotifications', () => {
        setup();
        fixture.detectChanges();

        bellButton()?.click();

        expect(navigateSpy).toHaveBeenCalledWith(['/notifications']);
    });

    it('routes_NotificationsRoute_ExistsWithAuthGuard', () => {
        const layoutRoute = routes.find((r) => Array.isArray(r.children));
        const route = layoutRoute?.children?.find((r) => r.path === 'notifications');
        expect(route).toBeDefined();
        expect(route?.canActivate?.length).toBeGreaterThan(0);
    });
});

/** Issue #106: DM menü girişleri ve okunmamış rozeti. Render edilmeden sınıf alanları doğrulanır. */
describe('EnhancedLayoutComponent direct messages menu (issue #106)', () => {
    function create(roles: string[], dmCount = 0): EnhancedLayoutComponent {
        const authStub: Partial<AuthService> = {
            hasRealmRole: (role: string) => roles.includes(role),
            isAuthenticated: () => of(true),
            isUnapprovedTeacher: signal(false),
            user: signal(null),
        };
        TestBed.configureTestingModule({
            imports: [EnhancedLayoutComponent, translocoTestingModule()],
            providers: [
                provideRouter([]),
                { provide: AuthService, useValue: authStub },
                { provide: SignalRService, useValue: { accessRequestUpdates$: new Subject() } },
                { provide: WorksheetAccessRequestService, useValue: { pendingCount: signal(2) } },
                { provide: StudentSchoolRequestService, useValue: { pendingCount: signal(0), refreshPendingCount: () => of(0) } },
                { provide: DirectMessageService, useValue: directMessageStub(dmCount) },
                { provide: NotificationService, useValue: { unreadCount: signal(0) } },
                { provide: BookingService, useValue: { pendingCount: signal(0), refreshPendingCount: () => of(0) } },
                { provide: OpenTextReviewService, useValue: { pendingCount: signal(0), refreshPendingCount: () => of(0) } },
                { provide: UserThemeService, useValue: {} },
                { provide: ThemeConfigService, useValue: {} },
            ],
        });
        return TestBed.createComponent(EnhancedLayoutComponent).componentInstance;
    }

    it('student_SeesTeacherMessagesEntry_NotInbox', () => {
        const ids = create(['Student']).visibleMenuItems().map((i) => i.id);
        expect(ids).toContain('teacher-messages');
        expect(ids).not.toContain('student-messages');
    });

    it('teacher_SeesStudentMessagesEntry_NotStudentPage', () => {
        const ids = create(['Teacher']).visibleMenuItems().map((i) => i.id);
        expect(ids).toContain('student-messages');
        expect(ids).not.toContain('teacher-messages');
    });

    it('menuBadgeCount_DmItemsUseDmCount_AccessRequestsKeepOwnCount', () => {
        const component = create(['Student'], 3);
        expect(component.menuBadgeCount('teacher-messages')).toBe(3);
        expect(component.menuBadgeCount('student-messages')).toBe(3);
        expect(component.menuBadgeCount('access-requests')).toBe(2);
        expect(component.menuBadgeCount('dashboard')).toBe(0);
    });

    it('isDirectMessageItem_And_BadgeDescriptionKey', () => {
        const component = create(['Student'], 2);
        expect(component.isDirectMessageItem('teacher-messages')).toBe(true);
        expect(component.isDirectMessageItem('access-requests')).toBe(false);
        const transloco = TestBed.inject(TranslocoService);
        expect(transloco.translate('layout.dmUnreadBadge', { count: 2 })).toBe('2 okunmamış konuşma');
    });

    it('menuLabels_TranslatedTrAndEn', () => {
        create(['Student']);
        const transloco = TestBed.inject(TranslocoService);
        expect(transloco.translate('layout.menu.teacherMessages')).toBe('Mesajlar');
        expect(transloco.translate('layout.menu.studentMessages', {}, 'en')).toBe('Messages');
    });

    it('routes_DirectMessagePagesExist', () => {
        const layoutRoute = routes.find((r) => Array.isArray(r.children));
        const paths = layoutRoute?.children?.map((r) => r.path);
        expect(paths).toContain('teacher-messages');
        expect(paths).toContain('student-messages');
    });
});

/** Issue #106 dilim b: SignalR `DirectMessageReceived` push'u sidenav DM rozetini gerçek zamanlı tazeler. */
describe('EnhancedLayoutComponent DM badge push (issue #106 b)', () => {
    beforeEach(() => {
        vi.useFakeTimers();
    });
    afterEach(() => {
        vi.useRealTimers();
    });
    let fixture: ComponentFixture<EnhancedLayoutComponent>;
    let directMessageReceived$: Subject<{
        conversationId: number;
        senderRole: 'Student' | 'Teacher';
    }>;
    let dmRefresh: Mock;

    function setup(role: 'Student' | 'Teacher'): void {
        directMessageReceived$ = new Subject();
        dmRefresh = vi.fn().mockName('refreshUnreadCount').mockReturnValue(of(1));
        const authStub: Partial<AuthService> = {
            hasRealmRole: (r: string) => r === role,
            isAuthenticated: () => of(true),
            isUnapprovedTeacher: signal(false),
            user: signal(null),
            isCachedUserCurrent: () => true,
            refreshProfile: () => NEVER,
        };
        TestBed.configureTestingModule({
            imports: [EnhancedLayoutComponent, NoopAnimationsModule, translocoTestingModule()],
            providers: [
                provideHttpClient(withXhr()),
                provideHttpClientTesting(),
                provideRouter([]),
                { provide: AuthService, useValue: authStub },
                {
                    provide: SignalRService,
                    useValue: {
                        startConnection: () => undefined,
                        accessRequestUpdates$: new Subject(),
                        notificationsChanged$: new Subject<void>().asObservable(),
                        directMessageReceived$: directMessageReceived$.asObservable(),
                    },
                },
                { provide: WorksheetAccessRequestService, useValue: { pendingCount: signal(0), refreshPendingCount: () => of(0) } },
                { provide: StudentSchoolRequestService, useValue: { pendingCount: signal(0), refreshPendingCount: () => of(0) } },
                {
                    provide: DirectMessageService,
                    useValue: { unreadCount: signal(0).asReadonly(), refreshUnreadCount: dmRefresh, resetUnreadCount: () => undefined },
                },
                {
                    provide: NotificationService,
                    useValue: { unreadCount: signal(0).asReadonly(), refreshUnreadCount: () => of(0), resetUnreadCount: () => undefined },
                },
                { provide: UserThemeService, useValue: { userTheme$: of(null) } },
                { provide: ThemeConfigService, useValue: {} },
            ],
        });
        fixture = TestBed.createComponent(EnhancedLayoutComponent);
    }

    afterEach(() => localStorage.removeItem('user'));

    it('studentPush_RefreshesStudentUnreadCount_Debounced', async () => {
        setup('Student');
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);
        expect(dmRefresh).toHaveBeenCalledTimes(1);
        expect(dmRefresh).toHaveBeenCalledWith('Student');

        directMessageReceived$.next({ conversationId: 5, senderRole: 'Teacher' });
        directMessageReceived$.next({ conversationId: 5, senderRole: 'Teacher' });
        await vi.advanceTimersByTimeAsync(EnhancedLayoutComponent.DM_PUSH_REFRESH_DEBOUNCE_MS);

        expect(dmRefresh).toHaveBeenCalledTimes(2);
        fixture.destroy();
        await vi.runAllTimersAsync();
    });

    it('noPush_DoesNotRefreshAgain', async () => {
        setup('Student');
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(EnhancedLayoutComponent.DM_PUSH_REFRESH_DEBOUNCE_MS * 2);
        expect(dmRefresh).toHaveBeenCalledTimes(1);
        fixture.destroy();
        await vi.runAllTimersAsync();
    });
});

/** Tüm route ağacındaki tam yollar (`/a/b`), parametreli olanlar dahil. */
function allRoutePaths(list: Routes, parent = ''): string[] {
    return list.flatMap((r) => {
        const own = r.path ? `${parent}/${r.path}` : parent;
        const self = r.path !== undefined && r.path !== '**' ? [own || '/'] : [];
        return [...self, ...(r.children ? allRoutePaths(r.children, own) : [])];
    });
}

function layoutProviders(roles: string[], unapprovedTeacher = false, routeConfig: Routes = [], user: UserProfile | null = null) {
    const authStub: Partial<AuthService> = {
        hasRealmRole: (role: string) => roles.includes(role),
        isAuthenticated: () => of(true),
        isUnapprovedTeacher: signal(unapprovedTeacher),
        user: signal(user),
    };
    return [
        provideRouter(routeConfig),
        { provide: AuthService, useValue: authStub },
        { provide: SignalRService, useValue: { accessRequestUpdates$: new Subject() } },
        { provide: WorksheetAccessRequestService, useValue: { pendingCount: signal(0) } },
        { provide: StudentSchoolRequestService, useValue: { pendingCount: signal(0), refreshPendingCount: () => of(0) } },
        { provide: DirectMessageService, useValue: directMessageStub() },
        { provide: NotificationService, useValue: { unreadCount: signal(0) } },
        { provide: UserThemeService, useValue: {} },
        { provide: ThemeConfigService, useValue: {} },
    ];
}

/** Issue #374 / #373: rotasız ve role uymayan menü öğeleri gizli; her görünür öğenin rotası tanımlı. */
describe('EnhancedLayoutComponent menu routes (issues #374, #373)', () => {
    const ROLE_CASES: Array<{
        name: string;
        roles: string[];
        unapproved?: boolean;
    }> = [
        { name: 'Student', roles: ['Student'] },
        { name: 'Teacher', roles: ['Teacher'] },
        { name: 'UnapprovedTeacher', roles: ['Teacher'], unapproved: true },
        { name: 'Admin', roles: ['Admin'] },
        { name: 'AdminTeacher', roles: ['Admin', 'Teacher'] },
    ];

    function create(roles: string[], unapproved = false): EnhancedLayoutComponent {
        TestBed.configureTestingModule({
            imports: [EnhancedLayoutComponent, translocoTestingModule()],
            providers: layoutProviders(roles, unapproved),
        });
        return TestBed.createComponent(EnhancedLayoutComponent).componentInstance;
    }

    for (const roleCase of ROLE_CASES) {
        it(`everyVisibleMenuItem_${roleCase.name}_HasRouteDefinedInAppRoutes`, () => {
            const component = create(roleCase.roles, roleCase.unapproved);
            const defined = new Set(allRoutePaths(routes));
            const items = [...component.visibleMenuItems(), ...component.visibleBottomNavItems()].filter((i) => i.type === 'menu');

            expect(items.length).toBeGreaterThan(0);
            for (const item of items) {
                expect(defined.has(item.route), `${roleCase.name}: ${item.id} -> ${item.route}`).toBe(true);
            }
        });

        it(`visibleMenuItems_${roleCase.name}_HidesItemsWithoutPage`, () => {
            const component = create(roleCase.roles, roleCase.unapproved);
            const ids = component.visibleMenuItems().map((i) => i.id);
            for (const hidden of ['students', 'help', 'feedback']) {
                expect(ids, `${roleCase.name}: ${hidden}`).not.toContain(hidden);
            }
        });
    }

    // Issue #417: Ayarlar her rolde görünür; öğrenci kendi sayfasına, öğretmen/admin /settings'e gider.
    const SETTINGS_CASES: Array<{
        name: string;
        roles: string[];
        unapproved?: boolean;
        route: string;
    }> = [
        { name: 'Student', roles: ['Student'], route: '/student-settings' }, // #847
        { name: 'Teacher', roles: ['Teacher'], route: '/settings' },
        { name: 'UnapprovedTeacher', roles: ['Teacher'], unapproved: true, route: '/settings' },
        { name: 'Admin', roles: ['Admin'], route: '/settings' },
        { name: 'AdminTeacher', roles: ['Admin', 'Teacher'], route: '/settings' },
        // Karma rol: guard ile aynı kural (settingsUrlFor) — öğretmenlik /settings'e götürür.
        { name: 'StudentTeacher', roles: ['Student', 'Teacher'], route: '/settings' },
    ];

    for (const c of SETTINGS_CASES) {
        it(`settings_${c.name}_VisibleInSidenav_WithRoleRoute (issue #417)`, () => {
            const component = create(c.roles, c.unapproved);
            const settings = component.visibleMenuItems().filter((i) => i.id === 'settings');
            expect(settings.length, c.name).toBe(1);
            expect(settings[0].route, c.name).toBe(c.route);
            expect(component.settingsRoute).toBe(c.route);
        });
    }

    it('settings_NonStudent_NeverLinksStudentProfile', () => {
        for (const roles of [['Teacher'], ['Admin']]) {
            TestBed.resetTestingModule();
            const other = create(roles);
            expect(other.visibleMenuItems().map((i) => i.route), roles[0]).not.toContain('/student-profile');
            expect(other.visibleBottomNavItems().map((i) => i.route), roles[0]).not.toContain('/student-profile');
        }
    });

    it('routes_Settings_UsesSettingsGuardWithoutApprovalGate (issue #417)', () => {
        const children = routes.find((r) => Array.isArray(r.children))?.children ?? [];
        const route = children.find((r) => r.path === 'settings');
        expect(route?.canActivate).toContain(settingsGuard);
        expect(route?.loadComponent).toBeDefined();
    });

    it('routes_StudentProfile_UsesStudentGuard', () => {
        const children = routes.find((r) => Array.isArray(r.children))?.children ?? [];
        const route = children.find((r) => r.path === 'student-profile');
        expect(route?.canActivate).toContain(studentGuard);
    });

    it('study_HiddenForStudentInSidenavAndBottomNav (issue #382)', () => {
        const student = create(['Student']);

        expect(student.visibleMenuItems().map((i) => i.route)).not.toContain('/study');
        expect(student.visibleBottomNavItems().map((i) => i.route)).not.toContain('/study');
        expect(student.visibleMenuItems().map((i) => i.id)).not.toContain('study');
        expect(student.visibleBottomNavItems().map((i) => i.id)).not.toContain('study');
    });

    it('routes_Study_RedirectsToDashboardWithoutRenderingSamplePage (issue #382)', () => {
        const children = routes.find((r) => Array.isArray(r.children))?.children ?? [];
        const route = children.find((r) => r.path === 'study');

        expect(route).toBeDefined();
        expect(route?.redirectTo).toBe('/dashboard');
        expect(route?.component).toBeUndefined();
        expect(route?.loadComponent).toBeUndefined();
    });

    it('allRoutePaths_HelperResolvesNestedPaths', () => {
        const paths = allRoutePaths(routes);
        expect(paths).toContain('/programs/:id/detail');
        expect(paths).toContain('/admin/teachers');
        expect(paths).not.toContain('/help');
    });
});

@Component({ standalone: true, changeDetection: ChangeDetectionStrategy.Eager,
    template: '' })
class ActiveItemStubPageComponent {
}

/** Issue #385: seçili menü öğesi gezinmeden (NavigationEnd) türetilir. */
describe('EnhancedLayoutComponent active menu item (issue #385)', () => {
    function create(roles: string[]): EnhancedLayoutComponent {
        TestBed.configureTestingModule({
            imports: [EnhancedLayoutComponent, translocoTestingModule()],
            providers: layoutProviders(roles, false, [{ path: '**', component: ActiveItemStubPageComponent }]),
        });
        return TestBed.createComponent(EnhancedLayoutComponent).componentInstance;
    }

    async function expectActive(component: EnhancedLayoutComponent, cases: Array<[
        string,
        string | null
    ]>) {
        const router = TestBed.inject(Router);
        for (const [url, expected] of cases) {
            await router.navigateByUrl(url);
            expect(component.activeMenuItem(), url).toBe(expected);
        }
    }

    it('student_DetailPagesSelectParentItem_AndUnknownPageSelectsNothing', async () => {
        await expectActive(create(['Student']), [
            ['/dashboard', 'dashboard'],
            ['/programs/5/detail', 'my-calendar'],
            ['/program-create', 'my-calendar'],
            ['/test/12', 'exams'],
            ['/notifications', null],
            // #847: profil menüde değil (kullanıcı menüsünde); Ayarlar ayrı sayfa.
            ['/student-profile', null],
            ['/student-settings', 'settings'],
        ]);
    });

    it('teacher_ExamAndQuestionCanvasSelectTests', async () => {
        await expectActive(create(['Teacher']), [
            ['/exam', 'exams'],
            ['/exam/3', 'exams'],
            ['/questioncanvas', 'exams'],
            ['/test/12', 'exams'],
            ['/study-pages/new', 'study-pages'],
            ['/student-profile', null],
            ['/settings', 'settings'],
        ]);
    });

    it('isQuestionCanvasRoute_TrueOnlyForCanvasEditor', async () => {
        const component = create(['Teacher']);
        const router = TestBed.inject(Router);
        const cases: Array<[string, boolean]> = [
            ['/questioncanvas', true],
            ['/questioncanvas?testId=4', true],
            ['/questioncanvas/12', true],
            ['/questioncanvas/preview', false],
            ['/questioncanvas/preview/4', false],
            ['/questioncanvasx', false],
            ['/exam', false],
        ];
        for (const [url, expected] of cases) {
            await router.navigateByUrl(url);
            expect(component.isQuestionCanvasRoute(), url).toBe(expected);
        }
    });

    it('beforeAnyNavigation_DoesNotDefaultToDashboard', () => {
        const component = create(['Student']);
        expect(component.activeMenuItem()).toBeNull();
    });
});

/**
 * Tam render edilen (ngOnInit + şablon) layout için sağlayıcılar. `mobile` BreakpointObserver'ı sabitler;
 * Karma penceresi mobil sorgusuna düşebildiği için her iki düzen de açıkça seçilir.
 */
function renderedLayoutProviders(roles: string[], mobile: boolean) {
    const authStub: Partial<AuthService> = {
        hasRealmRole: (role: string) => roles.includes(role),
        isAuthenticated: () => of(true),
        isUnapprovedTeacher: signal(false),
        isCachedUserCurrent: () => true,
        refreshProfile: () => NEVER,
        user: signal(null),
    };
    return [
        { provide: BreakpointObserver, useValue: { observe: () => of({ matches: mobile, breakpoints: {} }) } },
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: authStub },
        {
            provide: SignalRService,
            useValue: {
                startConnection: () => undefined,
                accessRequestUpdates$: new Subject(),
                notificationsChanged$: new Subject<void>().asObservable(),
                directMessageReceived$: new Subject().asObservable(),
            },
        },
        { provide: WorksheetAccessRequestService, useValue: { pendingCount: signal(0), refreshPendingCount: () => of(0) } },
        { provide: StudentSchoolRequestService, useValue: { pendingCount: signal(0), refreshPendingCount: () => of(0) } },
        { provide: DirectMessageService, useValue: directMessageStub() },
        {
            provide: NotificationService,
            useValue: { unreadCount: signal(0).asReadonly(), refreshUnreadCount: () => of(0), resetUnreadCount: () => undefined },
        },
        { provide: UserThemeService, useValue: { userTheme$: of(null) } },
        { provide: ThemeConfigService, useValue: {} },
    ];
}

function renderLayout(roles: string[], mobile: boolean): ComponentFixture<EnhancedLayoutComponent> {
    TestBed.configureTestingModule({
        imports: [EnhancedLayoutComponent, NoopAnimationsModule, translocoTestingModule()],
        providers: renderedLayoutProviders(roles, mobile),
    });
    const fixture = TestBed.createComponent(EnhancedLayoutComponent);
    fixture.detectChanges();
    return fixture;
}

/** Issue #409: marka adı uygulama kabuğunda kök sözlükteki tek `brand.name` anahtarından gelir. */
describe('EnhancedLayoutComponent brand name (issue #409)', () => {
    afterEach(() => localStorage.removeItem('user'));

    function text(fixture: ComponentFixture<EnhancedLayoutComponent>, selector: string): string | undefined {
        return (fixture.nativeElement as HTMLElement).querySelector(selector)?.textContent?.trim();
    }

    it('desktopSidebarTitle_ShowsBrandNameFromI18n', () => {
        const fixture = renderLayout(['Student'], false);
        expect(text(fixture, '.app-title')).toBe('Hedef Okul');
    });

    it('mobileToolbarTitle_ShowsBrandNameFromI18n', () => {
        const fixture = renderLayout(['Student'], true);
        expect(text(fixture, '.mobile-toolbar-title')).toBe('Hedef Okul');
    });

    it('brandName_IsSameInEnglish_AndNeverTheOldName', () => {
        const fixture = renderLayout(['Student'], false);
        TestBed.inject(TranslocoService).setActiveLang('en');
        fixture.detectChanges();
        expect(text(fixture, '.app-title')).toBe('Hedef Okul');
        expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('ExamApp');
    });
});

/**
 * Issue #375: profil menüsünde yalnız çalışan öğeler. #417: Ayarlar tüm rollerde (öğrencide profil sayfası).
 * #846: menü kim olduğunu gösterir; öğrencide Profilim + Rozetlerim; tema ve dil menünün içinde; Çıkış ayrı grupta.
 */
describe('EnhancedLayoutComponent profile menu (issue #375, #846)', () => {
    let fixture: ComponentFixture<EnhancedLayoutComponent>;

    function setup(roles: string[]): void {
        // Masaüstü düzeni: profil menüsü yalnız masaüstü toolbar'ında.
        fixture = renderLayout(roles, false);
    }

    afterEach(() => localStorage.removeItem('user'));

    function openProfileMenu(): HTMLElement[] {
        const trigger = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('[data-testid="user-menu-trigger"]');
        expect(trigger, 'profile menu trigger').not.toBeNull();
        trigger?.click();
        fixture.detectChanges();
        return Array.from(document.querySelectorAll<HTMLElement>('.user-menu-panel [mat-menu-item]'));
    }

    function titles(items: HTMLElement[]): (string | undefined)[] {
        return items.map((i) => i.querySelector('.mat-mdc-menu-item-text')?.textContent?.replace(/\s+/g, ' ').trim());
    }

    it('student_SeesProfileBadgesNotificationsAndLogout (#846)', () => {
        setup(['Student']);
        const navigateSpy = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
        const items = openProfileMenu();

        expect(titles(items)).toEqual(['Profilim', 'Rozetlerim', 'Bildirimler', 'Ayarlar', 'Çıkış Yap']);
        items[0].click();
        expect(navigateSpy).toHaveBeenCalledWith(['/student-profile'], {});
        items[1].click();
        expect(navigateSpy).toHaveBeenCalledWith(['/certificates'], {});
        items[3].click();
        expect(navigateSpy).toHaveBeenCalledWith(['/student-settings'], {}); // #847
    });

    for (const roles of [['Teacher'], ['Admin']]) {
        it(`${roles[0]}_SeesNotificationsSettingsAndLogout_SettingsOpensSettingsPage (issue #417)`, () => {
            setup(roles);
            const navigateSpy = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
            const items = openProfileMenu();

            expect(titles(items)).toEqual(['Bildirimler', 'Ayarlar', 'Çıkış Yap']);
            items[1].click();
            expect(navigateSpy).toHaveBeenCalledWith(['/settings'], {});
        });
    }

    it('header_ShowsInitialsRoleGradeAndEmail_PreferencesInside (#846)', () => {
        setup(['Student']);
        const c = fixture.componentInstance;
        c.userName.set('Pilot Öğrenci');
        c.userEmail.set('student574@hedefokul.com');
        c.userGradeName.set('5. Sınıf');
        fixture.detectChanges();
        openProfileMenu();

        const who = document.querySelector<HTMLElement>('[data-testid="user-menu-who"]')!;
        expect(who.querySelector('.um-who__avatar--initials')?.textContent?.trim()).toBe('PÖ');
        expect(who.querySelector('[data-testid="user-menu-role"]')?.textContent?.trim()).toBe('Öğrenci · 5. Sınıf');
        expect(who.querySelector('.um-who__email')?.getAttribute('title')).toBe('student574@hedefokul.com');
        expect(document.querySelector('.user-menu-panel [data-testid="ump-theme"]')).not.toBeNull();
        expect(document.querySelector('.user-menu-panel [data-testid="ump-language"]')).not.toBeNull();
        // Oturum açmışken tema ve dil üst çubukta değil.
        expect((fixture.nativeElement as HTMLElement).querySelector('.toolbar-language-switcher')).toBeNull();
    });

    it('deadProfileHandlers_AreRemoved', () => {
        setup(['Student']);
        const component = fixture.componentInstance;
        for (const handler of ['onProfile', 'onAccountSettings', 'onSupport']) {
            expect(handler in component, handler).toBe(false);
        }
    });
});

/** Issue #384: özel ders profili yalnız bağımsız öğretmene; okula bağlı öğretmende menüde yok, rota guard'lı. */
describe('EnhancedLayoutComponent tutor profile entry (issue #384)', () => {
    /** Backend kuralı `teacher.isIndependentTutor`; okul kimliği karar vermez. */
    function teacherProfile(isIndependentTutor: boolean | undefined, schoolId: number | null = null): UserProfile {
        return {
            email: '',
            avatar: '',
            fullName: '',
            id: 1,
            keycloakId: 'k',
            profileId: 1,
            role: 'Teacher',
            teacher: { id: 1, userId: 1, schoolName: '', schoolId, isIndependentTutor },
        } as UserProfile;
    }

    function create(roles: string[], user: UserProfile | null, unapproved = false): EnhancedLayoutComponent {
        TestBed.configureTestingModule({
            imports: [EnhancedLayoutComponent, translocoTestingModule()],
            providers: layoutProviders(roles, unapproved, [], user),
        });
        return TestBed.createComponent(EnhancedLayoutComponent).componentInstance;
    }

    const routesOf = (c: EnhancedLayoutComponent) => c.visibleMenuItems().map((i) => i.route);

    it('visibleMenuItems_NotIndependentTeacher_HidesTutorProfile', () => {
        expect(routesOf(create(['Teacher'], teacherProfile(false, 7)))).not.toContain('/tutor-profile');
    });

    it('visibleMenuItems_NotIndependentUnapprovedTeacher_HidesTutorProfile', () => {
        expect(routesOf(create(['Teacher'], teacherProfile(false), true))).not.toContain('/tutor-profile');
    });

    it('visibleMenuItems_IndependentTeacher_ShowsTutorProfile', () => {
        expect(routesOf(create(['Teacher'], teacherProfile(true)))).toContain('/tutor-profile');
    });

    it('visibleMenuItems_IndependentFlagWins_OverSchoolId', () => {
        // Okul kimliği olsa da backend bağımsız diyorsa öğe görünür (tek kural: isIndependentTutor).
        expect(routesOf(create(['Teacher'], teacherProfile(true, 7)))).toContain('/tutor-profile');
    });

    it('visibleMenuItems_FlagUnknownOrProfileNotLoaded_ShowsTutorProfile', () => {
        expect(routesOf(create(['Teacher'], teacherProfile(undefined, 7)))).toContain('/tutor-profile');
        TestBed.resetTestingModule();
        expect(routesOf(create(['Teacher'], null))).toContain('/tutor-profile');
    });

    it('routes_TutorProfile_UsesIndependentTeacherGuard', () => {
        const children = routes.find((r) => Array.isArray(r.children))?.children ?? [];
        const route = children.find((r) => r.path === 'tutor-profile');
        expect(route?.canActivate).toContain(independentTeacherGuard);
    });

    const tabRoutesOf = (component: EnhancedLayoutComponent) => component.visibleSectionTabs().flat().map((i) => i.route);

    // Issue #418: randevu (müsaitlik + gelen talepler) da bağımsız öğretmen özelliği — aynı kural.
    for (const route of ['/availability', '/booking-requests']) {
        it(`visibleMenuItems_NotIndependentTeacher_Hides_${route}`, () => {
            expect(tabRoutesOf(create(['Teacher'], teacherProfile(false, 7)))).not.toContain(route);
        });

        it(`visibleMenuItems_IndependentTeacher_Shows_${route}`, () => {
            expect(tabRoutesOf(create(['Teacher'], teacherProfile(true)))).toContain(route);
        });

        it(`visibleMenuItems_FlagUnknown_Shows_${route}`, () => {
            expect(tabRoutesOf(create(['Teacher'], teacherProfile(undefined, 7)))).toContain(route);
        });

        it(`routes_${route}_UsesIndependentTeacherGuard`, () => {
            const children = routes.find((r) => Array.isArray(r.children))?.children ?? [];
            const config = children.find((r) => `/${r.path}` === route);
            expect(config?.canActivate).toContain(independentTeacherGuard);
        });
    }
});

/** Issue #706: mobilde ve daraltılmış kenar çubuğunda toolbar yok; çıkış kenar çubuğunda da bulunur. */
describe('EnhancedLayoutComponent sidebar logout (issue #706)', () => {
    afterEach(() => localStorage.removeItem('user'));

    function logoutItem(fixture: ComponentFixture<EnhancedLayoutComponent>): HTMLElement | null {
        return (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.sidebar-logout-item');
    }

    it('mobile_ShowsLogoutInSidebar_AndClickLogsOut', () => {
        const fixture = renderLayout(['Student'], true);
        const logoutSpy = vi.spyOn(fixture.componentInstance, 'logout').mockImplementation(() => undefined);

        const item = logoutItem(fixture);
        expect(item?.textContent).toContain('Çıkış Yap');
        item?.click();
        expect(logoutSpy).toHaveBeenCalledOnce();
    });

    it('collapsedDesktop_ShowsIconOnlyLogout', () => {
        const fixture = renderLayout(['Teacher'], false);
        TestBed.inject(SidenavService).setSidenavCollapsed(true);
        fixture.detectChanges();

        const item = logoutItem(fixture);
        expect(item).not.toBeNull();
        expect(item?.querySelector('.menu-text')).toBeNull();
        expect(item?.getAttribute('title')).toBe('Çıkış Yap');
    });

    it('expandedDesktop_KeepsLogoutInProfileMenuOnly', () => {
        const fixture = renderLayout(['Student'], false);
        expect(logoutItem(fixture)).toBeNull();
    });
});

/** Issue #713: "Raporlar" rozet sayfasını açıyordu; öğrencide Rozetler, diğer rollerde yok. */
describe('EnhancedLayoutComponent badges menu item (issue #713)', () => {
    afterEach(() => localStorage.removeItem('user'));

    function ids(roles: string[]): string[] {
        return renderLayout(roles, false).componentInstance.visibleMenuItems().map((item) => item.id);
    }

    it('student_SeesBadges', () => {
        expect(ids(['Student'])).toContain('badges');
    });

    for (const role of ['Teacher', 'Parent', 'Admin']) {
        it(`${role}_DoesNotSeeBadges`, () => {
            expect(ids([role])).not.toContain('badges');
        });
    }
});

/** Issue #719: rol başına menü, alt çubuk ve bölüm sekmeleri. */
describe('EnhancedLayoutComponent role navigation (issue #719)', () => {
    afterEach(() => localStorage.removeItem('user'));

    function labels(ids: { id: string }[]): string[] {
        return ids.map((i) => i.id);
    }

    it('student_SixPrimaryItemsPlusBadgesAndSettings', () => {
        const items = renderLayout(['Student'], false).componentInstance.visibleMenuItems().filter((i) => i.type === 'menu');
        expect(labels(items)).toEqual(['dashboard', 'exams', 'practice', 'my-calendar', 'tutors', 'teacher-messages', 'badges', 'settings']);
    });

    it('teacher_GroupsSectionsAndDropsCreateTest', () => {
        const items = renderLayout(['Teacher'], false).componentInstance.visibleMenuItems().filter((i) => i.type === 'menu');
        expect(labels(items)).toEqual([
            'dashboard', 'exams', 'question-bank', 'study-pages', 'my-calendar', 'teacher-requests', 'student-messages', 'tutor-profile', 'settings',
        ]);
    });

    it('admin_DashboardFirstAndItemsGroupedUnderHeaders_issue726', () => {
        const items = renderLayout(['Admin'], false).componentInstance.visibleMenuItems().filter((i) => i.type !== 'divider');
        expect(labels(items)).toEqual([
            'admin-dashboard', 'admin', 'admin-pending', 'question-bank',
            'group-users', 'admin-teachers', 'admin-students',
            'group-schools', 'admin-schools',
            'group-content', 'mock-exams', 'exam-templates', 'questiongeneration', 'questiontransfer', 'labeling', 'labeling-books', 'admin-badge-definitions',
            'settings',
        ]);
    });

    // Issue #861: beş kuyruk tek "Bekleyenler" girişinde; sekmeler ve rozetler özet ucundan.
    it('admin_PendingInbox_TabsCountsAndActiveItem_issue861', () => {
        const component = renderLayout(['Admin'], false).componentInstance;
        const pending = TestBed.inject(AdminPendingService);
        pending.summary.set({ teacherApplications: 2, schoolApplications: 1, commentReports: 0, questionReports: 4, openTextAnswers: 3, total: 10 });

        expect(component.menuBadgeCount('admin-pending')).toBe(10);
        expect(component.menuBadgeCount('tab-admin-question-reports')).toBe(4);
        expect(component.visibleSectionTabs()[0].map((t) => t.route)).toEqual([
            '/admin/teacher-approvals', '/admin/student-school-requests', '/admin/comment-reports', '/question-reports', '/open-text-reviews',
        ]);
        for (const url of ['/admin/comment-reports', '/question-reports', '/open-text-reviews']) {
            expect(resolveActiveMenuItemId(url, component.visibleMenuItems())).toBe('admin-pending');
        }
    });

    it('questionTrainer_SeesLabelingWithoutAdminGroupHeaders_issue726', () => {
        const items = renderLayout(['QuestionTrainer'], false).componentInstance.visibleMenuItems();
        expect(items.some((i) => i.type === 'header')).toBe(false);
        expect(labels(items.filter((i) => i.type === 'menu'))).toEqual(['questiongeneration', 'labeling', 'labeling-books', 'settings']);
    });

    it('bottomNav_StudentAndTeacher_HavePrimaryTasksInsteadOfSettings', () => {
        expect(labels(renderLayout(['Student'], true).componentInstance.visibleBottomNavItems())).toEqual([
            'dashboard', 'exams', 'practice', 'my-calendar',
        ]);
        TestBed.resetTestingModule();
        expect(labels(renderLayout(['Teacher'], true).componentInstance.visibleBottomNavItems())).toEqual([
            'dashboard', 'exams', 'question-bank', 'my-calendar',
        ]);
    });

    it('bottomNav_Parent_KeepsSettings', () => {
        expect(labels(renderLayout(['Parent'], true).componentInstance.visibleBottomNavItems())).toContain('settings');
    });

    async function goTo(fixture: ComponentFixture<EnhancedLayoutComponent>, url: string): Promise<void> {
        const router = TestBed.inject(Router);
        router.resetConfig([{ path: '**', children: [] }]);
        await router.navigateByUrl(url);
        fixture.detectChanges();
    }

    it('sectionTabs_ShowOnlyInTheCurrentSection', async () => {
        const fixture = renderLayout(['Student'], false);
        const component = fixture.componentInstance;

        await goTo(fixture, '/my-bookings');
        expect(labels(component.activeSectionTabs())).toEqual(['tab-tutors', 'tab-bookings']);
        expect((fixture.nativeElement as HTMLElement).querySelectorAll('.section-tab').length).toBe(2);

        await goTo(fixture, '/dashboard');
        expect(component.activeSectionTabs()).toEqual([]);
    });

    it('sectionTabs_QuestionReportsSitNextToQuestionPool', async () => {
        const fixture = renderLayout(['Teacher'], false);

        await goTo(fixture, '/question-reports');

        expect(labels(fixture.componentInstance.activeSectionTabs())).toEqual(['tab-question-bank', 'tab-question-reports']);
    });

    it('sectionTabs_HiddenOnExamRoutes', async () => {
        const fixture = renderLayout(['Student'], false);
        await goTo(fixture, '/testsolve/4');
        expect((fixture.nativeElement as HTMLElement).querySelector('.section-tabs')).toBeNull();
    });

    it('sectionTabs_SingleVisibleTab_NoBar', async () => {
        const fixture = renderLayout(['Teacher'], false);
        await goTo(fixture, '/my-calendar');
        expect((fixture.nativeElement as HTMLElement).querySelector('.section-tabs')).toBeNull();
    });

    it('requestsBadge_SumsPendingCounts', () => {
        const component = renderLayout(['Teacher'], false).componentInstance;
        (TestBed.inject(WorksheetAccessRequestService).pendingCount as WritableSignal<number>).set(2);
        (TestBed.inject(StudentSchoolRequestService).pendingCount as WritableSignal<number>).set(3);
        expect(component.menuBadgeCount('teacher-requests')).toBe(5);
    });
});

/** Issue #720: üst çubukta sahte öğe yok; arama yalnız testleri arar ve avatar gerçek. */
describe('EnhancedLayoutComponent header (issue #720)', () => {
    afterEach(() => localStorage.removeItem('user'));

    it('search_HasTestLabel_AndNoSampleSuggestions', () => {
        const el = renderLayout(['Student'], false).nativeElement as HTMLElement;
        const input = el.querySelector<HTMLInputElement>('[data-test="toolbar-search"] input');

        expect(input?.placeholder).toBe('Test ara...');
        input?.dispatchEvent(new FocusEvent('focusin', { bubbles: true }));
        expect(el.querySelector('.ms-search-dropdown')).toBeNull();
    });

    it('newAndPopularButtons_AreRemoved', () => {
        const el = renderLayout(['Student'], false).nativeElement as HTMLElement;
        expect(el.querySelector('.section-nav-btn')).toBeNull();
    });

    it('avatar_UsesUserImage_AndFallsBackOnError', () => {
        const fixture = renderLayout(['Student'], false);
        fixture.componentInstance.userAvatarUrl.set('https://example.test/a.png');
        fixture.detectChanges();
        const img = (fixture.nativeElement as HTMLElement).querySelector<HTMLImageElement>('.preview-image');
        expect(img?.getAttribute('src')).toBe('https://example.test/a.png');

        img?.dispatchEvent(new Event('error'));
        fixture.detectChanges();
        // #846: fotoğraf yüklenemezse baş harfler.
        expect((fixture.nativeElement as HTMLElement).querySelector('.preview-image')).toBeNull();
        expect((fixture.nativeElement as HTMLElement).querySelector('.user-initials')).not.toBeNull();
    });
});
