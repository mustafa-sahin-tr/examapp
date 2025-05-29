import { Component, signal, OnInit, computed } from '@angular/core';
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
    ReactiveFormsModule
  ],
  templateUrl: './enhanced-layout.component.html',
  styleUrls: ['./enhanced-layout.component.scss']
})
export class EnhancedLayoutComponent implements OnInit {
  // Signals for state management
  isSidenavCollapsed = signal(false);
  activeMenuItem = signal('dashboard');
  isSearchFocused = signal(false);
  
  // Search functionality
  searchControl = new FormControl('');
  searchSuggestions = signal<string[]>(['Dashboard', 'Sınavlar', 'Sorular', 'Öğrenciler', 'Raporlar']);
  
  // User info
  userName = signal('Mustafa Sahin');
  userEmail = signal('mustafa@examapp.com');
  userAvatarUrl = signal('');

  // Menu items
  menuItems: MenuItem[] = [
    { id: 'dashboard', name: 'Dashboard', icon: 'dashboard', route: '/dashboard', type: 'menu' },
    { id: 'exams', name: 'Sınavlar', icon: 'quiz', route: '/exams', type: 'menu' },
    { id: 'questions', name: 'Sorular', icon: 'help_outline', route: '/questions', type: 'menu' },
    { id: 'students', name: 'Öğrenciler', icon: 'people', route: '/students', type: 'menu' },
    { id: 'divider1', name: '', icon: '', route: '', type: 'divider' },
    { id: 'reports', name: 'Raporlar', icon: 'analytics', route: '/reports', type: 'menu' },
    { id: 'settings', name: 'Ayarlar', icon: 'settings', route: '/settings', type: 'menu' },
    { id: 'divider2', name: '', icon: '', route: '', type: 'divider' },
    { id: 'help', name: 'Yardım', icon: 'support', route: '/help', type: 'menu' },
    { id: 'feedback', name: 'Geri Bildirim', icon: 'feedback', route: '/feedback', type: 'menu' }
  ];

  // Computed values
  filteredSuggestions = computed(() => {
    const searchValue = this.searchControl.value?.toLowerCase() || '';
    if (!searchValue) return [];
    return this.searchSuggestions().filter(suggestion => 
      suggestion.toLowerCase().includes(searchValue)
    );
  });

  constructor(private router: Router) {}

  ngOnInit() {
    // Set initial active menu item based on current route
    const currentRoute = this.router.url;
    const activeItem = this.menuItems.find(item => item.route === currentRoute);
    if (activeItem) {
      this.activeMenuItem.set(activeItem.id);
    }
  }

  toggleSidenav() {
    this.isSidenavCollapsed.set(!this.isSidenavCollapsed());
    console.log('Sidebar collapsed:', this.isSidenavCollapsed());
  }

  navigateTo(route: string) {
    if (route) {
      this.router.navigate([route]);
      const menuItem = this.menuItems.find(item => item.route === route);
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
    this.searchControl.setValue(suggestion);
    this.onSearch();
    this.isSearchFocused.set(false);
  }

  logout() {
    console.log('Logging out...');
    // Implement logout logic here
    this.router.navigate(['/login']);
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
}
