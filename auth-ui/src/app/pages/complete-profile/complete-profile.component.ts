import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { AuthService } from '../../services/auth.service';
import { Grade, RegisterProfileResponse } from '../../models/registration.model';

type AppRole = 'Student' | 'Teacher' | 'Parent';

interface RoleOption {
  value: AppRole;
  label: string;
  description: string;
}

@Component({
  selector: 'app-complete-profile',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, MatSnackBarModule],
  templateUrl: './complete-profile.component.html',
  styleUrl: './complete-profile.component.scss',
})
export class CompleteProfileComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly authService = inject(AuthService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly fb = inject(FormBuilder);

  readonly roleOptions: RoleOption[] = [
    { value: 'Student', label: 'Öğrenci', description: 'Sınavlara katıl, sonuçlarını takip et.' },
    { value: 'Teacher', label: 'Öğretmen', description: 'Sınav oluştur, öğrencilerini yönet.' },
    { value: 'Parent', label: 'Veli', description: "Çocuğunun gelişimini izle." },
  ];

  readonly selectedRole = signal<AppRole | null>(null);
  readonly step = computed(() => (this.selectedRole() ? 2 : 1));
  readonly isLoading = signal(false);

  readonly grades = signal<Grade[]>([]);
  readonly gradesLoading = signal(false);
  readonly gradesError = signal<string | null>(null);

  private readonly intentMap: Record<string, AppRole> = {
    student: 'Student',
    teacher: 'Teacher',
    parent: 'Parent',
  };

  readonly studentForm = this.fb.group({
    studentNumber: ['', [Validators.required, Validators.maxLength(50)]],
    schoolName: ['', [Validators.required, Validators.maxLength(100)]],
    gradeId: [null as number | null, [Validators.required]],
  });

  readonly teacherForm = this.fb.group({
    schoolName: ['', [Validators.required, Validators.maxLength(100)]],
  });

  ngOnInit(): void {
    // 1) explicit intent from the registration link (?role=student|teacher|parent)
    const intent = (this.route.snapshot.queryParamMap.get('role') ?? '').toLowerCase();
    let resolved: AppRole | null = intent in this.intentMap ? this.intentMap[intent] : null;

    // 2) otherwise, an already-assigned realm role (admin-created / half-finished signup)
    if (!resolved) {
      const roles = this.authService.getRealmRoles();
      if (roles.includes('Student')) resolved = 'Student';
      else if (roles.includes('Teacher')) resolved = 'Teacher';
      else if (roles.includes('Parent')) resolved = 'Parent';
    }

    this.selectedRole.set(resolved);
    this.loadGrades();
  }

  selectRole(role: AppRole): void {
    this.selectedRole.set(role);
  }

  back(): void {
    this.selectedRole.set(null);
  }

  private loadGrades(): void {
    this.gradesLoading.set(true);
    this.gradesError.set(null);
    this.authService.getGrades().subscribe({
      next: (grades) => {
        this.grades.set(grades);
        this.gradesLoading.set(false);
      },
      error: () => {
        this.gradesError.set('Sınıf listesi yüklenemedi.');
        this.gradesLoading.set(false);
      },
    });
  }

  onSubmit(): void {
    const role = this.selectedRole();
    if (!role || this.isLoading()) {
      return;
    }

    let request$: ReturnType<typeof this.buildRequest>;
    if (role === 'Student') {
      if (this.studentForm.invalid) {
        this.studentForm.markAllAsTouched();
        return;
      }
    } else if (role === 'Teacher') {
      if (this.teacherForm.invalid) {
        this.teacherForm.markAllAsTouched();
        return;
      }
    }
    request$ = this.buildRequest(role);

    this.isLoading.set(true);
    request$.subscribe({
      next: (res) => {
        this.isLoading.set(false);
        this.applySession(role, res);
        this.snackBar.open('Profiliniz tamamlandı! Yönlendiriliyorsunuz...', 'Tamam', { duration: 3000 });
        window.location.href = role === 'Parent' ? '/dashboard' : '/tests';
      },
      error: (err) => {
        this.isLoading.set(false);
        if (err?.status === 409) {
          this.snackBar.open('Profiliniz zaten tamamlanmış.', 'Tamam', { duration: 3000 });
          window.location.href = role === 'Parent' ? '/dashboard' : '/tests';
          return;
        }
        if (err?.status === 401) {
          this.snackBar.open('Oturumunuz sona ermiş, lütfen tekrar giriş yapın.', 'Kapat', { duration: 3000 });
          this.router.navigate(['/login']);
          return;
        }
        this.snackBar.open('Profil tamamlanamadı. Lütfen tekrar deneyin.', 'Kapat', { duration: 3000 });
      },
    });
  }

  private buildRequest(role: AppRole) {
    if (role === 'Student') {
      const { studentNumber, schoolName, gradeId } = this.studentForm.getRawValue();
      return this.authService.registerStudentProfile({
        studentNumber: studentNumber ?? '',
        schoolName: schoolName ?? '',
        gradeId: gradeId as number,
      });
    }
    if (role === 'Teacher') {
      const { schoolName } = this.teacherForm.getRawValue();
      return this.authService.registerTeacherProfile({ schoolName: schoolName ?? '' });
    }
    return this.authService.registerParentProfile();
  }

  private applySession(role: AppRole, res: RegisterProfileResponse): void {
    if (res?.accessToken) {
      localStorage.setItem('auth_token', res.accessToken);
    }
    localStorage.setItem('user_role', role);

    const raw = localStorage.getItem('user');
    if (raw) {
      try {
        const user = JSON.parse(raw);
        user.role = role;
        if (res?.profileId) {
          user[role.toLowerCase()] = { id: res.profileId };
        }
        localStorage.setItem('user', JSON.stringify(user));
      } catch {
        /* ignore malformed cached user */
      }
    }
  }
}
