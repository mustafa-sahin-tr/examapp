import { Component, inject, Inject, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule, FormControl, MinLengthValidator } from '@angular/forms';
import { AuthService } from '../../services/auth.service';
import { Router } from '@angular/router';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-login',
  standalone: true,
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.scss'],
  imports: [
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatCardModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    CommonModule
  ]
})
export class LoginComponent implements OnInit {
  authService = inject(AuthService);
  router = inject(Router);
  snackBar = inject(MatSnackBar);
  isLoading = false;
  loginForm = new FormGroup({
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email], }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(6)] })
  })

  ngOnInit() {
    const token = localStorage.getItem('auth_token');
    const role = localStorage.getItem('user_role');

    if (token && role) {
      this.isLoading = true;
      this.checkUserSession(parseInt(role, 10));
    }
  }

  checkUserSession(role: number) {
    if (role === 0) {
      // 🟢 Öğrenci ise student kaydı olup olmadığını kontrol et
      this.authService.checkStudentProfile().subscribe({
        next: (studentRes) => {
          if (studentRes.hasStudentRecord) {
            localStorage.setItem('student', JSON.stringify(studentRes.student));
            this.router.navigate([`/student-profile`]);  // ✅ Öğrenci kaydı varsa Ana Sayfa'ya git
          } else {
            this.router.navigate(['/student-register']);  // ❌ Öğrenci kaydı yoksa kayıt sayfasına git
          }
        },
        error: () => {
          this.isLoading = false;
          this.snackBar.open('Öğrenci bilgileri kontrol edilirken hata oluştu.', 'Kapat', { duration: 3000 });
        }
      });
    } else {
      this.router.navigate(['/home']); // ✅ Öğretmen veya Veli ise Home sayfasına git
    }
  }

  onSubmit() {
    if (this.loginForm.valid) {
      this.isLoading = true;
      this.authService.login(this.loginForm.value).subscribe({
        next: (res) => {
          this.snackBar.open('Giriş başarılı! Yönlendiriliyorsunuz...', 'Tamam', { duration: 3000 });
          const role = +res.role; // 0 = Student, 1 = Teacher, 2 = Parent
          if (role === 0) {
            // Eğer Student ise, Student kaydı olup olmadığını kontrol et
            this.authService.checkStudentProfile().subscribe({
              next: (studentRes) => {
                if (studentRes.hasStudentRecord && studentRes.student) {
                  this.router.navigate(['/home']); // ✅ Öğrenci kaydı varsa Ana Sayfa'ya git
                } else {
                  this.router.navigate(['/student-register']); // ❌ Öğrenci kaydı yoksa kayıt sayfasına git
                }
              },
              error: () => {
                this.snackBar.open('Öğrenci kaydı kontrol edilirken hata oluştu.', 'Kapat', { duration: 3000 });
                this.router.navigate(['/student-register']); // ❌ Hata olursa öğrenci kayıt sayfasına git
              }
            });
          } else {
            this.router.navigate(['/home']); // ✅ Öğretmen/Veli ise Home sayfasına git
          }
        },
        error: () => {
          this.isLoading = false;
          this.snackBar.open('Giriş başarısız! Lütfen bilgilerinizi kontrol edin.', 'Kapat', { duration: 3000 });
        }
      });
    }
  }

  navigateToRegister() {
    this.router.navigate(['/register']);
  }
}
