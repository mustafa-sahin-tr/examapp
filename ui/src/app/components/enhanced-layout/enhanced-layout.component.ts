import { Component, signal, OnInit, OnDestroy, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatMenuModule } from '@angular/material/menu';
import { MatDividerModule } from '@angular/material/divider';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterOutlet, Router } from '@angular/router';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { AuthService } from '../../services/auth.service';
import { UserThemeService } from '../../services/user-theme.service';
import { ThemeConfigService } from '../../services/theme-config.service';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';

interface MenuItem {
  id: string;
  name: string;
  icon: string;
  route: string;
  type: 'menu' | 'divider';
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

  // Signals for state management
  isSidenavCollapsed = signal(false);
  activeMenuItem = signal('dashboard');
  isSearchFocused = signal(false);
  authService = inject(AuthService);
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
  currentSection = signal<'newest' | 'hot' | 'completed' | 'search' | 'relevant'>('newest');

  // Menu items
  menuItems: MenuItem[] = [
    { id: 'dashboard', name: 'Dashboard', icon: 'dashboard', route: '/dashboard', type: 'menu' },
    { id: 'exams', name: 'Sınavlar', icon: 'quiz', route: '/tests', type: 'menu' },
    { id: 'study', name: 'Ders Çalışma', icon: 'school', route: '/study', type: 'menu' },
    { id: 'programsm', name: 'Programlarım', icon: 'assignment_ind', route: '/programs', type: 'menu' },
    { id: 'students', name: 'Öğrenciler', icon: 'people', route: '/students', type: 'menu' },
    { id: 'divider1', name: '', icon: '', route: '', type: 'divider' },
    { id: 'exam', name: 'Test Ekleme', icon: 'add_circle', route: '/exam', type: 'menu' },
    { id: 'reports', name: 'Raporlar', icon: 'analytics', route: '/certificates', type: 'menu' },
    { id: 'settings', name: 'Ayarlar', icon: 'settings', route: '/student-profile', type: 'menu' },
    { id: 'divider2', name: '', icon: '', route: '', type: 'divider' },
    { id: 'help', name: 'Yardım', icon: 'support', route: '/help', type: 'menu' },
    { id: 'feedback', name: 'Geri Bildirim', icon: 'feedback', route: '/feedback', type: 'menu' },
  ];
  /*
    menuItems = [
    { type: 'menu', name: 'Sınavlar', icon: 'folder', route: '/tests' },
    { type: 'menu', name: 'Programlarım', icon: 'assignment_ind', route: '/programs' },
    { type: 'menu', name: 'Sertifikalar', icon: 'verified', route: '/certificates' },
    { type: 'menu', name: 'Parkur', icon: 'timeline' },
    { type: 'menu', name: 'Sonuçlar', icon: 'track_changes' },
    { type: 'divider' },
    { type: 'menu', name: 'Destek', icon: 'help' },
    { type: 'menu', name: 'Geri Bildirim', icon: 'feedback' },
    { type: 'divider' },
    { type: 'menu', name: 'Test Ekleme', icon: 'add_circle', route: '/exam' },
  ];*/

  // Computed values

  filteredSuggestions: string[] = [];
  constructor(private router: Router) {}

  ngOnInit() {
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
          if (!res || (profile == 'Student' && !res.student)) {
            this.router.navigate(['/student-register']);
          }
          if (!res || (profile == 'Teacher' && !res.teacher)) {
            this.router.navigate(['/teacher-register']);
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
    this.isSidenavCollapsed.set(!this.isSidenavCollapsed());
    console.log('Sidebar collapsed:', this.isSidenavCollapsed());
  }

  navigateTo(route: string, options: any = {}) {
    if (route) {
      this.router.navigate([route], options);
      const menuItem = this.menuItems.find((item) => item.route === route);
      if (menuItem) {
        this.activeMenuItem.set(menuItem.id);
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

  showSection(section: 'newest' | 'hot' | 'completed' | 'search' | 'relevant') {
    this.currentSection.set(section);
    if (section === 'search') {
      // this.performSearch();
    }
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
