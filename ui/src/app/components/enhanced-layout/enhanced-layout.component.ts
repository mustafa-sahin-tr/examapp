import { Component, signal, OnInit, computed, inject } from '@angular/core';
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
export class EnhancedLayoutComponent implements OnInit {
  // Signals for state management
  isSidenavCollapsed = signal(false);
  activeMenuItem = signal('dashboard');
  isSearchFocused = signal(false);
  authService = inject(AuthService);
  // Search functionality
  searchControl = new FormControl('');
  searchSuggestions = signal<string[]>(['Dashboard', 'Sınavlar', 'Sorular', 'Öğrenciler', 'Raporlar']);
  globalSearchControl = new FormControl('');
  // User info
  userName = signal('Mustafa Sahin');
  userEmail = signal('mustafa@examapp.com');
  userAvatarUrl = signal('');
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
    { id: 'programsm', name: 'Programlarım', icon: 'assignment_ind', route: '/programs', type: 'menu' },
    { id: 'students', name: 'Öğrenciler', icon: 'people', route: '/students', type: 'menu' },
    { id: 'divider1', name: '', icon: '', route: '', type: 'divider' },
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
  }

  toggleSidenav() {
    this.isSidenavCollapsed.set(!this.isSidenavCollapsed());
    console.log('Sidebar collapsed:', this.isSidenavCollapsed());
  }

  navigateTo(route: string) {
    if (route) {
      this.router.navigate([route]);
      const menuItem = this.menuItems.find((item) => item.route === route);
      if (menuItem) {
        this.activeMenuItem.set(menuItem.id);
      }
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
    this.router.navigate(['/logout']);
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

  onGlobalSearch() {
    const query = this.globalSearchControl.value?.trim() || '';
    this.router.navigate(['/tests'], { queryParams: { search: query } });

    // Aramayı search history'e ekle (varsa yinelenmeyen)
    if (query && !this.searchHistory.includes(query)) {
      this.searchHistory.unshift(query);
    }
  }

  showSection(section: 'newest' | 'hot' | 'completed' | 'search' | 'relevant') {
    this.currentSection.set(section);
    if (section === 'search') {
      // this.performSearch();
    }
  }
}
