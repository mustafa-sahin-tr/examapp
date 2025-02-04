import { Component } from '@angular/core';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { AuthService } from '../services/auth.service';
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
    CommonModule
  ]
})
export class RegisterComponent {
  registerForm: FormGroup;
  isLoading = false;
  hidePassword = true;
  hideConfirmPassword = true;
  roles = [
    { value: 0, viewValue: 'Öğrenci' },
    { value: 1, viewValue: 'Öğretmen' },
    { value: 2, viewValue: 'Veli' }
  ];

  constructor(
    private fb: FormBuilder,
    private authService: AuthService,
    private router: Router,
    private snackBar: MatSnackBar
  ) {
    this.registerForm = this.fb.group({
      firstName: ['', [Validators.required, Validators.minLength(2)]],
      lastName: ['', [Validators.required, Validators.minLength(2)]],
      email: ['', [Validators.required, Validators.email]],
      role: [null, [Validators.required]],
      password: ['', [Validators.required, Validators.minLength(6)]],
      confirmPassword: ['', [Validators.required]]
    }, { validator: this.passwordMatchValidator });
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
        fullName: fullName,
        email: this.registerForm.value.email,
        password: this.registerForm.value.password,
        role: this.registerForm.value.role
      };

      this.authService.register(registerPayload).subscribe({
        next: (res) => {
          console.log('Başarılı Yanıt:', res);
          if (res.id) {
            this.snackBar.open('Kayıt başarılı! Giriş yapabilirsiniz.', 'Tamam', { duration: 3000 });
            const navigationExtras: NavigationExtras = {
              state: {
                email: registerPayload.email,
                password: registerPayload.password
              }
            };
            setTimeout(() => {
              this.router.navigate(['/login'],navigationExtras);
            }, 1000);
          } else {
            console.error('Başarılı ama beklenmeyen yanıt kodu:', res.id);
          }
        },
        error: (err) => {
          console.error('Hata Yanıtı:', err);
          this.isLoading = false;
          this.snackBar.open('Kayıt başarısız! Lütfen bilgilerinizi kontrol edin.', 'Kapat', { duration: 3000 });
        }
      });
    }
  }

  navigateToLogin() {
    this.router.navigate(['/login']);
  }
}
