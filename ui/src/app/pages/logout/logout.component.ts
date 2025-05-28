import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-logout',
  standalone: true,
  imports: [CommonModule, MatIconModule],
  templateUrl: './logout.component.html',
  styleUrl: './logout.component.scss',
})
export class LogoutComponent implements OnInit {
  private authService = inject(AuthService);
  private router = inject(Router);

  // Logout state management
  currentStep = 1;
  currentMessage = 'Oturum sonlandırılıyor...';
  userName: string = '';

  // Logout messages for different steps
  private messages = ['Oturum sonlandırılıyor...', 'Veriler güvenli bir şekilde temizleniyor...', 'Çıkış yapılıyor...'];

  private updateStep(step: number): void {
    this.currentStep = step;
    if (step <= this.messages.length) {
      this.currentMessage = this.messages[step - 1];
    }
  }

  private delay(ms: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }

  ngOnInit(): void {
    // Get user name before logout
    this.userName = this.authService.getUser()?.fullName || 'Kullanıcı';
    this.performLogout();
  }

  private async performLogout(): Promise<void> {
    try {
      // Step 1: Starting logout process
      this.updateStep(1);
      await this.delay(1200);

      // Step 2: Clearing data
      this.updateStep(2);
      await this.delay(1000);

      // Step 3: Final logout
      this.updateStep(3);
      await this.delay(800);

      // Perform actual logout
      this.authService.logout();

      // Navigate to login after a short delay
      await this.delay(500);
      this.router.navigate(['/login']);
    } catch (error) {
      console.error('Logout error:', error);
      // Even if there's an error, complete the logout
      this.authService.logout();
      this.router.navigate(['/login']);
    }
  }
}
