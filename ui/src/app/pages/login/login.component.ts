import { Component, inject, Inject, OnInit } from '@angular/core';
import {
  FormBuilder,
  FormGroup,
  Validators,
  ReactiveFormsModule,
  FormControl,
  MinLengthValidator,
} from '@angular/forms';
import { AuthService } from '../../services/auth.service';
import { Router } from '@angular/router';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { CommonModule } from '@angular/common';
import { jwtDecode } from 'jwt-decode'


@Component({
  selector: 'app-login',
  standalone: true,
  // templateUrl: './login.component.html',
  template: `<p>Giriş yapılıyor, lütfen bekleyin...</p>`,
  styleUrls: ['./login.component.scss'],
  imports: [
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatCardModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    CommonModule,
  ],
})
export class LoginComponent implements OnInit {
  authService = inject(AuthService);
  router = inject(Router);
  snackBar = inject(MatSnackBar);
  isLoading = false;
  loginForm = new FormGroup({
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(6)] }),
  });

  ngOnInit() {
    const token = localStorage.getItem('access_token');
    // 
    if (token && this.isTokenValid(token)) {
      this.router.navigate(['/tests']);
    } else {
      window.location.href = '/oidc-login'; // veya /oidc-login gibi temiz bir path
    }

    // const token = localStorage.getItem('auth_token');
    // const role = localStorage.getItem('user_role');
    // const user = localStorage.getItem('user');
    // if (token && role) {
    //   this.isLoading = true;
    //   this.checkUserSession(role);
    // }
  }

  isTokenValid(token: string): boolean {
    try {
      const decoded: any = jwtDecode(token);
      const now = Math.floor(Date.now() / 1000);
      return decoded.exp && decoded.exp > now;
    } catch (e) {
      return false;
    }
  }

  checkUserSession(role: string) {
    if (role === 'Student') {
      // 🟢 Öğrenci ise student kaydı olup olmadığını kontrol et
      this.authService.checkStudentProfile().subscribe({
        next: (studentRes) => {
          if (studentRes.hasStudentRecord) {
            localStorage.setItem('student', JSON.stringify(studentRes.student));
            this.router.navigate([`/student-profile`]); // ✅ Öğrenci kaydı varsa Ana Sayfa'ya git
          } else {
            this.router.navigate(['/student-register']); // ❌ Öğrenci kaydı yoksa kayıt sayfasına git
          }
        },
        error: () => {
          this.isLoading = false;
          this.snackBar.open('Öğrenci bilgileri kontrol edilirken hata oluştu.', 'Kapat', { duration: 3000 });
        },
      });
    } else if (role == 'Teacher') {
      // 🟢 Öğrenci ise student kaydı olup olmadığını kontrol et
      this.authService.checkTeacherProfile().subscribe({
        next: (teacherRes) => {
          if (teacherRes.hasTeacherRecord) {
            localStorage.setItem('teacher', JSON.stringify(teacherRes.teacher));
            this.router.navigate([`/tests`]); // ✅ Öğrenci kaydı varsa Ana Sayfa'ya git
          } else {
            this.router.navigate(['/teacher-register']); // ❌ Öğrenci kaydı yoksa kayıt sayfasına git
          }
        },
        error: () => {
          this.isLoading = false;
          this.snackBar.open('Öğretmen bilgileri kontrol edilirken hata oluştu.', 'Kapat', { duration: 3000 });
        },
      });
    } else {
      this.router.navigate(['/tests']); // ✅ Öğretmen veya Veli ise Home sayfasına git
    }
  }

  onSubmit() {
    if (this.loginForm.valid) {
      this.isLoading = true;
      this.authService.login(this.loginForm.value).subscribe({
        next: (res) => {
          this.snackBar.open('Giriş başarılı! Yönlendiriliyorsunuz...', 'Tamam', { duration: 3000 });
          const role = 'Student'; //res.role; // 0 = Student, 1 = Teacher, 2 = Parent
          this.checkUserSession(role);
        },
        error: () => {
          this.isLoading = false;
          this.snackBar.open('Giriş başarısız! Lütfen bilgilerinizi kontrol edin.', 'Kapat', { duration: 3000 });
        },
      });
    }
  }

  navigateToRegister() {
    this.router.navigate(['/register']);
  }
}
