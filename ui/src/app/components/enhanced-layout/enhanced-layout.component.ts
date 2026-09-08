import { Component, signal, OnInit, OnDestroy, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
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
import { Subject } from 'rxjs';
import { filter, map, takeUntil } from 'rxjs/operators';
import { SidenavService } from '../../services/sidenav.service';
import { SignalRService } from '../../services/signalr.service';
import { WorksheetAccessRequestService } from '../../services/worksheet-access-request.service';

interface MenuItem {
  id: string;
  name: string;
  icon: string;
  route: string;
  type: 'menu' | 'divider';
  /** Realm roles allowed to see this item. Omitted = visible to every role. */
  roles?: string[];
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
  activeMenuItem = signal('dashboard');
  isSearchFocused = signal(false);
  authService = inject(AuthService);
  private readonly isTeacher = this.authService.hasRealmRole('Teacher');
  private readonly signalR = inject(SignalRService);
  private readonly accessRequestService = inject(WorksheetAccessRequestService);
  readonly accessRequestCount = this.accessRequestService.pendingCount;
  userThemeService = inject(UserThemeService);
  themeConfigService = inject(ThemeConfigService);
  // Search functionality
  searchControl = new FormControl('');
  searchSuggestions = signal<string[]>(['Dashboard', 'Sınavlar', 'Sorular', 'Öğrenciler', 'Raporlar']);
  globalSearchControl = new FormControl('');
  // User info
  userName = signal('Mustafa Sahin');
  userEmail = signal('mustafa@examapp.com');
  userAvatarUrl = signal('');
  userThemePreset = signal<string>('standard');
  userThemeCustomConfig = signal<string | null>(null);
  isInputFocused: boolean = false;
  searchHistory: string[] = ['Matematik', 'Türkçe', 'Hayat Bilgisi', 'Fen Bilimler'];
  isAuthenticated = this.authService.isAuthenticated();
  // Örnek öneri listesi (tüm öneriler)
  allSuggestions: string[] = ['Doğal Sayılar', 'Gezegenimiz', 'Çarpma', 'Zıt Anlamlı'];
  currentSection = signal<'newest' | 'hot'>('newest');

  // Realm roles the current user holds (used to filter the menu).
  private readonly userRoles = ['Student', 'Teacher', 'Admin'].filter((r) =>
    this.authService.hasRealmRole(r)
  );

  // Menu items — single source of truth. `roles` omitted = visible to every role.
  menuItems: MenuItem[] = [
    { id: 'dashboard', name: 'Dashboard', icon: 'dashboard', route: '/dashboard', type: 'menu', roles: ['Student', 'Teacher'] },
    { id: 'exams', name: 'Sınavlar', icon: 'quiz', route: '/tests', type: 'menu', roles: ['Student', 'Teacher'] },
    { id: 'practice', name: 'Soru Çöz', icon: 'bolt', route: '/practice', type: 'menu', roles: ['Student'] },
    { id: 'study', name: 'Ders Çalışma', icon: 'school', route: '/study', type: 'menu', roles: ['Student'] },
    { id: 'programsm', name: 'Programlarım', icon: 'assignment_ind', route: '/programs', type: 'menu', roles: ['Student'] },
    { id: 'my-calendar', name: 'Planım', icon: 'event_note', route: '/my-calendar', type: 'menu', roles: ['Student'] },
    { id: 'students', name: 'Öğrenciler', icon: 'people', route: '/students', type: 'menu', roles: ['Teacher'] },
    { id: 'access-requests', name: 'Atama İzin Talepleri', icon: 'how_to_reg', route: '/assignment-permission-requests', type: 'menu', roles: ['Teacher'] },
    { id: 'divider1', name: '', icon: '', route: '', type: 'divider' },
    { id: 'study-pages', name: 'Çalışma Ekleme', icon: 'library_add', route: '/study-pages', type: 'menu', roles: ['Teacher'] },
    { id: 'exam', name: 'Test Ekleme', icon: 'app_registration', route: '/exam', type: 'menu', roles: ['Teacher'] },
    { id: 'questiontransfer', name: 'Soru Transferi', icon: 'swap_horiz', route: '/question-transfer', type: 'menu', roles: ['Teacher'] },
    { id: 'reports', name: 'Raporlar', icon: 'analytics', route: '/certificates', type: 'menu' },
    { id: 'settings', name: 'Ayarlar', icon: 'settings', route: '/student-profile', type: 'menu' },
    { id: 'admin', name: 'Yönetim', icon: 'admin_panel_settings', route: '/admin', type: 'menu', roles: ['Admin'] },
    { id: 'divider2', name: '', icon: '', route: '', type: 'divider' },
    { id: 'help', name: 'Yardım', icon: 'support', route: '/help', type: 'menu' },
    { id: 'feedback', name: 'Geri Bildirim', icon: 'feedback', route: '/feedback', type: 'menu' },
  ];
  // Bottom navigation items for mobile (max 4 primary items + menu trigger)
  bottomNavItems: MenuItem[] = [
    { id: 'dashboard', name: 'Ana Sayfa', icon: 'home', route: '/dashboard', type: 'menu', roles: ['Student', 'Teacher'] },
    { id: 'exams', name: 'Sınavlar', icon: 'quiz', route: '/tests', type: 'menu', roles: ['Student', 'Teacher'] },
    { id: 'study', name: 'Çalışma', icon: 'school', route: '/study', type: 'menu', roles: ['Student'] },
    { id: 'settings', name: 'Ayarlar', icon: 'settings', route: '/student-profile', type: 'menu' },
  ];

  private isItemAllowed(item: MenuItem): boolean {
    return !item.roles || item.roles.some((r) => this.userRoles.includes(r));
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

  readonly visibleMenuItems: MenuItem[] = this.stripDividers(
    this.menuItems.filter((item) => this.isItemAllowed(item))
  );
  readonly visibleBottomNavItems: MenuItem[] = this.bottomNavItems.filter((item) =>
    this.isItemAllowed(item)
  );

  // Computed values

  filteredSuggestions: string[] = [];

  ngOnInit() {
    this.signalR.startConnection();

    if (this.isTeacher) {
      this.accessRequestService.refreshPendingCount().subscribe({ error: () => {} });
      this.signalR.accessRequestUpdates$.pipe(takeUntil(this.destroy$)).subscribe((update) => {
        if (update.kind === 'requested') {
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

    // Set initial active menu item based on current route
    const currentRoute = this.router.url;
    const activeItem = this.menuItems.find((item) => item.route === currentRoute);
    if (activeItem) {
      this.activeMenuItem.set(activeItem.id);
    }

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
    this.setUserInfo();

    var refresh =
      !user ||
      (profile == 'Student' && !JSON.parse(user).student) ||
      (profile == 'Teacher' && !JSON.parse(user).teacher);
    if (refresh) {
      this.authService.refresh().subscribe({
        next: (res) => {
          if (res) {
            localStorage.setItem('user', JSON.stringify(res));
            this.setUserInfo();
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

  public setUserInfo() {
    var user = localStorage.getItem('user');
    if (user) {
      const userObj = JSON.parse(user);
      this.userName.set(userObj.fullName || 'Kullanıcı');
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
      const menuItem = this.menuItems.find((item) => item.route === route);
      if (menuItem) {
        this.activeMenuItem.set(menuItem.id);
      }

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
  onProfile() {
    this.router.navigate(['/profile']);
  }

  onAccountSettings() {
    this.router.navigate(['/settings/account']);
  }

  onNotifications() {
    this.router.navigate(['/notifications']);
  }

  onSupport() {
    this.router.navigate(['/support']);
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

        // LocalStorage'ı güncelle
        localStorage.setItem('user', JSON.stringify(userObj));
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
