
import { Component, inject, signal, WritableSignal } from '@angular/core';
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
    ReactiveFormsModule,
     MatFormField ,MatAutocompleteModule, MatInputModule
  ],
  templateUrl: './layout.component.html',
  styleUrl: './layout.component.scss'
})
export class LayoutComponent {
  // isSidenavOpen = signal(true);
  showHeader = signal(true);
  globalSearchControl = new FormControl('');
  menuItems = [
    { type:"menu", name: 'Ana Sayfa', icon: 'home', route: '/home' },
    { type:"menu", name: 'Sınavlar', icon: 'folder', route: '/tests' },
    { type:"menu", name: 'Role IQ', icon: 'assignment_ind', route: '/program-create' },
    { type:"menu", name: 'Sertifikalar', icon: 'verified' },
    { type:"menu", name: 'Parkur', icon: 'timeline' },
    { type:"menu", name: 'Sonuçlar', icon: 'track_changes'},
    { type:"divider"},
    { type:"menu", name: 'Destek', icon: 'help' },
    { type:"menu", name: 'Geri Bildirim', icon: 'feedback' },
    { type:"divider"},
    { type:"menu", name: 'Soru Ekleme', icon: 'add_circle' , route: '/questioncanvas'}
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

  onGlobalSearch() {
    const query = this.globalSearchControl.value?.trim() || '';
    this.router.navigate(['/tests'], { queryParams: { search: query } });
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

