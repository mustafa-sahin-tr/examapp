import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { AuthService } from '../../services/auth.service';

type AppRole = 'Student' | 'Teacher' | 'Parent';

interface RoleOption {
  value: AppRole;
  label: string;
  description: string;
}

@Component({
  selector: 'app-complete-profile',
  standalone: true,
  imports: [CommonModule, MatSnackBarModule],
  templateUrl: './complete-profile.component.html',
  styleUrl: './complete-profile.component.scss',
})
export class CompleteProfileComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly authService = inject(AuthService);
  private readonly snackBar = inject(MatSnackBar);

  readonly roleOptions: RoleOption[] = [
    { value: 'Student', label: 'Öğrenci', description: 'Sınavlara katıl, sonuçlarını takip et.' },
    { value: 'Teacher', label: 'Öğretmen', description: 'Sınav oluştur, öğrencilerini yönet.' },
    { value: 'Parent', label: 'Veli', description: "Çocuğunun gelişimini izle." },
  ];

  readonly selectedRole = signal<AppRole | null>(null);
  readonly isLoading = signal(false);

  private readonly intentMap: Record<string, AppRole> = {
    student: 'Student',
    teacher: 'Teacher',
    parent: 'Parent',
  };

  ngOnInit(): void {
    const intent = (this.route.snapshot.queryParamMap.get('role') ?? '').toLowerCase();
    if (intent in this.intentMap) {
      this.selectedRole.set(this.intentMap[intent]);
    }
  }

  selectRole(role: AppRole): void {
    this.selectedRole.set(role);
  }

  onSubmit(): void {
    const role = this.selectedRole();
    if (!role || this.isLoading()) {
      return;
    }

    this.isLoading.set(true);

    this.authService.completeProfile(role).subscribe({
      next: () => {
        this.authService.refreshToken().subscribe({
          next: (newToken) => {
            if (newToken) {
              this.authService.applyRefreshedSession(newToken);
            }
            this.snackBar.open('Profiliniz tamamlandı! Yönlendiriliyorsunuz...', 'Tamam', { duration: 3000 });
            window.location.href = '/dashboard';
          },
          error: () => {
            this.isLoading.set(false);
            this.snackBar.open(
              'Profil kaydedildi ancak oturum yenilenemedi. Lütfen tekrar giriş yapın.',
              'Kapat',
              { duration: 4000 }
            );
            this.router.navigate(['/login']);
          },
        });
      },
      error: (err) => {
        this.isLoading.set(false);
        if (err?.status === 409) {
          this.snackBar.open('Profiliniz zaten tamamlanmış.', 'Tamam', { duration: 3000 });
          window.location.href = '/dashboard';
          return;
        }
        if (err?.status === 404) {
          this.snackBar.open('Hesap bilgileriniz bulunamadı, lütfen tekrar giriş yapın.', 'Kapat', {
            duration: 4000,
          });
          this.router.navigate(['/login']);
          return;
        }
        this.snackBar.open('Profil tamamlanamadı. Lütfen tekrar deneyin.', 'Kapat', { duration: 3000 });
      },
    });
  }
}
