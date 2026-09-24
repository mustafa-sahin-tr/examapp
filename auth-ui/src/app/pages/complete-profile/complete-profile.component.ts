import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { AuthService } from '../../services/auth.service';
import {
  Grade,
  RegisterErrorBody,
  RegisterProfileResponse,
  RegisterTeacherResponse,
  School,
} from '../../models/registration.model';

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
  /** Issue #234: öğretmen kaydı tamamlandı, okul bağlantısı yönetici onayı bekliyor — yönlendirmeden önce bilgilendir. */
  readonly schoolApprovalPending = signal(false);
  /** Issue #234: 409 (kayıt değişikliğine izin yok) backend mesajı; form kullanılabilir kalır. */
  readonly submitError = signal<string | null>(null);

  readonly grades = signal<Grade[]>([]);
  readonly gradesLoading = signal(false);
  readonly gradesError = signal<string | null>(null);

  readonly schools = signal<School[]>([]);
  readonly schoolsLoading = signal(false);
  readonly schoolsError = signal<string | null>(null);

  private readonly intentMap: Record<string, AppRole> = {
    student: 'Student',
    teacher: 'Teacher',
    parent: 'Parent',
  };

  readonly studentForm = this.fb.group({
    studentNumber: ['', [Validators.required, Validators.maxLength(50)]],
    schoolId: [null as number | null],
    gradeId: [null as number | null, [Validators.required]],
  });

  readonly teacherForm = this.fb.group({
    /** false = bir okula bağlı (varsayılan), true = bağımsız özel ders öğretmeni. */
    isIndependentTutor: [false],
    schoolId: [null as number | null],
  });

  /** Template'te okul alanını gizlemek için; form kontrolünün canlı değeri. */
  readonly isIndependentTutor = toSignal(this.teacherForm.controls.isIndependentTutor.valueChanges, {
    initialValue: this.teacherForm.controls.isIndependentTutor.value,
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
    this.loadSchools();
  }

  selectRole(role: AppRole): void {
    this.selectedRole.set(role);
  }

  back(): void {
    this.selectedRole.set(null);
    this.submitError.set(null);
  }

  /** Okul onayı bilgilendirmesinden sonra öğretmen akışına devam. */
  continueAfterPending(): void {
    window.location.href = '/tests';
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

  private loadSchools(): void {
    this.schoolsLoading.set(true);
    this.schoolsError.set(null);
    this.authService.getSchools().subscribe({
      next: (schools) => {
        this.schools.set(schools);
        this.schoolsLoading.set(false);
      },
      error: () => {
        this.schoolsError.set('Okul listesi yüklenemedi.');
        this.schoolsLoading.set(false);
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
    this.submitError.set(null);
    request$.subscribe({
      next: (res) => {
        this.isLoading.set(false);
        this.applySession(role, res);
        if (role === 'Teacher' && 'schoolApprovalPending' in res && res.schoolApprovalPending) {
          // Oturum kaydedildi; kullanıcı bilgilendirme kartını görüp "Devam Et" ile ilerler.
          this.schoolApprovalPending.set(true);
          return;
        }
        this.snackBar.open('Profiliniz tamamlandı! Yönlendiriliyorsunuz...', 'Tamam', { duration: 3000 });
        window.location.href = role === 'Parent' ? '/dashboard' : '/tests';
      },
      error: (err: HttpErrorResponse) => {
        this.isLoading.set(false);
        // Issue #234: öğretmen ucunda 409 = mevcut kaydın okulu/bağımsızlığı değiştirilemez; mesajı göster, yönlendirme.
        if (role === 'Teacher' && err?.status === 409) {
          const body = err.error as RegisterErrorBody | null;
          this.submitError.set(body?.message || 'Mevcut öğretmen kaydınızın okul bilgisi bu adımla değiştirilemez.');
          return;
        }
        // Issue #259: öğrenci ucunda 409 gövdeli gelirse (student.schoolLocked / student.registrationConflict)
        // sunucu mesajını göster, formda kal. Gövdesiz 409 = profil zaten tamamlanmış (eski davranış).
        if (role === 'Student' && err?.status === 409) {
          const body = err.error as RegisterErrorBody | null;
          if (body?.message) {
            this.submitError.set(body.message);
            return;
          }
        }
        if (err?.status === 409) {
          this.snackBar.open('Profiliniz zaten tamamlanmış.', 'Tamam', { duration: 3000 });
          this.redirectTo(role === 'Parent' ? '/dashboard' : '/tests');
          return;
        }
        // Issue #255: token geçerli ama hesap profili sunucuda çözülemedi → 404. Oturum geçersiz
        // değildir; login'e atmak yerine formu açık bırakıp nedenini göster.
        if (err?.status === 404) {
          this.submitError.set(
            'Hesap bilgileriniz şu anda doğrulanamadı. Lütfen birkaç dakika sonra tekrar deneyin; ' +
              'sorun sürerse çıkış yapıp yeniden giriş yapın.'
          );
          return;
        }
        // 401 yalnızca oturum gerçekten geçersizse gelir (ör. refresh cookie yok, token kimlik içermiyor).
        if (err?.status === 401) {
          this.snackBar.open('Oturumunuz sona ermiş, lütfen tekrar giriş yapın.', 'Kapat', { duration: 3000 });
          this.router.navigate(['/login']);
          return;
        }
        this.snackBar.open('Profil tamamlanamadı. Lütfen tekrar deneyin.', 'Kapat', { duration: 3000 });
      },
    });
  }

  /** Tam sayfa yönlendirme; spec'lerde gerçek navigasyonu engellemek için ayrı metot. */
  redirectTo(url: string): void {
    window.location.href = url;
  }

  private buildRequest(role: AppRole): Observable<RegisterProfileResponse | RegisterTeacherResponse> {
    if (role === 'Student') {
      const { studentNumber, schoolId, gradeId } = this.studentForm.getRawValue();
      return this.authService.registerStudentProfile({
        studentNumber: studentNumber ?? '',
        schoolId: schoolId ?? null,
        gradeId: gradeId as number,
      });
    }
    if (role === 'Teacher') {
      const { schoolId, isIndependentTutor } = this.teacherForm.getRawValue();
      const independent = isIndependentTutor === true;
      return this.authService.registerTeacherProfile({
        // Bağımsız seçildiyse önceden seçilmiş okul değeri gönderilmez.
        schoolId: independent ? null : schoolId ?? null,
        isIndependentTutor: independent,
      });
    }
    return this.authService.registerParentProfile();
  }

  private applySession(role: AppRole, res: RegisterProfileResponse): void {
    if (res?.accessToken) {
      localStorage.setItem('auth_token', res.accessToken);
    }
    localStorage.setItem('user_role', role);

    // Onbellekteki kayit baska bir kullaniciya aitse merge etme, at.
    if (!this.authService.isCachedUserCurrent()) {
      this.authService.clearCachedUser();
      return;
    }

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
