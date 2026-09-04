// src/app/auth/callback/callback.component.ts
import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatIconModule } from '@angular/material/icon';

@Component({
  standalone: true,
  selector: 'app-callback',
  imports: [CommonModule, MatIconModule],
  templateUrl: './callback.component.html',
  styleUrl: './callback.component.scss',
})
export class CallbackComponent implements OnInit {
  route = inject(ActivatedRoute);
  router = inject(Router);
  authService = inject(AuthService);
  snackBar = inject(MatSnackBar);

  // Loading state management
  currentStep = 1;
  currentMessage = 'Kimlik doğrulanıyor...';
  userRole: string | null = null;

  // Loading messages for different steps
  private messages = ['Kimlik doğrulanıyor...', 'Profil bilgileri kontrol ediliyor...', 'Yönlendiriliyor...'];

  private updateStep(step: number): void {
    this.currentStep = step;
    if (step <= this.messages.length) {
      this.currentMessage = this.messages[step - 1];
    }
  }

  private delay(ms: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }

  getRoleIcon(role: string): string {
    switch (role) {
      case 'Student':
        return 'school';
      case 'Teacher':
        return 'person';
      default:
        return 'account_circle';
    }
  }

  getRoleMessage(role: string): string {
    switch (role) {
      case 'Student':
        return 'Öğrenci paneline yönlendiriliyorsunuz...';
      case 'Teacher':
        return 'Öğretmen paneline yönlendiriliyorsunuz...';
      default:
        return 'Sisteme yönlendiriliyorsunuz...';
    }
  }

  async checkUserSession(role: string) {
    this.userRole = role;
    this.updateStep(2);
    await this.delay(100); // Add visual delay for better UX

    console.log('Unknown role');
    await this.delay(100);
    this.updateStep(3);
    await this.delay(100);
    this.router.navigate(['/tests']);
  }

  ngOnInit(): void {
    this.route.queryParams.subscribe(async (params) => {
      const code = params['code'];

      // state = "<returnBase>" | "<returnBase>~student|teacher|parent".
      // A "~role" suffix = the user came through a role-specific register link.
      const [returnBase = '', intent = ''] = String(params['state'] ?? '').split('~');
      const hasIntentRole = ['student', 'teacher', 'parent'].includes(intent);
      console.log('Callback params:', params, '-> intent:', intent);
      if (code) {
        // Add initial delay for better visual experience
        await this.delay(100);

        this.authService.exchangeCodeForToken(code).subscribe({
          next: async (res) => {
            this.snackBar.open('Giriş başarılı! Yönlendiriliyorsunuz...', 'Tamam', { duration: 3000 });
            await this.delay(100);
            await this.delay(100);
            this.updateStep(3);
            await this.delay(100);

            // No app role yet (Student/Teacher/Parent) -> profile completion screen.
            // With an intent, pre-select that role; otherwise the picker opens
            // with nothing selected. Existing users -> dashboard.
            const appRoles: string[] = res?.roles ?? [];
            const hasAppRole = ['Student', 'Teacher', 'Parent'].some((r) => appRoles.includes(r));
            const dest = hasAppRole
              ? '/dashboard'
              : hasIntentRole
              ? `/app/complete-profile?role=${intent}`
              : '/app/complete-profile';

            window.location.href = (returnBase + dest) || '/login';
          },
          error: () => {
            this.snackBar.open('Giriş başarısız! Lütfen bilgilerinizi kontrol edin.', 'Kapat', { duration: 3000 });
            setTimeout(() => {
              this.router.navigate(['/login']);
            }, 2000);
          },
        });
      } else {
        this.router.navigate(['/login']);
      }
    });
  }
}
