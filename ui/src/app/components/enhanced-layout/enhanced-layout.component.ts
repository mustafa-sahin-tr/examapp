import { Component, signal, OnInit, OnDestroy, computed, inject } from '@angular/core';
import { CommonModule, DOCUMENT } from '@angular/common';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatMenuModule } from '@angular/material/menu';
import { MatBadgeModule } from '@angular/material/badge';
import { MatDividerModule } from '@angular/material/divider';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterOutlet, Router, NavigationEnd } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { BreakpointObserver } from '@angular/cdk/layout';
import { AuthService } from '../../services/auth.service';
import { UserThemeService } from '../../services/user-theme.service';
import { ThemeConfigService } from '../../services/theme-config.service';
import { EMPTY, Subject, fromEvent, merge, of } from 'rxjs';
import { catchError, debounceTime, distinctUntilChanged, filter, map, switchMap, takeUntil, throttleTime } from 'rxjs/operators';
import { SidenavService } from '../../services/sidenav.service';
import { TEACHER_APPROVAL_PENDING_URL, teacherAccountApprovalOf } from '../../models/teacher-approval.model';
import { SignalRService } from '../../services/signalr.service';
import { WorksheetAccessRequestService } from '../../services/worksheet-access-request.service';
import { StudentSchoolRequestService } from '../../services/student-school-request.service';
import { NotificationService } from '../../services/notification.service';
import { DirectMessageService } from '../../services/direct-message.service';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ColorSchemeToggleComponent } from '../../shared/components/color-scheme-toggle/color-scheme-toggle.component';
import { LanguageSwitcherComponent } from '../../shared/components/language-switcher/language-switcher.component';
import { resolveActiveMenuItemId } from './active-menu-item';
import { SETTINGS_URL, settingsUrlFor } from '../../shared/guards/settings.guard';

interface MenuItem {
  id: string;
  /** Kök sözlükteki `layout.menu.*` anahtarı (issue #183). Ayırıcılarda boş. */
  labelKey: string;
  icon: string;
  route: string;
  type: 'menu' | 'divider';
  /** Realm roles allowed to see this item. Omitted = visible to every role. */
  roles?: string[];
  /** Issue #287: yalnız hesabı onaylanmamış öğretmene görünür (başvuru durumu girişi). */
  onlyUnapprovedTeacher?: boolean;
  /**
   * Issue #287: Teacher'a özel olduğu hâlde onay bekleyen öğretmene de açık (backend izin verir) — ör. özel ders
   * profili, bağımsız öğretmen başvurusunun formudur. Başvuru türü profilde olmadığından tüm onaysızlara gösterilir.
   */
  allowUnapprovedTeacher?: boolean;
  /**
   * Issue #384: yalnız bağımsız öğretmene görünür — profilde `teacher.isIndependentTutor === false` (backend kuralı)
   * BİLİNEN öğretmende gizlenir. Bayrak henüz yoksa gösterilir; asıl kapı backend ve `independentTeacherGuard`.
   */
  onlyIndependentTeacher?: boolean;
}

@Component({
  selector: 'app-enhanced-layout',
  standalone: true,
  imports: [
    CommonModule,
    MatSidenavModule,
    MatToolbarModule,
    MatIconModule,
    MatButtonModule,
    MatInputModule,
    MatFormFieldModule,
    MatMenuModule,
    MatBadgeModule,
    MatDividerModule,
    MatTooltipModule,
    RouterOutlet,
    ReactiveFormsModule,
    ColorSchemeToggleComponent,
    LanguageSwitcherComponent,
    TranslocoDirective,
  ],
  templateUrl: './enhanced-layout.component.html',
  styleUrls: ['./enhanced-layout.component.scss'],
})
export class EnhancedLayoutComponent implements OnInit, OnDestroy {
  // Cleanup subject for subscriptions
  private destroy$ = new Subject<void>();
  private static readonly MOBILE_LAYOUT_QUERY =
    '(max-width: 768px), ((max-height: 500px) and (orientation: landscape))';
  /** `.enhanced-sidenav { width }` ile aynı tutulmalı (enhanced-layout.component.scss). */
  private static readonly SIDENAV_WIDTH_EXPANDED = 280;
  /** `.enhanced-sidenav.collapsed { width }` ile aynı tutulmalı (enhanced-layout.component.scss). */
  private static readonly SIDENAV_WIDTH_COLLAPSED = 72;

