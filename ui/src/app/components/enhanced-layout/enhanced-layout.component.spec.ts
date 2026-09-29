import { WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed, fakeAsync, flush, tick } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { BehaviorSubject, NEVER, of, Subject, throwError } from 'rxjs';

import { EnhancedLayoutComponent } from './enhanced-layout.component';
import { AuthService } from '../../services/auth.service';
import { SignalRService } from '../../services/signalr.service';
import { WorksheetAccessRequestService } from '../../services/worksheet-access-request.service';
import { NotificationService } from '../../services/notification.service';
import { UserThemeService } from '../../services/user-theme.service';
import { ThemeConfigService } from '../../services/theme-config.service';
import { routes } from '../../app.routes';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';

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
    };
    TestBed.configureTestingModule({
      imports: [EnhancedLayoutComponent, translocoTestingModule()],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authStub },
        { provide: SignalRService, useValue: { accessRequestUpdates$: new Subject() } },
        { provide: WorksheetAccessRequestService, useValue: { pendingCount: signal(0) } },
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

  it('navigateTo_AdminListEntries_NavigatesToListRouteAndMarksActive', () => {
    const component = create(['Admin']);
    const router = TestBed.inject(Router);
    const navigateSpy = spyOn(router, 'navigate').and.returnValue(Promise.resolve(true));

    for (const entry of ADMIN_LIST_ENTRIES) {
      component.navigateTo(entry.route);
      expect(navigateSpy).toHaveBeenCalledWith([entry.route], {});
      expect(component.activeMenuItem()).toBe(entry.id);
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

  // ── Issue #287: onaysız öğretmen menüsü ─────────────────────────────────────

  const TEACHER_ONLY_ROUTES = [
    '/dashboard',
    '/tests',
    '/exam',
    '/study-pages',
    '/study-links',
    '/question-transfer',
    '/availability',
    '/booking-requests',
    '/assignment-permission-requests',
    '/my-calendar',
    '/students',
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
    expect(routesShown).toContain('/student-profile');
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
    };
    TestBed.configureTestingModule({
      imports: [EnhancedLayoutComponent, translocoTestingModule()],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authStub },
        { provide: SignalRService, useValue: { accessRequestUpdates$: new Subject() } },
        { provide: WorksheetAccessRequestService, useValue: { pendingCount: signal(0) } },
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
