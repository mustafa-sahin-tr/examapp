import { Component } from '@angular/core';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { AuthService } from '../../services/auth.service';
import { Router } from '@angular/router';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { CommonModule } from '@angular/common';
import { NavigationExtras } from '@angular/router';

@Component({
  selector: 'app-register',
  standalone: true,
  templateUrl: './register.component.html',
  styleUrls: ['./register.component.scss'],
  imports: [
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatCardModule,
    MatSnackBarModule,
    MatIconModule,
    MatSelectModule,
    CommonModule,
  ],
})
export class RegisterComponent {
  registerForm: FormGroup;
  isLoading = false;
  hidePassword = true;
  hideConfirmPassword = true;
  roles: { value: string; viewValue: string }[] = [];
  isLoadingRoles = true;

  constructor(
    private fb: FormBuilder,
    private authService: AuthService,
    private router: Router,
    private snackBar: MatSnackBar
  ) {
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

    this.loadRoles();
  }

  loadRoles() {
    this.isLoadingRoles = true;

    // Role control'ü disable et
    this.registerForm.get('role')?.disable();

    this.authService.getRoles().subscribe({
      next: (roles) => {
        this.roles = roles.map((role) => ({
          value: role.name,
          viewValue: role.displayName || role.name,
        }));
        this.isLoadingRoles = false;

        // Role control'ü enable et
        this.registerForm.get('role')?.enable();
      },
      error: (error) => {
        console.error('Roller yüklenemedi:', error);
        // Fallback olarak varsayılan rolleri kullan
        this.roles = [
          { value: 'student', viewValue: 'Öğrenci' },
          { value: 'teacher', viewValue: 'Öğretmen' },
          { value: 'parent', viewValue: 'Veli' },
        ];
        this.isLoadingRoles = false;

        // Role control'ü enable et
        this.registerForm.get('role')?.enable();

        this.snackBar.open('Roller yüklenirken hata oluştu, varsayılan roller kullanılıyor.', 'Tamam', {
          duration: 3000,
        });
      },
    });
  }

  passwordMatchValidator(group: FormGroup) {
    const password = group.get('password')?.value;
    const confirmPassword = group.get('confirmPassword')?.value;
    return password === confirmPassword ? null : { mismatch: true };
  }

  onSubmit() {
    if (this.registerForm.valid) {
      this.isLoading = true;

      const fullName = `${this.registerForm.value.firstName} ${this.registerForm.value.lastName}`.trim();
      const registerPayload = {
        firstName: this.registerForm.value.firstName,
        lastName: this.registerForm.value.lastName,
        email: this.registerForm.value.email,
        password: this.registerForm.value.password,
        role: this.registerForm.value.role,
      };

      this.authService.register(registerPayload).subscribe({
        next: (res) => {
          console.log('Başarılı Yanıt:', res);
          if (res.id) {
            this.snackBar.open('Kayıt başarılı! Giriş yapabilirsiniz.', 'Tamam', { duration: 3000 });
            const navigationExtras: NavigationExtras = {
              state: {
                email: registerPayload.email,
                password: registerPayload.password,
              },
            };
            setTimeout(() => {
              this.router.navigate(['/login'], navigationExtras);
            }, 1000);
          } else {
            console.error('Başarılı ama beklenmeyen yanıt kodu:', res.id);
          }
        },
        error: (err) => {
          console.error('Hata Yanıtı:', err);
          this.isLoading = false;
          this.snackBar.open('Kayıt başarısız! Lütfen bilgilerinizi kontrol edin.', 'Kapat', { duration: 3000 });
        },
      });
    }
  }

  navigateToLogin() {
    this.router.navigate(['/login']);
  }
}