  // Signals for state management
  private readonly router = inject(Router);
  private readonly sidenavService = inject(SidenavService);
  private readonly breakpointObserver = inject(BreakpointObserver);
  /** Aktif URL (redirect sonrası). NavigationEnd ile güncellenir; ilk değer mevcut URL. */
  private readonly currentUrl = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects)
    ),
    { initialValue: this.router.url }
  );
  /**
   * Sınav çözme ekranı (/testsolve/...) aktif mi. Bu ekranın kendi alt dock'u var;
   * genel mobil alt nav aynı alanı paylaşıp dock'u kapatıyordu (issue #72), bu yüzden gizlenir.
   */
  readonly isExamRoute = computed(() => this.currentUrl().startsWith('/testsolve'));
  isMobile = signal(false);
  isMobileSidenavOpen = signal(false);
  isSidenavCollapsed = computed(() => !this.isMobile() && this.sidenavService.isSidenavCollapsed());
  isFullScreen = computed(() => this.sidenavService.isFullScreen());
  /**
   * Sidenav'ın kapladığı gerçek piksel genişliği — içerik margin'i buradan TEK KAYNAKTAN türetilir
   * ve şablonda `[style.margin-left.px]` ile uygulanır. Material, CSS class'ıyla daralan sidenav
   * genişliğini kendi takip edemediği için margin'i biz hesaplıyoruz.
   * Yeni bir sidenav durumu (yeni bir route/mod) eklendiğinde SADECE bu computed güncellenir;
   * SCSS'te ayrı bir !important override eklemeye gerek kalmaz.
   * Değerler `.enhanced-sidenav` (280px) ve `.enhanced-sidenav.collapsed` (72px) ile birebir aynıdır.
   */
  readonly sidenavContentOffsetPx = computed(() => {
    if (this.isMobile() || this.isExamRoute()) {
      return 0;
    }
    return this.isSidenavCollapsed()
      ? EnhancedLayoutComponent.SIDENAV_WIDTH_COLLAPSED
      : EnhancedLayoutComponent.SIDENAV_WIDTH_EXPANDED;
  });
  isSearchFocused = signal(false);
  authService = inject(AuthService);
  private readonly isTeacher = this.authService.hasRealmRole('Teacher');
  private readonly signalR = inject(SignalRService);
  private readonly accessRequestService = inject(WorksheetAccessRequestService);
  readonly accessRequestCount = this.accessRequestService.pendingCount;
  // Issue #361: bekleyen öğrenci okul başvuruları rozeti (onaylı öğretmen + admin); sayfa kararları sonrası liste tazelemesi
  // de aynı signal'ı günceller.
  private readonly studentSchoolRequestService = inject(StudentSchoolRequestService);
  readonly studentSchoolRequestCount = this.studentSchoolRequestService.pendingCount;
  private studentSchoolRequestCountLoaded = false;
  private readonly notificationService = inject(NotificationService);
  /** Issue #146: zil rozetindeki okunmamış kalıcı bildirim sayısı. */
  readonly unreadNotificationCount = this.notificationService.unreadCount;
  private readonly directMessageService = inject(DirectMessageService);
  private readonly document = inject(DOCUMENT);
  /** Issue #106: DM menü rozeti — okunmamış mesajı olan konuşma sayısı. */
  readonly directMessageUnreadCount = this.directMessageService.unreadCount;
  /** Öğrenci mi — öğrencinin Ayarlar'ı /student-profile (issue #373, #375). */
  readonly isStudent = this.authService.hasRealmRole('Student');
  /**
   * Issue #417: Ayarlar her role görünür; rota guard ile aynı kuraldan (`settingsUrlFor`): Teacher/Admin → /settings,
   * yalnız öğrenci → /student-profile. Roller token'dan okunur ve oturum boyunca değişmez, bu yüzden sabit.
   */
  readonly settingsRoute = settingsUrlFor(this.authService) ?? SETTINGS_URL;
  /** Pencere odağında DM sayacı en sık bu aralıkla tazelenir (polling değil; yalnız odak olayı). */
  static readonly DM_FOCUS_REFRESH_THROTTLE_MS = 30_000;
  /** Issue #106 b: DM push'ları arka arkaya gelirse tek rozet isteği (ms). */
  static readonly DM_PUSH_REFRESH_DEBOUNCE_MS = 400;
  /** Art arda gelen hub push'larını tek sayaç isteğinde birleştirme penceresi. */
  static readonly NOTIFICATION_REFRESH_DEBOUNCE_MS = 1000;
  userThemeService = inject(UserThemeService);
  themeConfigService = inject(ThemeConfigService);
  // Search functionality
  searchControl = new FormControl('');
  globalSearchControl = new FormControl('');
  // User info
  userName = signal('');
  userEmail = signal('');
  userAvatarUrl = signal('');
  userThemePreset = signal<string>('standard');
  userThemeCustomConfig = signal<string | null>(null);
  isInputFocused: boolean = false;
  /**
   * Örnek arama geçmişi/önerileri sözlükten gelir (issue #183): anahtarlar sabit, metinler aktif
   * dile göre `layout.search.*` altından çözülür. Kullanıcının yazdığı aramalar da aynı listeye
   * eklendiği için dizi anahtar değil, çevrilmiş metin tutar.
   */
  private static readonly SEARCH_HISTORY_KEYS = [
    'layout.search.history.math',
    'layout.search.history.turkish',
    'layout.search.history.life',
    'layout.search.history.science',
  ];
  private static readonly SEARCH_SUGGESTION_KEYS = [
    'layout.search.suggestions.naturalNumbers',
    'layout.search.suggestions.ourPlanet',
    'layout.search.suggestions.multiplication',
    'layout.search.suggestions.antonyms',
  ];
  private readonly transloco = inject(TranslocoService);
  searchHistory: string[] = [];
  isAuthenticated = this.authService.isAuthenticated();
  allSuggestions: string[] = [];
  currentSection = signal<'newest' | 'hot'>('newest');

  // Realm roles the current user holds (used to filter the menu).
  // Issue #419: Parent rolü — veli menü öğeleri.
  private readonly userRoles = ['Student', 'Teacher', 'Admin', 'Parent'].filter((r) =>
    this.authService.hasRealmRole(r)
  );

  /**
   * #421 review: yalnızca veli rolü olan kullanıcı — worksheet keşfine ait üst çubuk öğeleri (Yeni / Popüler, worksheet araması)
   * veliye anlamsız, gizlenir. Başka bir rolü de olan (ör. öğretmen + veli) kullanıcı bunları görmeye devam eder.
   */
  readonly isParentOnly = this.userRoles.length === 1 && this.userRoles[0] === 'Parent';

  // Menu items — single source of truth. `roles` omitted = visible to every role.
  // `labelKey` kök sözlükteki `layout.*` anahtarıdır; metin şablonda çevrilir (issue #183).
  menuItems: MenuItem[] = [
    // Issue #287: onaysız öğretmenin tek öğretmen girişi — öğretmene özel diğer tüm öğeler gizlenir.
    { id: 'teacher-approval-status', labelKey: 'menu.teacherApprovalStatus', icon: 'hourglass_top', route: TEACHER_APPROVAL_PENDING_URL, type: 'menu', onlyUnapprovedTeacher: true },
    { id: 'dashboard', labelKey: 'menu.dashboard', icon: 'dashboard', route: '/dashboard', type: 'menu', roles: ['Student', 'Teacher'] },
    { id: 'exams', labelKey: 'menu.exams', icon: 'quiz', route: '/tests', type: 'menu', roles: ['Student', 'Teacher'] },
    { id: 'practice', labelKey: 'menu.practice', icon: 'bolt', route: '/practice', type: 'menu', roles: ['Student'] },
    // Issue #382: 'study' (/study, Ders Çalışma) örnek veriyle çalıştığı için menüden ve alt navigasyondan kaldırıldı.
    { id: 'programsm', labelKey: 'menu.programs', icon: 'assignment_ind', route: '/programs', type: 'menu', roles: ['Student'] },
    { id: 'my-calendar', labelKey: 'menu.myCalendar', icon: 'event_note', route: '/my-calendar', type: 'menu', roles: ['Student', 'Teacher'] },
    { id: 'tutors', labelKey: 'menu.tutors', icon: 'person_search', route: '/tutors', type: 'menu', roles: ['Student'] },
    { id: 'my-bookings', labelKey: 'menu.myBookings', icon: 'event_available', route: '/my-bookings', type: 'menu', roles: ['Student'] },
    // Issue #106: öğrenci → öğretmen mesajlaşma (booking ile aynı "öğrenci → öğretmen" ailesi).
    { id: 'teacher-messages', labelKey: 'menu.teacherMessages', icon: 'forum', route: '/teacher-messages', type: 'menu', roles: ['Student'] },
    // Issue #420: veli paneli (çocuk özeti) — velinin ana sayfası.
    { id: 'parent-home', labelKey: 'menu.parentHome', icon: 'home', route: '/parent', type: 'menu', roles: ['Parent'] },
    // Issue #419: velinin çocukları (davet koduyla ekleme + liste).
    { id: 'my-children', labelKey: 'menu.myChildren', icon: 'family_restroom', route: '/my-children', type: 'menu', roles: ['Parent'] },
    { id: 'tutor-profile', labelKey: 'menu.tutorProfile', icon: 'cast_for_education', route: '/tutor-profile', type: 'menu', roles: ['Teacher'], allowUnapprovedTeacher: true, onlyIndependentTeacher: true },
    { id: 'availability', labelKey: 'menu.availability', icon: 'event_available', route: '/availability', type: 'menu', roles: ['Teacher'], onlyIndependentTeacher: true },
    { id: 'booking-requests', labelKey: 'menu.bookingRequests', icon: 'inbox', route: '/booking-requests', type: 'menu', roles: ['Teacher'], onlyIndependentTeacher: true },
    { id: 'access-requests', labelKey: 'menu.accessRequests', icon: 'how_to_reg', route: '/assignment-permission-requests', type: 'menu', roles: ['Teacher'] },
    // Issue #361: okulunu seçen öğrencilerin üyelik onayı (bağımsız öğretmende liste boş döner).
    { id: 'student-school-requests', labelKey: 'menu.studentSchoolRequests', icon: 'person_add', route: '/student-school-requests', type: 'menu', roles: ['Teacher'] },
    // Issue #106: öğretmenin öğrenci mesajları gelen kutusu.
    { id: 'student-messages', labelKey: 'menu.studentMessages', icon: 'forum', route: '/student-messages', type: 'menu', roles: ['Teacher'] },
    { id: 'divider1', labelKey: '', icon: '', route: '', type: 'divider' },
    { id: 'study-pages', labelKey: 'menu.studyPages', icon: 'library_add', route: '/study-pages', type: 'menu', roles: ['Teacher'] },
    // Issue #61: öğretmen konu/alt konu çalışma linkleri yönetimi.
    { id: 'study-links', labelKey: 'menu.studyLinks', icon: 'video_library', route: '/study-links', type: 'menu', roles: ['Teacher'] },
    { id: 'exam', labelKey: 'menu.examAuthoring', icon: 'app_registration', route: '/exam', type: 'menu', roles: ['Teacher'] },
    { id: 'reports', labelKey: 'menu.reports', icon: 'analytics', route: '/certificates', type: 'menu' },
    // Issue #417: tüm roller (onay bekleyen öğretmen dahil — dil/hesap ayarı); rota role göre (#373: öğrenci /student-profile).
    { id: 'settings', labelKey: 'menu.settings', icon: 'settings', route: this.settingsRoute, type: 'menu' },
    { id: 'admin-dashboard', labelKey: 'menu.dashboard', icon: 'insights', route: '/admin/dashboard', type: 'menu', roles: ['Admin'] },
    { id: 'admin', labelKey: 'menu.admin', icon: 'admin_panel_settings', route: '/admin', type: 'menu', roles: ['Admin'] },
    { id: 'admin-teacher-approvals', labelKey: 'menu.teacherApprovals', icon: 'how_to_reg', route: '/admin/teacher-approvals', type: 'menu', roles: ['Admin'] },
    { id: 'admin-teachers', labelKey: 'menu.adminTeachers', icon: 'groups', route: '/admin/teachers', type: 'menu', roles: ['Admin'] },
    { id: 'admin-students', labelKey: 'menu.adminStudents', icon: 'school', route: '/admin/students', type: 'menu', roles: ['Admin'] },
    // Issue #361: tüm okulların bekleyen öğrenci okul üyelikleri.
    { id: 'admin-student-school-requests', labelKey: 'menu.studentSchoolRequests', icon: 'person_add', route: '/admin/student-school-requests', type: 'menu', roles: ['Admin'] },
    { id: 'admin-schools', labelKey: 'menu.adminSchools', icon: 'apartment', route: '/admin/schools', type: 'menu', roles: ['Admin'] },
    // Issue #148: rozet tanımları yönetimi.
    { id: 'admin-badge-definitions', labelKey: 'menu.adminBadgeDefinitions', icon: 'military_tech', route: '/admin/badge-definitions', type: 'menu', roles: ['Admin'] },
    // Issue #305: yorum şikayetleri (moderasyon).
    { id: 'admin-comment-reports', labelKey: 'menu.adminCommentReports', icon: 'flag', route: '/admin/comment-reports', type: 'menu', roles: ['Admin'] },
    // Issue #365: soru aktarımı (export/import) yalnız Admin.
    { id: 'questiontransfer', labelKey: 'menu.questionTransfer', icon: 'swap_horiz', route: '/question-transfer', type: 'menu', roles: ['Admin'] },
  ];
  // Bottom navigation items for mobile (max 4 primary items + menu trigger)
  bottomNavItems: MenuItem[] = [
    { id: 'teacher-approval-status', labelKey: 'menu.teacherApprovalStatus', icon: 'hourglass_top', route: TEACHER_APPROVAL_PENDING_URL, type: 'menu', onlyUnapprovedTeacher: true },
    { id: 'dashboard', labelKey: 'bottomNav.home', icon: 'home', route: '/dashboard', type: 'menu', roles: ['Student', 'Teacher'] },
    { id: 'exams', labelKey: 'menu.exams', icon: 'quiz', route: '/tests', type: 'menu', roles: ['Student', 'Teacher'] },
    { id: 'parent-home', labelKey: 'bottomNav.home', icon: 'home', route: '/parent', type: 'menu', roles: ['Parent'] },
    { id: 'my-children', labelKey: 'menu.myChildren', icon: 'family_restroom', route: '/my-children', type: 'menu', roles: ['Parent'] },
    // Issue #417: tüm roller (onay bekleyen öğretmen dahil — dil/hesap ayarı); rota role göre (#373: öğrenci /student-profile).
    { id: 'settings', labelKey: 'menu.settings', icon: 'settings', route: this.settingsRoute, type: 'menu' },
  ];

  /**
   * Issue #287: onaysız öğretmende Teacher rolü öğe görünürlüğü için sayılmaz — yalnız Teacher'a (veya Teacher'la
   * birlikte başka rollere) açılan öğeler, kullanıcının başka bir rolü onları açmıyorsa gizlenir.
   */
  private isItemAllowed(item: MenuItem, unapprovedTeacher: boolean, notIndependent: boolean): boolean {
    if (item.onlyUnapprovedTeacher) {
      return unapprovedTeacher;
    }
    if (item.onlyIndependentTeacher && notIndependent) {
      return false;
    }
    if (!item.roles) {
      return true;
    }
    const roles =
      unapprovedTeacher && !item.allowUnapprovedTeacher ? this.userRoles.filter((r) => r !== 'Teacher') : this.userRoles;
    return item.roles.some((r) => roles.includes(r));
  }

  /** Drop leading/trailing dividers and collapse consecutive ones. */
  private stripDividers(items: MenuItem[]): MenuItem[] {
    const result: MenuItem[] = [];
    for (const item of items) {
      if (item.type === 'divider') {
        if (result.length === 0 || result[result.length - 1].type === 'divider') {
          continue;
        }
      }
      result.push(item);
    }
    while (result.length && result[result.length - 1].type === 'divider') {
      result.pop();
    }
    return result;
  }

  /** Onay durumu profil yenilenince değişebildiği için menü reaktif (issue #287). */
  readonly visibleMenuItems = computed<MenuItem[]>(() => {
    const unapprovedTeacher = this.authService.isUnapprovedTeacher();
    const notIndependent = this.isKnownNotIndependent();
    return this.stripDividers(
      this.menuItems.filter((item) => this.isItemAllowed(item, unapprovedTeacher, notIndependent))
    );
  });
  readonly visibleBottomNavItems = computed<MenuItem[]>(() => {
    const unapprovedTeacher = this.authService.isUnapprovedTeacher();
    const notIndependent = this.isKnownNotIndependent();
    return this.bottomNavItems.filter((item) => this.isItemAllowed(item, unapprovedTeacher, notIndependent));
  });

  /** Issue #384: profil bağımsız olmadığını söylüyor mu (reaktif — `user` signal'ı profil yenilenince değişir). */
  private isKnownNotIndependent(): boolean {
    return AuthService.isIndependentTutorOf(this.authService.user()) === false;
  }

  /**
   * Issue #385: seçili menü öğesi her gezinmede (link, geri/ileri, yenileme) URL'den türetilir; detay sayfaları üst
   * öğeyi seçer, karşılığı olmayan sayfada hiçbiri seçili değildir. Mobil alt menü aynı id'leri kullanır.
   */
  readonly activeMenuItem = computed(() => resolveActiveMenuItemId(this.currentUrl(), this.visibleMenuItems()));

  /** Öğretmen ve hesabının onaylı olduğu profilden BİLİNİYOR (bilinmeyen durum onaysız gibi ele alınır). */
  private isTeacherKnownApproved(): boolean {
    return (
      this.isTeacher &&
      !this.authService.isUnapprovedTeacher() &&
      teacherAccountApprovalOf(this.authService.user()) === true
    );
  }

  /** Atama izin talebi sayacı istendi mi (issue #287: yalnız onaylı öğretmen için, bir kez). */
  private accessRequestCountLoaded = false;

  /**
   * Sayaç ucu (access-requests/incoming/count) onaysız öğretmene 403 `TeacherNotApproved` döner ve interceptor
   * kullanıcıyı durum sayfasına taşır — bu yüzden yalnız onayı BİLİNEN öğretmende çağrılır (issue #287).
   */
  private loadAccessRequestCountIfApproved(): void {
    if (!this.isTeacherKnownApproved() || this.accessRequestCountLoaded) {
      return;
    }
    this.accessRequestCountLoaded = true;
    this.accessRequestService.refreshPendingCount().subscribe({ error: () => {} });
    this.loadStudentSchoolRequestCount();
    // Issue #106: onay bilgisi geç geldiyse (init'te bilinmiyordu) DM rozeti şimdi çekilir — init çektiyse tekrar etmez.
    if (!this.directMessageBadgeLoaded) {
      this.directMessageBadgeLoaded = true;
      this.directMessageService.refreshUnreadCount('Teacher').subscribe({ error: () => {} });
    }
  }

  /**
   * Issue #361: bekleyen öğrenci okul başvurusu sayısı — onaylı öğretmen (onay bilindiğinde) ve admin için bir kez çekilir;
   * sonrası başvuru sayfasının liste yüklemeleriyle güncellenir. Hata sessizce yutulur (rozet 0 kalır).
   */
  private loadStudentSchoolRequestCount(): void {
    if (this.studentSchoolRequestCountLoaded) {
      return;
    }
    this.studentSchoolRequestCountLoaded = true;
    this.studentSchoolRequestService.refreshPendingCount().subscribe({ error: () => {} });
  }

  /** Issue #106: DM menü öğesi mi (rozete ekran okuyucu açıklaması verilir). */
  isDirectMessageItem(itemId: string): boolean {
    return itemId === 'teacher-messages' || itemId === 'student-messages';
  }

  /** Menü öğesinin rozet sayısı (0 → gizli). */
  menuBadgeCount(itemId: string): number {
    switch (itemId) {
      case 'access-requests':
        return this.accessRequestCount();
      case 'student-school-requests':
      case 'admin-student-school-requests':
        return this.studentSchoolRequestCount();
      case 'teacher-messages':
      case 'student-messages':
        return this.directMessageUnreadCount();
      default:
        return 0;
    }
  }

  /**
   * Issue #106 dilim (c): DM okunmamış rozeti girişte ve pencere odağında (en sık 30 sn'de bir) tazelenir — polling yok.
   * Öğretmende yalnız onayı bilinen hesapta (gelen kutusu ucu onaysıza 403). Çıkışta sıfırlanır.
   * Dilim (b): SignalR DM push'u (`directMessageReceived$`) geldiğinde aynı `refreshUnreadCount` çağrılır (polling yok).
   */
  /** Öğretmen DM rozeti ilk kez çekildi mi (init ve onay-geç-geldi yolları çift istek atmasın). */
  private directMessageBadgeLoaded = false;

  private initDirectMessageBadge(): void {
    const view = this.document.defaultView;
    if (!this.isStudent && !this.isTeacher) {
      return;
    }
    this.isAuthenticated
      .pipe(
        distinctUntilChanged(),
        switchMap((authenticated) => {
          if (!authenticated) {
            this.directMessageService.resetUnreadCount();
            return EMPTY;
          }
          const focus$ = view
            ? fromEvent(view, 'focus').pipe(throttleTime(EnhancedLayoutComponent.DM_FOCUS_REFRESH_THROTTLE_MS))
            : EMPTY;
          // Issue #106 dilim b: SignalR DM push'u anında rozeti tazeler; ardışık push'lar tek istekte birleşir (debounce).
          const push$ = this.signalR.directMessageReceived$.pipe(debounceTime(EnhancedLayoutComponent.DM_PUSH_REFRESH_DEBOUNCE_MS));
          return merge(of(undefined), focus$, push$).pipe(
            switchMap(() => {
              if (this.isStudent) {
                return this.directMessageService.refreshUnreadCount('Student').pipe(catchError(() => EMPTY));
              }
              if (!this.isTeacherKnownApproved()) {
                return EMPTY;
              }
              this.directMessageBadgeLoaded = true;
              return this.directMessageService.refreshUnreadCount('Teacher').pipe(catchError(() => EMPTY));
            })
          );
        }),
        takeUntil(this.destroy$)
      )
      .subscribe();
  }

  // Computed values

  filteredSuggestions: string[] = [];

  ngOnInit() {
    this.signalR.startConnection();
    this.initNotificationBadge();
    this.initDirectMessageBadge();

    // Sözlük yüklendiğinde (ve dil değiştiğinde) örnek arama metinlerini tazele.
    this.transloco.langChanges$.pipe(takeUntil(this.destroy$)).subscribe(() => {
      this.searchHistory = this.translateAll(EnhancedLayoutComponent.SEARCH_HISTORY_KEYS);
      this.allSuggestions = this.translateAll(EnhancedLayoutComponent.SEARCH_SUGGESTION_KEYS);
      this.filteredSuggestions = [...this.searchHistory];
    });

    // Issue #361: admin için bekleyen öğrenci okul başvurusu rozeti (öğretmende onay bilinince çekilir).
    if (this.authService.hasRealmRole('Admin')) {
      this.loadStudentSchoolRequestCount();
    }

    if (this.isTeacher) {
      this.signalR.accessRequestUpdates$.pipe(takeUntil(this.destroy$)).subscribe((update) => {
        if (update.kind === 'requested' && this.isTeacherKnownApproved()) {
          this.accessRequestService.refreshPendingCount().subscribe({ error: () => {} });
        }
      });
    }

    this.breakpointObserver
      .observe([EnhancedLayoutComponent.MOBILE_LAYOUT_QUERY])
      .pipe(takeUntil(this.destroy$))
      .subscribe((state) => {
        this.isMobile.set(state.matches);
        if (!state.matches) {
          this.isMobileSidenavOpen.set(false);
        }
      });

    // Abone olarak değer değişimini takip et
    this.globalSearchControl.valueChanges.subscribe((value) => {
      // Eğer input boşsa, geçmiş aramalar gösterilsin
      if (!value || !value.trim()) {
        this.filteredSuggestions = [...this.searchHistory];
      } else {
        // Girilen değere göre öneriler filtrelensin (küçük/büyük harf duyarsız)
        const filterValue = value.toLowerCase();
        this.filteredSuggestions = this.allSuggestions.filter((item) => item.toLowerCase().includes(filterValue));
      }
    });

    var profile = localStorage.getItem('user_role');
    var user = localStorage.getItem('user');
    // Önbellekteki profil başka bir kullanıcıya aitse (A çıkıp B girdiğinde) hiç gösterme,
    // refresh cevabı gelene kadar başlık boş kalsın.
    const cachedUserIsCurrent = this.authService.isCachedUserCurrent();
    if (!cachedUserIsCurrent) {
      this.authService.clearCachedUser();
      this.userName.set('');
      this.userEmail.set('');
      this.userAvatarUrl.set('');
    } else {
      this.setUserInfo();
      this.loadAccessRequestCountIfApproved();
    }

    var refresh =
      !cachedUserIsCurrent ||
      !user ||
      (profile == 'Student' && !JSON.parse(user).student) ||
      (profile == 'Teacher' && !JSON.parse(user).teacher) ||
      // Issue #287: onay durumu yalnız refresh ile gelir; onaylı olduğu bilinmeyen öğretmende her açılışta tazelenir
      // (admin onayı sonrası menü/erişim açılsın).
      (profile == 'Teacher' && JSON.parse(user).teacher?.teacherAccountApproved !== true) ||
      // Issue #361: okul üyeliği onay bekleyen öğrencide her açılışta tazelenir (onay sonrası band kalksın, okul kapsamı açılsın).
      (profile == 'Student' && JSON.parse(user).student?.pendingSchoolId != null);
    if (refresh) {
      // Reaktif profil kaynağı (issue #191): schoolId ve öğretmen onay durumu (#287) yalnızca bu refresh ile gelir;
      // `refreshProfile` sonucu `user` signal'ına yazar.
      this.authService.refreshProfile().subscribe({
        next: (res) => {
          if (res) {
            this.setUserInfo();
            this.loadAccessRequestCountIfApproved();
          }
          if (!res) {
            this.router.navigate(['/register']);
          } else if (profile == 'Student' && !res.student) {
            this.router.navigate(['/register'], { queryParams: { role: 'student' } });
          } else if (profile == 'Teacher' && !res.teacher) {
            this.router.navigate(['/register'], { queryParams: { role: 'teacher' } });
          }
        },
        error: (err) => {
          console.error('Error refreshing token:', err);
          // Handle error (e.g., redirect to login)
          this.authService.goLogin();
        },
      });
    }

    // UserThemeService'den theme değişikliklerini dinle
    this.userThemeService.userTheme$.pipe(takeUntil(this.destroy$)).subscribe((themeData) => {
      if (themeData) {
        this.userThemePreset.set(themeData.themePreset);
        this.userThemeCustomConfig.set(themeData.themeCustomConfig || null);
        this.updateUserProfileInLocalStorage(themeData.themePreset, themeData.themeCustomConfig || null);
        this.applyUserTheme();
      }
    });
  }

  /**
   * Issue #146: okunmamış sayacı oturum açılınca çekilir, sonra yalnızca kalıcı bildirim üreten hub push'larında
   * (debounce ile) tazelenir — periyodik yoklama yapılmaz. Oturum kapanınca sayaç sıfırlanır (önceki kullanıcının
   * sayısı görünmez). Hata sessizce yutulur (rozet eski değerde kalır).
   */
  private initNotificationBadge(): void {
    this.isAuthenticated
      .pipe(
        distinctUntilChanged(),
        switchMap((authenticated) => {
          if (!authenticated) {
            this.notificationService.resetUnreadCount();
            return EMPTY;
          }
          return merge(
            of(undefined),
            this.signalR.notificationsChanged$.pipe(debounceTime(EnhancedLayoutComponent.NOTIFICATION_REFRESH_DEBOUNCE_MS))
          ).pipe(switchMap(() => this.notificationService.refreshUnreadCount().pipe(catchError(() => EMPTY))));
        }),
        takeUntil(this.destroy$)
      )
      .subscribe();
  }

  private translateAll(keys: readonly string[]): string[] {
    return keys.map((key) => this.transloco.translate<string>(key) ?? '');
  }

  public setUserInfo() {
    var user = localStorage.getItem('user');
    if (user) {
      const userObj = JSON.parse(user);
      this.userName.set(userObj.fullName || this.transloco.translate<string>('layout.profile.fallbackName') || '');
      this.userEmail.set(userObj.email || '');
      this.userAvatarUrl.set(userObj.avatar || '');

      // Theme bilgisini user profile'dan al
      let themePreset = 'standard';
      let themeCustomConfig = null;

      if (userObj.student?.themePreset) {
        themePreset = userObj.student.themePreset;
        themeCustomConfig = userObj.student.themeCustomConfig;
      } else if (userObj.teacher?.themePreset) {
        themePreset = userObj.teacher.themePreset;
        themeCustomConfig = userObj.teacher.themeCustomConfig;
      }

      this.userThemePreset.set(themePreset);
      this.userThemeCustomConfig.set(themeCustomConfig);

      // LocalStorage'daki user objesini theme bilgisiyle güncelle
      this.updateUserProfileInLocalStorage(themePreset, themeCustomConfig);

      // Theme config service'i güncelle
      this.applyUserTheme();

      // UserThemeService'e bildir
      this.userThemeService.notifyThemeChange(themePreset, themeCustomConfig);
    }
  }

  toggleSidenav() {
    // Sınav ekranında sidenav her zaman kapalı; açma denemesi yok sayılır.
    if (this.isExamRoute()) {
      return;
    }

    if (this.isMobile()) {
      this.isMobileSidenavOpen.update((value) => !value);
      return;
    }

    this.sidenavService.toggleSidenav();
  }

  navigateTo(route: string, options: any = {}) {
    if (route) {
      this.router.navigate([route], options);

      if (this.isMobile()) {
        this.isMobileSidenavOpen.set(false);
      }
    }
  }

  onGlobalSearch() {
    const query = this.globalSearchControl.value?.trim() || '';
    this.navigateTo('/tests', { queryParams: { search: query } });
    // this.router.navigate(['/tests'], { queryParams: { search: query } });

    // Aramayı search history'e ekle (varsa yinelenmeyen)
    if (query && !this.searchHistory.includes(query)) {
      this.searchHistory.unshift(query);
    }
  }

  onSearchFocus() {
    this.isSearchFocused.set(true);
  }

  onSearchBlur() {
    setTimeout(() => {
      this.isSearchFocused.set(false);
    }, 200);
  }

  onSearch() {
    const searchValue = this.searchControl.value;
    if (searchValue) {
      console.log('Searching for:', searchValue);
      // Implement search logic here
    }
  }

  onSelectSuggestion(suggestion: string) {
    this.globalSearchControl.setValue(suggestion);
    this.onGlobalSearch();
  }

  logout() {
    console.log('Logging out...');
    // Implement logout logic here
    window.location.href = '/app/logout';
  }

  // Profile menu actions
  onNotifications() {
    this.router.navigate(['/notifications']);
  }

  // Track by function for ngFor performance
  trackByItemId(index: number, item: MenuItem): string {
    return item.id;
  }

  onFocus() {
    this.isInputFocused = true;
    const value = this.globalSearchControl.value;
    // if (!value || !value.trim()) {
    //   this.filteredSuggestions
    // }
  }

  onDelete(item: string) {
    this.searchHistory = this.searchHistory.filter((i) => i !== item);
    // this.filteredSuggestions = this.filteredSuggestions.filter((i) => i !== item);
  }

  // Blur olduğunda belirli bir gecikme sonrası listeyi kapat (click eventi için)
  onBlur() {
    setTimeout(() => {
      this.isInputFocused = false;
    }, 150);
  }

  showSection(section: 'newest' | 'hot') {
    this.currentSection.set(section);
    // Header'daki Yeni / Popüler butonları test listesine yönlendirir.
    // Yeni  -> en güncel sınavlar üstte
    // Popüler -> son zamanlarda öğrenciler tarafından çözülenler (öğrencinin sınıfına göre)
    this.navigateTo('/tests', {
      queryParams: { section: section === 'hot' ? 'popular' : 'newest' },
    });
  }

  private updateUserProfileInLocalStorage(themePreset: string, themeCustomConfig: string | null) {
    const user = localStorage.getItem('user');
    if (user) {
      try {
        const userObj = JSON.parse(user);

        // Theme bilgilerini student veya teacher objesine ekle
        if (userObj.student) {
          userObj.student.themePreset = themePreset;
          userObj.student.themeCustomConfig = themeCustomConfig;
        } else if (userObj.teacher) {
          userObj.teacher.themePreset = themePreset;
          userObj.teacher.themeCustomConfig = themeCustomConfig;
        }

        // LocalStorage'ı ve `user` signal'ını güncelle
        this.authService.setUser(userObj);
      } catch (error) {
        console.warn('Failed to update user profile in localStorage:', error);
      }
    }
  }

  private applyUserTheme() {
    const themePreset = this.userThemePreset();
    const customConfig = this.userThemeCustomConfig();

    if (customConfig) {
      try {
        const parsedConfig = JSON.parse(customConfig);
        this.themeConfigService.setCustomTheme(parsedConfig);
      } catch (error) {
        console.warn('Invalid custom theme config, using preset:', error);
        this.themeConfigService.setTheme(themePreset as any);
      }
    } else {
      this.themeConfigService.setTheme(themePreset as any);
    }
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
