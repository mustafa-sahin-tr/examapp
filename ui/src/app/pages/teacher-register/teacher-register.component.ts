import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import { Router } from '@angular/router';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TeacherService } from '../../services/teacher.service';

@Component({
  selector: 'app-teacher-register',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatInputModule,
    MatFormFieldModule,
    MatButtonModule,
    MatSelectModule,
    MatSnackBarModule
  ],
  templateUrl: './teacher-register.component.html',
})
export class TeacherRegisterComponent {
  private fb = inject(FormBuilder);
  private teacherService = inject(TeacherService);
  private router = inject(Router);
  private snackBar = inject(MatSnackBar);

  isSubmitting = signal(false);

  teacherForm = this.fb.group({    
    schoolName: ['', [Validators.required, Validators.maxLength(100)]]
  });

  submitForm() {
    if (this.teacherForm.invalid) return;

    this.isSubmitting.set(true);

    this.teacherService.register(this.teacherForm.value).subscribe({
      next: (val) => {
        this.isSubmitting.set(false);
        if(val.refreshToken) {
          localStorage.setItem('auth_token', val.refreshToken);
        }
        this.snackBar.open('Öğretmen kaydı başarılı!', 'Tamam', { duration: 3000 });
        this.router.navigate(['/dashboard']); // Kayıt sonrası yönlendirme
      },
      error: (err) => {
        this.isSubmitting.set(false);
        this.snackBar.open('Öğretmen kaydı başarısız! Tekrar deneyin.', 'Tamam', { duration: 3000 });
        console.error('Teacher Register Error:', err);
      },
    });
  }
}
