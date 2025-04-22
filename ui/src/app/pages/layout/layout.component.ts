
import { Component, inject, OnInit, signal, WritableSignal } from '@angular/core';
import { NavigationEnd, Router, RouterModule, RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import {MatSidenavModule} from '@angular/material/sidenav';
import { MatMenuModule } from '@angular/material/menu';
import { AuthService } from '../../services/auth.service';
import { SidenavService } from '../../services/sidenav.service';
import { FormControl, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { MatFormField, MatFormFieldModule } from '@angular/material/form-field';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatInputModule } from '@angular/material/input';

@Component({
  selector: 'app-layout',
  imports: [CommonModule,
    MatSidenavModule,
    MatToolbarModule,
    MatButtonModule,
    MatIconModule,
    MatListModule,
    RouterModule,
    MatMenuModule,
    FormsModule,
    ReactiveFormsModule,MatAutocompleteModule, MatInputModule
  ],
  templateUrl: './layout.component.html',
  styleUrl: './layout.component.scss'
})
export class LayoutComponent implements OnInit {
  // isSidenavOpen = signal(true);
  showHeader = signal(true);
  globalSearchControl = new FormControl('');
  // Örnek geçmiş aramalar
  searchHistory: string[] = ['Angular', 'TypeScript', 'RxJS'];
  
  // Örnek öneri listesi (tüm öneriler)
  allSuggestions: string[] = [
    'Angular Material', 
    'Angular Animations', 
    'Angular Universal',
    'Angular CLI',
    'TypeScript Tutorial',
    'RxJS Operators'
  ];
  // Filtrelenmiş öneriler ya da geçmiş arama listesi için liste
  filteredSuggestions: string[] = [];
  
  // Input aktif mi? (focus durumunu takip etmek için)
  isInputFocused: boolean = false;

  menuItems = [
    { type:"menu", name: 'Sınavlar', icon: 'folder', route: '/tests' },
    { type:"menu", name: 'Program', icon: 'assignment_ind', route: '/program-create' },
    { type:"menu", name: 'Sertifikalar', icon: 'verified', route: '/certificates' },
    { type:"menu", name: 'Parkur', icon: 'timeline' },
    { type:"menu", name: 'Sonuçlar', icon: 'track_changes'},
    { type:"divider"},
    { type:"menu", name: 'Destek', icon: 'help' },
    { type:"menu", name: 'Geri Bildirim', icon: 'feedback' },
    { type:"divider"},
    { type:"menu", name: 'Test Ekleme', icon: 'add_circle' , route: '/exam'}
  ];  

  activeMenuItem: WritableSignal<string | null> = signal('/home');

  authService = inject(AuthService);
  profileImage = 'assets/profile.png';  
  userRole: string | null = null;
  $isAuthenticated = this.authService.isAuthenticated();
  sidenavService = inject(SidenavService);
  toggleSidenav() {
    this.sidenavService.toggleSidenav();
  }

  toggleFullScreen() {
    this.sidenavService.toggleFullScreen();
  }

  isLoginPage(): boolean {
    return this.router.url === '/login';
  }

  ngOnInit(): void {
    // Abone olarak değer değişimini takip et
    this.globalSearchControl.valueChanges.subscribe(value => {
      // Eğer input boşsa, geçmiş aramalar gösterilsin
      if (!value || !value.trim()) {
        this.filteredSuggestions = [...this.searchHistory];
      } else {
        // Girilen değere göre öneriler filtrelensin (küçük/büyük harf duyarsız)
        const filterValue = value.toLowerCase();
        this.filteredSuggestions = this.allSuggestions.filter(item =>
          item.toLowerCase().includes(filterValue)
        );
      }
    });
  }

  onGlobalSearch() {
    const query = this.globalSearchControl.value?.trim() || '';
    this.router.navigate(['/tests'], { queryParams: { search: query } });

    // Aramayı search history'e ekle (varsa yinelenmeyen)
    if (query && !this.searchHistory.includes(query)) {
      this.searchHistory.unshift(query);
    }
  }

  // Input focus olduğunda geçmiş veya öneri listesini göster
  onFocus() {
    this.isInputFocused = true;
    const value = this.globalSearchControl.value;
    if (!value || !value.trim()) {
      this.filteredSuggestions = [...this.searchHistory];
    }
  }

  onDelete(item: string) {
    this.searchHistory = this.searchHistory.filter(i => i !== item);
    this.filteredSuggestions = this.filteredSuggestions.filter(i => i !== item);
  }
  
  // Blur olduğunda belirli bir gecikme sonrası listeyi kapat (click eventi için)
  onBlur() {
    setTimeout(() => {
      this.isInputFocused = false;
    }, 150);
  }

  // Kullanıcı öneriden bir öğeye tıkladığında
  onSelectSuggestion(suggestion: string) {
    this.globalSearchControl.setValue(suggestion);
    this.onGlobalSearch();
  }

  
  isSidenavOpen = this.sidenavService.isSidenavOpen; // Servisten değer alıyoruz
  isFullScreen = this.sidenavService.isFullScreen; // Servisten değer alıyoruz

  constructor(private router: Router) {
    this.router.events.subscribe(event => {
      if (event instanceof NavigationEnd) {
        const url = (event as NavigationEnd).urlAfterRedirects; // ✅ Doğru URL almak için
        this.showHeader.set(!(url === '/login' || url === '/register'));
      }
    });
  }
  title = 'exam-app';
    get userAvatarUrl () {
      return this.authService.getUserAvatar() || this.profileImage;
    }

    get userName() {
      return this.authService.getUser()?.fullName || 'Kullanıcı';
    }
  
    navigateTo(path: string | undefined) {
      if (!path) {
        return;
      }
      if(path === '/tests') {
         this.globalSearchControl.setValue(''); // Arama çubuğunu temizle
      }
      this.activeMenuItem.set(path);
      this.router.navigate([path]);
    }
  
    logout() {
      this.authService.logout();
    }
}

