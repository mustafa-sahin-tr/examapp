import { Component, inject } from '@angular/core';
import { Router, RouterModule } from '@angular/router';
import { AuthService } from '../../../services/auth.service';

@Component({
  selector: 'app-public-layout',
  imports: [RouterModule],
  standalone: true,
  templateUrl: './public-layout.component.html',
  styleUrl: './public-layout.component.scss'
})
export class PublicLayoutComponent {
  authService = inject(AuthService);
  router = inject(Router);
  profileImage = 'assets/profile.png';  
  userRole: string | null = null;
  $isAuthenticated = this.authService.isAuthenticated();

  isLoginPage(): boolean {
    return this.router.url === '/login';
  }

  get userAvatarUrl () {
    return this.authService.getUserAvatar() || this.profileImage;
  }
}
