import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import { Router } from '@angular/router';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { StudentService } from '../../services/student.service';

@Component({
  selector: 'app-student-register',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatInputModule,
    MatFormFieldModule,
    MatButtonModule,
    MatSelectModule,
    MatSnackBarModule,
  ],
  templateUrl: './student-register.component.html',
})
export class StudentRegisterComponent {
  private fb = inject(FormBuilder);
  private studentService = inject(StudentService);
  private router = inject(Router);
  private snackBar = inject(MatSnackBar);

  isSubmitting = signal(false);

  studentForm = this.fb.group({
    studentNumber: ['', [Validators.required, Validators.maxLength(50)]],
    schoolName: ['', [Validators.required, Validators.maxLength(100)]],
    gradeId: [null], // Opsiyonel
  });

  submitForm() {
    if (this.studentForm.invalid) return;

    this.isSubmitting.set(true);

    this.studentService.register(this.studentForm.value).subscribe({
      next: (val) => {
        this.isSubmitting.set(false);
        if (val.accessToken) {
          localStorage.setItem('auth_token', val.accessToken);
        }
        this.snackBar.open('Öğrenci kaydı başarılı!', 'Tamam', { duration: 3000 });
        this.router.navigate(['/tests']); // Kayıt sonrası yönlendirme
      },
      error: (err) => {
        this.isSubmitting.set(false);
        this.snackBar.open('Öğrenci kaydı başarısız! Tekrar deneyin.', 'Tamam', { duration: 3000 });
        console.error('Student Register Error:', err);
      },
    });
  }
}
