import { Component, DestroyRef, computed, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { combineLatest, take } from 'rxjs';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { AuthService } from '../../services/auth.service';
import { StudentService } from '../../services/student.service';
import { TeacherService } from '../../services/teacher.service';
import { ParentService } from '../../services/parent.service';
import { GradesService } from '../../services/grades.service';
import { Grade } from '../../models/student';
import { TeacherRegistrationError } from '../../models/teacher-registration.model';
import { REGISTER_SCOPE } from './register-scope';
import { TEACHER_APPROVAL_PENDING_URL } from '../../models/teacher-approval.model';

type Role = 'student' | 'teacher' | 'parent';

/** Rol tamamlama uçlarının ortak yanıtı (servisler henüz `any` döndürüyor). */
interface RegistrationResult {
  accessToken?: string;
  profileId?: number;
  /** Yalnız öğretmen ucu (issue #234): okul bağlantısı admin onayı bekliyor. */
  schoolApprovalPending?: boolean;
  /** Yalnız öğretmen ucu (issue #287): false → hesap admin onayı bekliyor (yeni kayıtta her zaman). */
  teacherAccountApproved?: boolean;
  /** Yalnız öğretmen ucu (issue #287): yerelleştirilmiş sunucu mesajı. */
  message?: string;
}

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
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(REGISTER_SCOPE)],
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
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);

  role = signal<Role | null>(null);
  step = computed(() => (this.role() ? 2 : 1));
  isSubmitting = signal(false);
  readonly grades = signal<Grade[]>([]);
  /** Issue #234: kayıt tamamlandı ama okul bağlantısı admin onayı bekliyor — yönlendirmeden önce bilgilendir. */
  readonly schoolApprovalPending = signal(false);
  /** Issue #234: 409 gibi kullanıcıya gösterilecek backend mesajı; form kullanılabilir kalır. */
  readonly submitError = signal<string | null>(null);

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
    this.submitError.set(null);
  }

  /** Okul onayı bilgilendirmesinden sonra öğretmen akışına devam. */
  continueAfterPending() {
    this.router.navigate(['/tests']);
  }

  private loadGrades() {
    this.gradesService.getGrades().subscribe({
      next: (g: Grade[]) => this.grades.set(g),
      error: () => this.grades.set([]),
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
    this.submitError.set(null);
    request$.subscribe({
      next: (val: RegistrationResult) => {
        this.isSubmitting.set(false);
        const roleName = role.charAt(0).toUpperCase() + role.slice(1); // Student/Teacher/Parent
        if (val?.accessToken) localStorage.setItem('auth_token', val.accessToken);
        localStorage.setItem('user_role', roleName);
        if (!this.authService.isCachedUserCurrent()) {
          // Onbellekteki kayit baska bir kullaniciya ait; merge etmek yerine at.
          this.authService.clearCachedUser();
        }
        const accountApprovalPending = role === 'teacher' && val?.teacherAccountApproved === false;
        const raw = localStorage.getItem('user');
        if (raw) {
          try {
            const u = JSON.parse(raw);
            u.role = roleName;
            if (val?.profileId) u[role] = { id: val.profileId };
            if (role === 'teacher' && u.teacher && typeof val?.teacherAccountApproved === 'boolean') {
              // Issue #287: menü/guard onay durumunu refresh beklemeden bilsin.
              u.teacher.teacherAccountApproved = val.teacherAccountApproved;
              if (accountApprovalPending) u.teacher.teacherApplicationStatus = 'Pending';
            }
            // `user` signal'ı da güncellensin (issue #191/#287: menü ve guard reaktif okur).
            this.authService.setUser(u);
          } catch {
            /* ignore */
          }
        }
        if (accountApprovalPending) {
          // Issue #287: yeni öğretmen hesabı admin onayı bekler — teacher dashboard yerine başvuru durumu sayfası.
          // Sunucu mesajı varsa o gösterilir; yoksa (eski sunucu) scope sözlüğündeki metin.
          const serverMessage = val?.message?.trim();
          if (serverMessage) {
            this.notifyText(serverMessage);
          } else {
            this.notify('wizard.teacherAccountPending');
          }
          this.router.navigate([TEACHER_APPROVAL_PENDING_URL]);
          return;
        }
        this.notify('wizard.success');
        if (role === 'teacher' && val?.schoolApprovalPending) {
          // Oturum kaydedildi; kullanıcı bilgilendirme kartını görüp "Devam et" ile ilerler.
          this.schoolApprovalPending.set(true);
          return;
        }
        this.router.navigate([role === 'parent' ? '/dashboard' : '/tests']);
      },
      error: (err: unknown) => {
        this.isSubmitting.set(false);
        // Issue #234: mevcut kaydın okulunu/bağımsızlığını değiştirme denemesi → 409, backend mesajı gösterilir.
        if (err instanceof HttpErrorResponse && err.status === 409) {
          const body = err.error as TeacherRegistrationError | null;
          this.submitError.set(body?.message || null);
          if (!body?.message) this.notify('wizard.error');
          return;
        }
        this.notify('wizard.error');
        console.error('Register wizard error:', err);
      },
    });
  }

  /**
   * Snackbar metni ve aksiyon etiketi şablon dışında üretildiği için ikisi de `selectTranslate`
   * ile okunur; bu çağrı scope sözlüğünü yükler ve dil değişiminde doğru metni verir.
   */
  /** Sunucudan gelen (zaten yerelleştirilmiş) metni snackbar'da gösterir; yalnız aksiyon etiketi çevrilir. */
  private notifyText(message: string): void {
    this.transloco
      .selectTranslate<string>('actions.ok', {}, REGISTER_SCOPE)
      .pipe(take(1), takeUntilDestroyed(this.destroyRef))
      .subscribe((action) => this.snackBar.open(message, action, { duration: 5000 }));
  }

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
