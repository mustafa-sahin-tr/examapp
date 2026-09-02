import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { AuthService } from '../../services/auth.service';
import { StudentService } from '../../services/student.service';
import { TeacherService } from '../../services/teacher.service';
import { ParentService } from '../../services/parent.service';
import { GradesService } from '../../services/grades.service';

type Role = 'student' | 'teacher' | 'parent';

@Component({
  selector: 'app-register-wizard',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    MatSnackBarModule,
  ],
  templateUrl: './register-wizard.component.html',
  styleUrl: './register-wizard.component.scss',
})
export class RegisterWizardComponent implements OnInit {
  private fb = inject(FormBuilder);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private snackBar = inject(MatSnackBar);
  private authService = inject(AuthService);
  private studentService = inject(StudentService);
  private teacherService = inject(TeacherService);
  private parentService = inject(ParentService);
  private gradesService = inject(GradesService);

  role = signal<Role | null>(null);
  step = computed(() => (this.role() ? 2 : 1));
  isSubmitting = signal(false);
  grades: any[] = [];

  studentForm = this.fb.group({
    studentNumber: ['', [Validators.required, Validators.maxLength(50)]],
    schoolName: ['', [Validators.required, Validators.maxLength(100)]],
    gradeId: [null as number | null],
  });

  teacherForm = this.fb.group({
    schoolName: ['', [Validators.required, Validators.maxLength(100)]],
  });

  ngOnInit(): void {
    // 1) explicit intent from the registration link (?role=student|teacher|parent)
    const q = (this.route.snapshot.queryParamMap.get('role') || '').toLowerCase();
    let resolved: Role | null = ['student', 'teacher', 'parent'].includes(q) ? (q as Role) : null;

    // 2) otherwise, an already-assigned realm role (admin-created / half-finished signup)
    if (!resolved) {
      const roles = this.authService.getRealmRoles();
      if (roles.includes('Student')) resolved = 'student';
      else if (roles.includes('Teacher')) resolved = 'teacher';
      else if (roles.includes('Parent')) resolved = 'parent';
    }

    this.role.set(resolved);
    this.loadGrades();
  }

  pickRole(role: Role) {
    this.role.set(role);
  }

  back() {
    this.role.set(null);
  }

  private loadGrades() {
    this.gradesService.getGrades().subscribe({
      next: (g) => (this.grades = g),
      error: () => (this.grades = []),
    });
  }

  submit() {
    const role = this.role();
    if (!role || this.isSubmitting()) return;

    let request$;
    if (role === 'student') {
      if (this.studentForm.invalid) return;
      request$ = this.studentService.register(this.studentForm.value);
    } else if (role === 'teacher') {
      if (this.teacherForm.invalid) return;
      request$ = this.teacherService.register(this.teacherForm.value);
    } else {
      request$ = this.parentService.register();
    }

    this.isSubmitting.set(true);
    request$.subscribe({
      next: (val: any) => {
        this.isSubmitting.set(false);
        const roleName = role.charAt(0).toUpperCase() + role.slice(1); // Student/Teacher/Parent
        if (val?.accessToken) localStorage.setItem('auth_token', val.accessToken);
        localStorage.setItem('user_role', roleName);
        const raw = localStorage.getItem('user');
        if (raw) {
          try {
            const u = JSON.parse(raw);
            u.role = roleName;
            if (val?.profileId) u[role] = { id: val.profileId };
            localStorage.setItem('user', JSON.stringify(u));
          } catch {
            /* ignore */
          }
        }
        this.snackBar.open('Kayıt tamamlandı!', 'Tamam', { duration: 3000 });
        this.router.navigate([role === 'parent' ? '/dashboard' : '/tests']);
      },
      error: (err: any) => {
        this.isSubmitting.set(false);
        this.snackBar.open('Kayıt başarısız! Lütfen tekrar deneyin.', 'Tamam', { duration: 3000 });
        console.error('Register wizard error:', err);
      },
    });
  }
}
