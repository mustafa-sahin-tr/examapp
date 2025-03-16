import { Component, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterModule, RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { AppHeaderComponent } from './shared/components/app-header/app-header.component';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import {MatSidenavModule} from '@angular/material/sidenav';
import { AuthService } from './services/auth.service';
import { MatMenuModule } from '@angular/material/menu';
import { SidenavService } from './services/sidenav.service';

@Component({
  selector: 'app-root',
  imports: [CommonModule,
    MatSidenavModule,
    MatToolbarModule,
    MatButtonModule,
    MatIconModule,
    MatListModule,
    RouterModule,
    AppHeaderComponent,
    MatMenuModule
  ],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent {
  // isSidenavOpen = signal(true);
  showHeader = signal(true);

  authService = inject(AuthService);
    profileImage = 'assets/profile.png';  
    userRole: string | null = null;
    $isAuthenticated = this.authService.isAuthenticated();

  toggleSidenav() {
    this.sidenavService.toggleSidenav();
  }

  isLoginPage(): boolean {
    return this.router.url === '/login';
  }

  sidenavService = inject(SidenavService);
  isSidenavOpen = this.sidenavService.isSidenavOpen; // Servisten değer alıyoruz

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
  
    navigateTo(path: string) {
      this.router.navigate([path]);
    }
  
    logout() {
      this.authService.logout();
    }
}
