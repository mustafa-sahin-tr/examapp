import { Component, OnInit, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';

import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { AuthService } from '../../services/auth.service';
import {
  Grade,
  RegisterCooldownErrorBody,
  RegisterErrorBody,
  RegisterProfileResponse,
  RegisterTeacherResponse,
  School,
} from '../../models/registration.model';
import {
  PARENT_INVITATION_ERROR_CODES,
  ParentInvitationErrorBody,
  PRIVACY_POLICY_URL,
  parentContactErrorMessage,
} from '../../models/parent-activation.model';
import {
  PARENT_NAME_MAX_LENGTH,
  PARENT_PHONE_INPUT_MAX_LENGTH,
  PARENT_EMAIL_MAX_LENGTH,
  parentEmailValidator,
  parentNameValidator,
  parentPhoneValidator,
} from '../../shared/utils/parent-contact.util';
import { PhoneMaskDirective } from '../../shared/directives/phone-mask.directive';

type AppRole = 'Student' | 'Teacher' | 'Parent';

/**
 * Issue #440: bu rollerden biri olan hesap veli kuralına tabi değildir (sunucu `ParentRequirement.AdultRoles`); öğrenci adımında
 * veli bilgisi istenmez.
 */
const ADULT_ROLES: readonly string[] = ['Teacher', 'Admin', 'SuperAdmin', 'Parent'];

/** Issue #440: öğrenci kaydının veli bilgisi hatalarından (formda kalınır, mesaj gösterilir) biri mi. */
const PARENT_CONTACT_ERROR_CODES: ReadonlySet<string> = new Set([
  PARENT_INVITATION_ERROR_CODES.parentInfoRequired,
  PARENT_INVITATION_ERROR_CODES.invalidParentName,
  PARENT_INVITATION_ERROR_CODES.invalidParentEmail,
  PARENT_INVITATION_ERROR_CODES.invalidParentPhone,
  PARENT_INVITATION_ERROR_CODES.parentEmailNotUsable,
  PARENT_INVITATION_ERROR_CODES.parentEmailChangeLocked,
  PARENT_INVITATION_ERROR_CODES.resendLimited,
  PARENT_INVITATION_ERROR_CODES.unavailable,
  PARENT_INVITATION_ERROR_CODES.busy,
]);

/** Issue #287: ana uygulamadaki (ui) öğretmen başvuru durumu sayfası — `ui/src/app/models/teacher-approval.model.ts`. */
export const TEACHER_APPROVAL_PENDING_URL = '/teacher-approval-pending';

interface RoleOption {
  value: AppRole;
  label: string;
  description: string;
}

@Component({
  selector: 'app-complete-profile',
  standalone: true,
  imports: [ReactiveFormsModule, MatSnackBarModule, PhoneMaskDirective],
  templateUrl: './complete-profile.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
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
  /** Issue #287: öğretmen hesabı yönetici onayı bekliyor — öğretmen özellikleri onaya kadar kapalı. */
  readonly accountApprovalPending = signal(false);
  /** Issue #287 (review): kayıt yanıtındaki sunucu mesajı; yoksa şablondaki varsayılan metin. */
  readonly accountApprovalMessage = signal<string | null>(null);
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

  /**
   * Issue #440: veli kuralına tabi öğrenci (token'da yetişkin rolü yok) kayıtta veli adı, e-postası ve telefonu verir. Sunucu,
   * Active velisi / açık daveti olan öğrencide alanları yok sayar; istemci bunu bilemediği için kurala tabi herkese sorar.
   */
  readonly parentInfoRequired = signal(true);
  readonly privacyPolicyUrl = PRIVACY_POLICY_URL;
  readonly parentNameMaxLength = PARENT_NAME_MAX_LENGTH;
  readonly parentEmailMaxLength = PARENT_EMAIL_MAX_LENGTH;
  readonly parentPhoneMaxLength = PARENT_PHONE_INPUT_MAX_LENGTH;

  readonly studentForm = this.fb.group({
    studentNumber: ['', [Validators.required, Validators.maxLength(50)]],
    schoolId: [null as number | null],
    gradeId: [null as number | null, [Validators.required]],
    parentFullName: ['', [Validators.required, parentNameValidator]],
    parentEmail: ['', [Validators.required, parentEmailValidator]],
    parentPhone: ['', [Validators.required, parentPhoneValidator]],
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
    this.applyParentRequirement();
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

  /**
   * Onay bilgilendirmesinden sonra devam: hesap onayı bekleniyorsa başvuru durumu sayfası (issue #287),
   * yalnız okul onayı bekleniyorsa öğretmen akışı.
   */
  continueAfterPending(): void {
    this.redirectTo(this.accountApprovalPending() ? TEACHER_APPROVAL_PENDING_URL : '/tests');
  }

  /** Yetişkin rolü olan hesapta veli alanları formdan çıkarılır (devre dışı kontrol geçerlilikte sayılmaz). */
  private applyParentRequirement(): void {
    const roles = this.authService.getRealmRoles();
    const required = !roles.some((role) => ADULT_ROLES.includes(role));
    this.parentInfoRequired.set(required);
    const controls = [
      this.studentForm.controls.parentFullName,
      this.studentForm.controls.parentEmail,
      this.studentForm.controls.parentPhone,
    ];
    for (const control of controls) {
      if (required) {
        control.enable({ emitEvent: false });
      } else {
        control.disable({ emitEvent: false });
      }
    }
  }

  /** Şablon: kontrol dokunulmuş ve belirtilen hata var mı. */
  hasError(controlName: 'parentFullName' | 'parentEmail' | 'parentPhone', error: string): boolean {
    const control = this.studentForm.controls[controlName];
    return control.touched && control.hasError(error);
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
        if (role === 'Teacher' && 'teacherAccountApproved' in res && res.teacherAccountApproved === false) {
          // Issue #287: yeni öğretmen hesabı yönetici onayı bekler; okul talebi varsa o da kartta belirtilir.
          this.accountApprovalPending.set(true);
          this.accountApprovalMessage.set(res.message?.trim() || null);
          this.schoolApprovalPending.set(res.schoolApprovalPending === true);
          return;
        }
        if (role === 'Teacher' && 'schoolApprovalPending' in res && res.schoolApprovalPending) {
          // Oturum kaydedildi; kullanıcı bilgilendirme kartını görüp "Devam Et" ile ilerler.
          this.schoolApprovalPending.set(true);
          return;
        }
        this.snackBar.open('Profiliniz tamamlandı! Yönlendiriliyorsunuz...', 'Tamam', { duration: 3000 });
        window.location.href = role === 'Parent' ? '/parent' : '/tests';
      },
      error: (err: HttpErrorResponse) => {
        this.isLoading.set(false);
        // Issue #277: reddedilen okul talebinden sonra 24 saatlik bekleme → 429; sunucu mesajı + kalan süre, formda kal.
        if (role === 'Teacher' && err?.status === 429) {
          this.submitError.set(cooldownMessage(err));
          return;
        }
        // Issue #440: veli bilgisi hatası (400 biçim, 409 kullanılamayan e-posta, 429 kilit, 502 hesap servisi) — formda kal.
        if (role === 'Student' && isParentContactError(err)) {
          this.submitError.set(
            parentContactErrorMessage(err.error as ParentInvitationErrorBody, 'Veli bilgileri kaydedilemedi. Lütfen tekrar dene.')
          );
          return;
        }
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
          this.redirectTo(role === 'Parent' ? '/parent' : '/tests');
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
      const { studentNumber, schoolId, gradeId, parentFullName, parentEmail, parentPhone } = this.studentForm.getRawValue();
      return this.authService.registerStudentProfile({
        studentNumber: studentNumber ?? '',
        schoolId: schoolId ?? null,
        gradeId: gradeId as number,
        // Issue #440: sunucu ayrıca normalize eder (küçük harf e-posta, E.164 telefon).
        ...(this.parentInfoRequired()
          ? {
              parentFullName: (parentFullName ?? '').trim(),
              parentEmail: (parentEmail ?? '').trim(),
              parentPhone: (parentPhone ?? '').trim(),
            }
          : {}),
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

/** Issue #440: hata gövdesindeki `errorCode` veli bilgisi / davet hatalarından biri mi. */
function isParentContactError(err: HttpErrorResponse): boolean {
  const body = (err?.error && typeof err.error === 'object' ? err.error : null) as ParentInvitationErrorBody | null;
  return typeof body?.errorCode === 'string' && PARENT_CONTACT_ERROR_CODES.has(body.errorCode);
}

/** 429 (okul talebi bekleme süresi) için kullanıcı metni: sunucu mesajı (yoksa varsayılan) + kalan süre. */
export function cooldownMessage(err: HttpErrorResponse, now: number = Date.now()): string {
  const body = (err.error && typeof err.error === 'object' ? err.error : null) as RegisterCooldownErrorBody | null;
  const message =
    body?.message?.trim() || 'Reddedilen okul talebinizden sonra bekleme süresi henüz dolmadı.';
  let seconds: number | null = null;
  if (typeof body?.retryAfterSeconds === 'number' && body.retryAfterSeconds > 0) {
    seconds = body.retryAfterSeconds;
  } else {
    const header = Number.parseInt(err.headers?.get('Retry-After') ?? '', 10);
    if (Number.isFinite(header) && header > 0) {
      seconds = header;
    } else if (body?.retryAfterUtc) {
      const until = Date.parse(body.retryAfterUtc);
      if (Number.isFinite(until) && until > now) seconds = Math.ceil((until - now) / 1000);
    }
  }
  if (seconds == null) return message;
  const totalMinutes = Math.max(1, Math.ceil(seconds / 60));
  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  const remaining = hours > 0 ? `${hours} saat ${minutes} dakika` : `${minutes} dakika`;
  return `${message} Yeni bir okul talebini yaklaşık ${remaining} sonra gönderebilirsin.`;
}
