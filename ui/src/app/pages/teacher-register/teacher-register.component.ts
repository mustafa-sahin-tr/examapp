import { Component, DestroyRef, inject, signal } from '@angular/core';
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
import { TeacherService } from '../../services/teacher.service';
import { REGISTER_SCOPE } from '../register/register-scope';

/** Öğretmen kayıt ucunun yanıtı (servis henüz `any` döndürüyor). */
interface TeacherRegistrationResult {
  accessToken?: string;
  profileId?: number;
}

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
    MatSnackBarModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(REGISTER_SCOPE)],
  templateUrl: './teacher-register.component.html',
})
export class TeacherRegisterComponent {
  private fb = inject(FormBuilder);
  private teacherService = inject(TeacherService);
  private router = inject(Router);
  private snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);

  isSubmitting = signal(false);

  teacherForm = this.fb.group({
    schoolName: ['', [Validators.required, Validators.maxLength(100)]],
  });

  submitForm() {
    if (this.teacherForm.invalid) return;

    this.isSubmitting.set(true);

    this.teacherService.register(this.teacherForm.value).subscribe({
      next: (val: TeacherRegistrationResult) => {
        this.isSubmitting.set(false);
        if (val.accessToken) {
          localStorage.setItem('auth_token', val.accessToken);
        }
        localStorage.setItem('user_role', 'Teacher');
        if (val.profileId) {
          const user = localStorage.getItem('user');
          if (user) {
            try {
              const userObj = JSON.parse(user);
              userObj.role = 'Teacher';
              userObj.teacher = { id: val.profileId };
              localStorage.setItem('user', JSON.stringify(userObj));
            } catch (e) {
              console.error('User data parsing error:', e);
            }
          }
        }
        this.notify('teacher.success');
        this.router.navigate(['/tests']); // Kayıt sonrası yönlendirme
      },
      error: (err: unknown) => {
        this.isSubmitting.set(false);
        this.notify('teacher.error');
        console.error('Teacher Register Error:', err);
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
