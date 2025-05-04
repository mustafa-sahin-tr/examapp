// src/app/auth/callback/callback.component.ts
import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { MatSnackBar } from '@angular/material/snack-bar';

@Component({
  standalone: true,
  selector: 'app-callback',
  imports: [CommonModule],
  template: `<p>Giriş yapılıyor, lütfen bekleyin...</p>`,
})
export class CallbackComponent implements OnInit {
  route = inject(ActivatedRoute);
  router = inject(Router);
  authService = inject(AuthService);
  snackBar = inject(MatSnackBar);

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
          //   this.isLoading = false;
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
          //   this.isLoading = false;
          this.snackBar.open('Öğretmen bilgileri kontrol edilirken hata oluştu.', 'Kapat', { duration: 3000 });
        },
      });
    } else {
      console.log('aaaaaaaaaaaa');
      //this.router.navigate(['/tests']); // ✅ Öğretmen veya Veli ise Home sayfasına git
    }
  }

  ngOnInit(): void {
    this.route.queryParams.subscribe((params) => {
      const code = params['code'];

      if (code) {
        this.authService.exchangeCodeForToken(code).subscribe({
          next: (res) => {
            this.snackBar.open('Giriş başarılı! Yönlendiriliyorsunuz...', 'Tamam', { duration: 3000 });
            const role = 'Student'; //res.role; // 0 = Student, 1 = Teacher, 2 = Parent
            this.checkUserSession(role);
          },
          error: () => {
            //   this.isLoading = false;
            this.snackBar.open('Giriş başarısız! Lütfen bilgilerinizi kontrol edin.', 'Kapat', { duration: 3000 });
          },
        });
      } else {
        this.router.navigate(['/login']);
      }
    });
  }
}
