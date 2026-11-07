import { Component, inject, ChangeDetectionStrategy, computed, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { AuthService } from '../../services/auth.service';
import { Router } from '@angular/router';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';

import { NavigationExtras } from '@angular/router';
import { RegisterRequest, RegisterResponse } from '../../models/registration.model';

@Component({
  selector: 'app-register',
  standalone: true,
  templateUrl: './register.component.html',
  styleUrls: ['./register.component.scss'],
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [ReactiveFormsModule, MatSnackBarModule],
})
export class RegisterComponent {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  registerForm: FormGroup;
  isLoading = false;
  hidePassword = true;
  hideConfirmPassword = true;
  // Issue #240: kayıt formu anonim; Keycloak realm rol kataloğu (GET /api/auth/roles) artık yalnızca Admin'e açık.
  // Anonim kayıtta seçilebilen roller auth-api'nin register allowlist'i ile birebir aynı sabit küme.
  readonly roles: readonly { value: string; viewValue: string; title: string; hint: string }[] = [
    { value: 'Student', viewValue: 'Öğrenci', title: 'Öğrenciyim', hint: 'Test çöz, rozet kazan' },
    { value: 'Teacher', viewValue: 'Öğretmen', title: 'Öğretmenim', hint: 'Test hazırla, ata' },
    { value: 'Parent', viewValue: 'Veli', title: 'Veliyim', hint: 'Çocuğumu izle' },
  ];

  /** Issue #850: kayıt önce rolü sorar (1 / 2), form o rolle açılır (2 / 2). */
  readonly step = signal<'role' | 'form'>('role');
  readonly selectedRole = signal<string | null>(null);
  readonly selectedRoleTitle = computed(
    () => this.roles.find((r) => r.value === this.selectedRole())?.viewValue ?? ''
  );

  constructor() {
    this.registerForm = this.fb.group(
      {
        firstName: ['', [Validators.required, Validators.minLength(2)]],
        lastName: ['', [Validators.required, Validators.minLength(2)]],
        email: ['', [Validators.required, Validators.email]],
        role: [null, [Validators.required]],
        password: ['', [Validators.required, Validators.minLength(6)]],
        confirmPassword: ['', [Validators.required]],
      },
      { validator: this.passwordMatchValidator }
    );
  }

  selectRole(role: string): void {
    this.selectedRole.set(role);
    this.registerForm.controls['role'].setValue(role);
  }

  continueToForm(): void {
    if (this.selectedRole()) this.step.set('form');
  }

  backToRole(): void {
    this.step.set('role');
  }

  passwordMatchValidator(group: FormGroup) {
    const password = group.get('password')?.value;
    const confirmPassword = group.get('confirmPassword')?.value;
    return password === confirmPassword ? null : { mismatch: true };
  }

  onSubmit() {
    if (this.registerForm.valid) {
      this.isLoading = true;

      const registerPayload: RegisterRequest = {
        firstName: this.registerForm.value.firstName,
        lastName: this.registerForm.value.lastName,
        email: this.registerForm.value.email,
        password: this.registerForm.value.password,
        role: this.registerForm.value.role,
      };

      this.authService.register(registerPayload).subscribe({
        // Issue #240: yanıt e-postanın zaten kayıtlı olup olmadığını belirtmez (her durumda aynı 200 + mesaj);
        // bu yüzden başarı gövdesinde id beklenmez, kullanıcı her durumda login'e yönlendirilir.
        // Mesaj auth-api'de Accept-Language'a göre yerelleştirilir; auth-ui'da i18n yok, yedek metin sabit.
        next: (res: RegisterResponse) => {
          this.snackBar.open(
            res?.message || 'Kayıt talebiniz alındı. Hesabınız oluşturulduysa giriş yapabilirsiniz.',
            'Tamam',
            { duration: 5000 }
          );
          const navigationExtras: NavigationExtras = {
            // Parola navigation state'e (tarayıcı history'si) konmaz; login yalnızca e-postayı alabilir.
            state: { email: registerPayload.email },
          };
          setTimeout(() => {
            this.router.navigate(['/login'], navigationExtras);
          }, 1000);
        },
        error: (err: HttpErrorResponse) => {
          this.isLoading = false;
          // 400 (rol / e-posta biçimi / seed alanı) ve 500 gövdesi auth-api'de yerelleştirilmiş `message` taşır;
          // hiçbiri e-postanın kayıtlı olup olmadığını belirtmez.
          const serverMessage = typeof err?.error?.message === 'string' ? err.error.message : null;
          this.snackBar.open(serverMessage || 'Kayıt başarısız! Lütfen bilgilerinizi kontrol edin.', 'Kapat', {
            duration: 4000,
          });
        },
      });
    }
  }

  navigateToLogin() {
    this.router.navigate(['/login']);
  }
}
