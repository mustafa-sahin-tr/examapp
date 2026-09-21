import { Component, DestroyRef, inject, signal, OnInit } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { combineLatest, take } from 'rxjs';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import { Router } from '@angular/router';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { StudentService } from '../../services/student.service';
import { GradesService } from '../../services/grades.service';
import { Grade } from '../../models/student';
import { REGISTER_SCOPE } from '../register/register-scope';

/** Öğrenci kayıt ucunun yanıtı (servis henüz `any` döndürüyor). */
interface StudentRegistrationResult {
  accessToken?: string;
  profileId?: number;
}

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
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(REGISTER_SCOPE)],
  templateUrl: './student-register.component.html',
})
export class StudentRegisterComponent implements OnInit {
  private fb = inject(FormBuilder);
  private studentService = inject(StudentService);
  private router = inject(Router);
  private snackBar = inject(MatSnackBar);
  private gradesService = inject(GradesService);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);

  isSubmitting = signal(false);

  studentForm = this.fb.group({
    studentNumber: ['', [Validators.required, Validators.maxLength(50)]],
    schoolName: ['', [Validators.required, Validators.maxLength(100)]],
    gradeId: [null as number | null], // Opsiyonel
  });

  readonly grades = signal<Grade[]>([]);

  ngOnInit(): void {
    this.loadGrades();
  }

  loadGrades() {
    this.gradesService.getGrades().subscribe({
      next: (grades: Grade[]) => this.grades.set(grades),
      error: () => this.grades.set([]),
    });
  }

  submitForm() {
    if (this.studentForm.invalid) return;

    this.isSubmitting.set(true);

    this.studentService.register(this.studentForm.value).subscribe({
      next: (val: StudentRegistrationResult) => {
        this.isSubmitting.set(false);
        if (val.accessToken) {
          localStorage.setItem('auth_token', val.accessToken);
        }
        localStorage.setItem('user_role', 'Student');
        if (val.profileId) {
          const user = localStorage.getItem('user');
          if (user) {
            try {
              const userObj = JSON.parse(user);
              userObj.role = 'Student';
              userObj.student = { id: val.profileId };
              localStorage.setItem('user', JSON.stringify(userObj));
            } catch (e) {
              console.error('User data parsing error:', e);
            }
          }
        }
        this.notify('student.success');
        this.router.navigate(['/tests']); // Kayıt sonrası yönlendirme
      },
      error: (err: unknown) => {
        this.isSubmitting.set(false);
        this.notify('student.error');
        console.error('Student Register Error:', err);
      },
    });
  }

  /**
   * Snackbar metni ve aksiyon etiketi şablon dışında üretildiği için ikisi de `selectTranslate`
   * ile okunur; bu çağrı scope sözlüğünü yükler ve dil değişiminde doğru metni verir.
   */
  private notify(messageKey: string): void {
    combineLatest([
      this.transloco.selectTranslate<string>(messageKey, {}, REGISTER_SCOPE),
      this.transloco.selectTranslate<string>('actions.ok', {}, REGISTER_SCOPE),
    ])
      .pipe(take(1), takeUntilDestroyed(this.destroyRef))
      .subscribe(([message, action]) => {
        this.snackBar.open(message, action, { duration: 3000 });
      });
  }
}
