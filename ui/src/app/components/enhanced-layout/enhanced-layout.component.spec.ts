import { Component, WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed, fakeAsync, flush, tick } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideHttpClient } from '@angular/common/http';
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
import { NotificationService } from '../../services/notification.service';
import { DirectMessageService } from '../../services/direct-message.service';
import { UserThemeService } from '../../services/user-theme.service';
import { ThemeConfigService } from '../../services/theme-config.service';
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
    refreshUnreadCount: jasmine.createSpy('refreshUnreadCount').and.returnValue(of(count)),
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
      expect(item).withContext(entry.id).toBeDefined();
      expect(item?.type).withContext(entry.id).toBe('menu');
      expect(item?.route).withContext(entry.id).toBe(entry.route);
      expect(item?.labelKey).withContext(entry.id).toBe(entry.labelKey);
      expect(item?.icon).withContext(entry.id).toBeTruthy();
    }
  });

  for (const role of ['Teacher', 'Student']) {
    it(`visibleMenuItems_${role}Role_DoesNotContainAdminListEntries`, () => {
      const component = create([role]);
      const routesShown = component.visibleMenuItems().map((i) => i.route);

      for (const entry of ADMIN_LIST_ENTRIES) {
        expect(routesShown).withContext(role).not.toContain(entry.route);
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
    const navigateSpy = spyOn(router, 'navigate').and.returnValue(Promise.resolve(true));

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
    const item = component.visibleMenuItems().find((i) => i.id === 'study-links');
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
      expect(other.visibleMenuItems().map((i) => i.route)).withContext(role).not.toContain('/question-transfer');
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
      expect(routesShown).withContext(route).not.toContain(route);
    }
    const status = items.filter((i) => i.id === 'teacher-approval-status');
    expect(status.length).toBe(1);
    expect(status[0].route).toBe('/teacher-approval-pending');
    // Rolsüz (herkese açık) öğeler kalır; baştaki/sondaki ayırıcılar temizlenir.
    expect(routesShown).toContain('/certificates');
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

    expect(ids).toContain('exam');
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
        { provide: UserThemeService, useValue: {} },
        { provide: ThemeConfigService, useValue: {} },
      ],
    });
    const component = TestBed.createComponent(EnhancedLayoutComponent).componentInstance;
    expect(component.visibleMenuItems().map((i) => i.id)).not.toContain('exam');

    unapproved.set(false);

    expect(component.visibleMenuItems().map((i) => i.id)).toContain('exam');
  });

  it('menuLabel_TeacherApprovalStatus_TranslatedTrAndEn', () => {
    create(['Teacher'], true);
    const transloco = TestBed.inject(TranslocoService);
    expect(transloco.translate('layout.menu.teacherApprovalStatus')).toBe('Başvuru durumu');
    expect(transloco.translate('layout.menu.teacherApprovalStatus', {}, 'en')).toBe('Application status');
  });

  it('routes_TeacherApprovalPendingRoute_Exists', () => {
    const layoutRoute = routes.find((r) => Array.isArray(r.children));
    expect(layoutRoute?.children?.map((r) => r.path)).toContain('teacher-approval-pending');
  });

  // ── Issue #361: bekleyen öğrenci okul başvuruları ───────────────────────────

  it('visibleMenuItems_StudentSchoolRequests_TeacherAndAdminOnly (issue #361)', () => {
    expect(create(['Teacher']).visibleMenuItems().map((i) => i.route)).toContain('/student-school-requests');

    TestBed.resetTestingModule();
    const admin = create(['Admin']).visibleMenuItems().map((i) => i.route);
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

  it('menuBadgeCount_StudentSchoolRequests_UsesPendingCount (issue #361)', () => {
    const component = create(['Teacher']);
    const service = TestBed.inject(StudentSchoolRequestService) as unknown as { pendingCount: WritableSignal<number> };
    service.pendingCount.set(3);
    expect(component.menuBadgeCount('student-school-requests')).toBe(3);
    expect(component.menuBadgeCount('admin-student-school-requests')).toBe(3);
    expect(component.menuBadgeCount('access-requests')).toBe(0);
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
  let fixture: ComponentFixture<EnhancedLayoutComponent>;
  let notificationsChanged$: Subject<void>;
  let authenticated$: BehaviorSubject<boolean>;
  let unreadCount: WritableSignal<number>;
  let refreshUnreadCount: jasmine.Spy;
  let navigateSpy: jasmine.Spy;

  function setup(isAuthenticated = true): void {
    notificationsChanged$ = new Subject<void>();
    authenticated$ = new BehaviorSubject<boolean>(isAuthenticated);
    unreadCount = signal(0);
    refreshUnreadCount = jasmine.createSpy('refreshUnreadCount').and.callFake(() => of(unreadCount()));

    const authStub: Partial<AuthService> = {
      hasRealmRole: () => false,
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
        provideHttpClient(),
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
    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.returnValue(Promise.resolve(true));
    fixture = TestBed.createComponent(EnhancedLayoutComponent);
  }

  function bellButton(): HTMLButtonElement | null {
    return (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.notification-bell');
  }

  function badgeText(): string {
    return bellButton()?.querySelector('.mat-badge-content')?.textContent?.trim() ?? '';
  }

  afterEach(() => localStorage.removeItem('user'));

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

  it('logout_StopsRefreshingOnHubPushes', fakeAsync(() => {
    setup();
    fixture.detectChanges();
    authenticated$.next(false);

    notificationsChanged$.next();
    tick(EnhancedLayoutComponent.NOTIFICATION_REFRESH_DEBOUNCE_MS);

    expect(refreshUnreadCount).toHaveBeenCalledTimes(1);
    fixture.destroy();
    flush();
  }));

  it('hubPushes_AreDebouncedIntoSingleRefresh', fakeAsync(() => {
    setup();
    fixture.detectChanges();
    expect(refreshUnreadCount).toHaveBeenCalledTimes(1);

    notificationsChanged$.next();
    notificationsChanged$.next();
    notificationsChanged$.next();
    tick(EnhancedLayoutComponent.NOTIFICATION_REFRESH_DEBOUNCE_MS - 1);
    expect(refreshUnreadCount).toHaveBeenCalledTimes(1);

    tick(1);
    expect(refreshUnreadCount).toHaveBeenCalledTimes(2);

    fixture.destroy();
    flush();
  }));

  it('refreshError_IsSwallowedAndLaterPushStillRefreshes', fakeAsync(() => {
    setup();
    refreshUnreadCount.and.returnValues(throwError(() => new Error('down')), of(2));
    fixture.detectChanges();

    notificationsChanged$.next();
    tick(EnhancedLayoutComponent.NOTIFICATION_REFRESH_DEBOUNCE_MS);
    expect(refreshUnreadCount).toHaveBeenCalledTimes(2);

    fixture.destroy();
    flush();
  }));

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
    expect(component.isDirectMessageItem('teacher-messages')).toBeTrue();
    expect(component.isDirectMessageItem('access-requests')).toBeFalse();
    const transloco = TestBed.inject(TranslocoService);
    expect(transloco.translate('layout.dmUnreadBadge', { count: 2 })).toBe('2 okunmamış konuşma');
  });

  it('menuLabels_TranslatedTrAndEn', () => {
    create(['Student']);
    const transloco = TestBed.inject(TranslocoService);
    expect(transloco.translate('layout.menu.teacherMessages')).toBe('Öğretmenime Yaz');
    expect(transloco.translate('layout.menu.studentMessages', {}, 'en')).toBe('Student Messages');
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
  let fixture: ComponentFixture<EnhancedLayoutComponent>;
  let directMessageReceived$: Subject<{ conversationId: number; senderRole: 'Student' | 'Teacher' }>;
  let dmRefresh: jasmine.Spy;

  function setup(role: 'Student' | 'Teacher'): void {
    directMessageReceived$ = new Subject();
    dmRefresh = jasmine.createSpy('refreshUnreadCount').and.returnValue(of(1));
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
        provideHttpClient(),
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

  it('studentPush_RefreshesStudentUnreadCount_Debounced', fakeAsync(() => {
    setup('Student');
    fixture.detectChanges();
    tick();
    expect(dmRefresh).toHaveBeenCalledTimes(1);
    expect(dmRefresh).toHaveBeenCalledWith('Student');

    directMessageReceived$.next({ conversationId: 5, senderRole: 'Teacher' });
    directMessageReceived$.next({ conversationId: 5, senderRole: 'Teacher' });
    tick(EnhancedLayoutComponent.DM_PUSH_REFRESH_DEBOUNCE_MS);

    expect(dmRefresh).toHaveBeenCalledTimes(2);
    fixture.destroy();
    flush();
  }));

  it('noPush_DoesNotRefreshAgain', fakeAsync(() => {
    setup('Student');
    fixture.detectChanges();
    tick(EnhancedLayoutComponent.DM_PUSH_REFRESH_DEBOUNCE_MS * 2);
    expect(dmRefresh).toHaveBeenCalledTimes(1);
    fixture.destroy();
    flush();
  }));
});

/** Tüm route ağacındaki tam yollar (`/a/b`), parametreli olanlar dahil. */
function allRoutePaths(list: Routes, parent = ''): string[] {
  return list.flatMap((r) => {
    const own = r.path ? `${parent}/${r.path}` : parent;
    const self = r.path !== undefined && r.path !== '**' ? [own || '/'] : [];
    return [...self, ...(r.children ? allRoutePaths(r.children, own) : [])];
  });
}

function layoutProviders(
  roles: string[],
  unapprovedTeacher = false,
  routeConfig: Routes = [],
  user: UserProfile | null = null
) {
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
  const ROLE_CASES: Array<{ name: string; roles: string[]; unapproved?: boolean }> = [
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
      const items = [...component.visibleMenuItems(), ...component.visibleBottomNavItems()].filter(
        (i) => i.type === 'menu'
      );

      expect(items.length).toBeGreaterThan(0);
      for (const item of items) {
        expect(defined.has(item.route)).withContext(`${roleCase.name}: ${item.id} -> ${item.route}`).toBeTrue();
      }
    });

    it(`visibleMenuItems_${roleCase.name}_HidesItemsWithoutPage`, () => {
      const component = create(roleCase.roles, roleCase.unapproved);
      const ids = component.visibleMenuItems().map((i) => i.id);
      for (const hidden of ['students', 'help', 'feedback']) {
        expect(ids).withContext(`${roleCase.name}: ${hidden}`).not.toContain(hidden);
      }
    });
  }

  // Issue #417: Ayarlar her rolde görünür; öğrenci kendi sayfasına, öğretmen/admin /settings'e gider.
  const SETTINGS_CASES: Array<{ name: string; roles: string[]; unapproved?: boolean; route: string }> = [
    { name: 'Student', roles: ['Student'], route: '/student-profile' },
    { name: 'Teacher', roles: ['Teacher'], route: '/settings' },
    { name: 'UnapprovedTeacher', roles: ['Teacher'], unapproved: true, route: '/settings' },
    { name: 'Admin', roles: ['Admin'], route: '/settings' },
    { name: 'AdminTeacher', roles: ['Admin', 'Teacher'], route: '/settings' },
    // Karma rol: guard ile aynı kural (settingsUrlFor) — öğretmenlik /settings'e götürür.
    { name: 'StudentTeacher', roles: ['Student', 'Teacher'], route: '/settings' },
  ];

  for (const c of SETTINGS_CASES) {
    it(`settings_${c.name}_VisibleInSidenavAndBottomNav_WithRoleRoute (issue #417)`, () => {
      const component = create(c.roles, c.unapproved);
      for (const items of [component.visibleMenuItems(), component.visibleBottomNavItems()]) {
        const settings = items.filter((i) => i.id === 'settings');
        expect(settings.length).withContext(c.name).toBe(1);
        expect(settings[0].route).withContext(c.name).toBe(c.route);
      }
      expect(component.settingsRoute).toBe(c.route);
    });
  }

  it('settings_NonStudent_NeverLinksStudentProfile', () => {
    for (const roles of [['Teacher'], ['Admin']]) {
      TestBed.resetTestingModule();
      const other = create(roles);
      expect(other.visibleMenuItems().map((i) => i.route)).withContext(roles[0]).not.toContain('/student-profile');
      expect(other.visibleBottomNavItems().map((i) => i.route)).withContext(roles[0]).not.toContain('/student-profile');
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

@Component({ standalone: true, template: '' })
class ActiveItemStubPageComponent {}

/** Issue #385: seçili menü öğesi gezinmeden (NavigationEnd) türetilir. */
describe('EnhancedLayoutComponent active menu item (issue #385)', () => {
  function create(roles: string[]): EnhancedLayoutComponent {
    TestBed.configureTestingModule({
      imports: [EnhancedLayoutComponent, translocoTestingModule()],
      providers: layoutProviders(roles, false, [{ path: '**', component: ActiveItemStubPageComponent }]),
    });
    return TestBed.createComponent(EnhancedLayoutComponent).componentInstance;
  }

  async function expectActive(component: EnhancedLayoutComponent, cases: Array<[string, string | null]>) {
    const router = TestBed.inject(Router);
    for (const [url, expected] of cases) {
      await router.navigateByUrl(url);
      expect(component.activeMenuItem()).withContext(url).toBe(expected);
    }
  }

  it('student_DetailPagesSelectParentItem_AndUnknownPageSelectsNothing', async () => {
    await expectActive(create(['Student']), [
      ['/dashboard', 'dashboard'],
      ['/programs/5/detail', 'programsm'],
      ['/program-create', 'programsm'],
      ['/test/12', 'exams'],
      ['/notifications', null],
      ['/student-profile', 'settings'],
    ]);
  });

  it('teacher_ExamAndQuestionCanvasSelectExamAuthoring', async () => {
    await expectActive(create(['Teacher']), [
      ['/exam', 'exam'],
      ['/exam/3', 'exam'],
      ['/questioncanvas', 'exam'],
      ['/test/12', 'exams'],
      ['/study-pages/new', 'study-pages'],
      ['/student-profile', null],
      ['/settings', 'settings'],
    ]);
  });

  it('beforeAnyNavigation_DoesNotDefaultToDashboard', () => {
    const component = create(['Student']);
    expect(component.activeMenuItem()).toBeNull();
  });
});

/** Issue #375: profil menüsünde yalnız çalışan öğeler (Ayarlar, Bildirimler, Çıkış). #417: Ayarlar tüm rollerde. */
describe('EnhancedLayoutComponent profile menu (issue #375)', () => {
  let fixture: ComponentFixture<EnhancedLayoutComponent>;

  function setup(roles: string[]): void {
    const authStub: Partial<AuthService> = {
      hasRealmRole: (role: string) => roles.includes(role),
      isAuthenticated: () => of(true),
      isUnapprovedTeacher: signal(false),
      isCachedUserCurrent: () => true,
      refreshProfile: () => NEVER,
      user: signal(null),
    };
    TestBed.configureTestingModule({
      imports: [EnhancedLayoutComponent, NoopAnimationsModule, translocoTestingModule()],
      providers: [
        // Masaüstü düzeni: profil menüsü yalnız masaüstü toolbar'ında (Karma penceresi mobil sorgusuna düşebilir).
        { provide: BreakpointObserver, useValue: { observe: () => of({ matches: false, breakpoints: {} }) } },
        provideHttpClient(),
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
      ],
    });
    fixture = TestBed.createComponent(EnhancedLayoutComponent);
    fixture.detectChanges();
  }

  afterEach(() => localStorage.removeItem('user'));

  function openProfileMenu(): HTMLElement[] {
    const trigger = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.profile-menu');
    expect(trigger).withContext('profile menu trigger').not.toBeNull();
    trigger?.click();
    fixture.detectChanges();
    return Array.from(document.querySelectorAll<HTMLElement>('.ms-menu-panel .ms-menu-item'));
  }

  function titles(items: HTMLElement[]): (string | undefined)[] {
    return items.map((i) => i.querySelector('.ms-item-title')?.textContent?.trim());
  }

  it('student_SeesSettingsNotificationsAndLogoutOnly', () => {
    setup(['Student']);
    const navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.returnValue(Promise.resolve(true));
    const items = openProfileMenu();

    expect(titles(items)).toEqual(['Ayarlar', 'Bildirimler', 'Çıkış Yap']);
    items[0].click();
    expect(navigateSpy).toHaveBeenCalledWith(['/student-profile'], {});
  });

  for (const roles of [['Teacher'], ['Admin']]) {
    it(`${roles[0]}_SeesSettingsNotificationsAndLogout_SettingsOpensSettingsPage (issue #417)`, () => {
      setup(roles);
      const navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.returnValue(Promise.resolve(true));
      const items = openProfileMenu();

      expect(titles(items)).toEqual(['Ayarlar', 'Bildirimler', 'Çıkış Yap']);
      items[0].click();
      expect(navigateSpy).toHaveBeenCalledWith(['/settings'], {});
    });
  }

  it('deadProfileHandlers_AreRemoved', () => {
    setup(['Student']);
    const component = fixture.componentInstance;
    for (const handler of ['onProfile', 'onAccountSettings', 'onSupport']) {
      expect(handler in component).withContext(handler).toBeFalse();
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

  // Issue #418: randevu (müsaitlik + gelen talepler) da bağımsız öğretmen özelliği — aynı kural.
  for (const route of ['/availability', '/booking-requests']) {
    it(`visibleMenuItems_NotIndependentTeacher_Hides_${route}`, () => {
      expect(routesOf(create(['Teacher'], teacherProfile(false, 7)))).not.toContain(route);
    });

    it(`visibleMenuItems_IndependentTeacher_Shows_${route}`, () => {
      expect(routesOf(create(['Teacher'], teacherProfile(true)))).toContain(route);
    });

    it(`visibleMenuItems_FlagUnknown_Shows_${route}`, () => {
      expect(routesOf(create(['Teacher'], teacherProfile(undefined, 7)))).toContain(route);
    });

    it(`routes_${route}_UsesIndependentTeacherGuard`, () => {
      const children = routes.find((r) => Array.isArray(r.children))?.children ?? [];
      const config = children.find((r) => `/${r.path}` === route);
      expect(config?.canActivate).toContain(independentTeacherGuard);
    });
  }
});
