import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, Injector, OnInit, afterNextRender, computed, inject, input, output, signal, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { ErrorStateMatcher } from '@angular/material/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoPipe, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import {
  CHILD_ACCOUNT_ERROR_CODES,
  CHILD_USERNAME_PATTERN,
  CreatedChildAccount,
  CreateChildAccountRequest,
  MAX_CHILD_ACCOUNTS,
  PARENT_CONSENT_VERSION,
  isAcceptableChildPassword,
  normalizeChildUsername,
} from '../../../models/parent-child-account.model';
import { PARENT_LINK_ERROR_CODES } from '../../../models/parent-link.model';
import { Grade } from '../../../models/student';
import { GradesService } from '../../../services/grades.service';
import { SchoolSelectComponent } from '../../../shared/components/school-select/school-select.component';
import { ParentChildAccountService } from '../../../services/parent-child-account.service';
import { ParentLinkService } from '../../../services/parent-link.service';
import { PARENT_LINKS_SCOPE } from '../../../shared/utils/parent-link-ui';

/** Okul otomatik tamamlamasında en fazla bu kadar öneri (okul listesi binlerce satır olabilir). */

/** Kullanıcı adı (normalize edilmiş) biçim kontrolü. */
const usernameValidator: ValidatorFn = (control: AbstractControl): ValidationErrors | null =>
  !control.value || CHILD_USERNAME_PATTERN.test(normalizeChildUsername(control.value)) ? null : { usernameInvalid: true };

/** Şifre tabanı + kullanıcı adını içermeme + tekrar eşleşmesi (form seviyesinde). */
const childPwGroupValidator: ValidatorFn = (group: AbstractControl): ValidationErrors | null => {
  const pw: string = group.get('pw')?.value ?? '';
  const repeat: string = group.get('pwRepeat')?.value ?? '';
  const username = normalizeChildUsername(group.get('username')?.value);
  const errors: ValidationErrors = {};
  if (pw && !isAcceptableChildPassword(pw)) errors['pwInvalid'] = true;
  if (pw && username && pw.toLowerCase().includes(username)) errors['pwContainsUsername'] = true;
  if (repeat && pw !== repeat) errors['pwMismatch'] = true;
  return Object.keys(errors).length ? errors : null;
};

/** #884: formun adımları ve her adımın denetlediği alanlar. */
const CREATE_STEPS = ['child', 'login', 'consent'] as const;
type CreateStep = (typeof CREATE_STEPS)[number];
const STEP_CONTROLS: Record<CreateStep, readonly string[]> = {
  child: ['firstName', 'lastName', 'gradeId', 'studentNumber'],
  login: ['username', 'pw', 'pwRepeat'],
  consent: ['consent'],
};
/** Şifre grubundaki hatalar giriş adımına aittir. */
const LOGIN_GROUP_ERRORS = ['pwInvalid', 'pwContainsUsername', 'pwMismatch'];

const TR_ASCII: Record<string, string> = { ç: 'c', ğ: 'g', ı: 'i', i̇: 'i', ö: 'o', ş: 's', ü: 'u', â: 'a', î: 'i', û: 'u' };

/**
 * #884: adımlar formu gönderir (Enter da ilerlesin diye); varsayılan eşleyici "form gönderildi"de bir sonraki adımın boş
 * alanlarını hemen kırmızı gösterirdi. Hata yalnız dokunulmuş alanda görünür (adım geçişi o adımın alanlarına dokunur).
 */
export class TouchedErrorStateMatcher implements ErrorStateMatcher {
  isErrorState(control: AbstractControl | null): boolean {
    return !!control && control.invalid && control.touched;
  }
}

/**
 * #884: ad + soyaddan kullanıcı adı önerisi ("Elif Su", "Kaya" → "elifsu.kaya"). Türkçe harfler ASCII'ye çevrilir; kurala
 * (`CHILD_USERNAME_PATTERN`) uymayan sonuçta (ör. çok kısa) öneri yok.
 */
export function suggestChildUsername(firstName: string, lastName: string): string | null {
  const part = (value: string) =>
    value
      .toLocaleLowerCase('tr-TR')
      .replace(/[çğıöşüâîû]|i̇/g, (ch) => TR_ASCII[ch] ?? '')
      .normalize('NFD')
      .replace(/[^a-z0-9]/g, '');
  const first = part(firstName);
  const last = part(lastName);
  const username = [first, last].filter(Boolean).join('.').slice(0, 30).replace(/\.$/, '');
  return CHILD_USERNAME_PATTERN.test(username) ? username : null;
}

/**
 * Issue #438 (epic #435): velinin "Çocuklarım" sayfasındaki "Çocuk hesabı oluştur" formu. Çocuk e-postasız, kullanıcı adı + şifreyle
 * giriş yapar; hesap veli tarafından yönetilir. Başarıda kısa özet (çocuğun giriş yapacağı kullanıcı adı) gösterilir — şifre
 * ASLA tekrar gösterilmez ve form sıfırlanır (bellekte tutulmaz). Sunucu alan hatalarını `errorCode` ile döner.
 */
@Component({
  selector: 'app-create-child-account',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    SchoolSelectComponent,
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    RouterLink,
    TranslocoDirective,
    TranslocoPipe,
  ],
  providers: [provideTranslocoScope(PARENT_LINKS_SCOPE), { provide: ErrorStateMatcher, useClass: TouchedErrorStateMatcher }],
  templateUrl: './create-child-account.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./create-child-account.component.scss'],
})
export class CreateChildAccountComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(ParentChildAccountService);
  private readonly linkService = inject(ParentLinkService);
  private readonly gradesService = inject(GradesService);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);

  /**
   * Velinin açık çocuk bağlantısı sayısı (üst sayfanın listesi); bilinmiyorsa null. Tavana (10) ulaşınca form gizlenir — sunucu
   * tavanı yine uygular; "açılmış hesap" tavanı istemcide hesaplanamadığından sunucu yanıtıyla da gizlenir.
   */
  readonly openChildren = input<number | null>(null);

  /** Hesap açıldı — üst sayfa çocuk listesini tazeler. */
  readonly created = output<CreatedChildAccount>();
  /** #884: "Vazgeç" / "Kapat" — üst sayfa paneli kapatır. */
  readonly closed = output<void>();

  protected readonly steps = CREATE_STEPS;
  protected readonly step = signal(0);
  /** Kullanıcı adı ad-soyaddan önerildi ve veli henüz değiştirmedi. */
  protected readonly usernameSuggested = signal(false);

  protected readonly consentVersion = PARENT_CONSENT_VERSION;
  protected readonly maxChildren = MAX_CHILD_ACCOUNTS;
  protected readonly grades = signal<Grade[]>([]);
  protected readonly submitting = signal(false);
  protected readonly serverError = signal<string | null>(null);
  protected readonly summary = signal<CreatedChildAccount | null>(null);
  protected readonly showPw = signal(false);
  /** Sınıf listesi yükleniyor ya da yüklenemedi (sınıf zorunlu: liste gelmeden form tamamlanamaz). */
  protected readonly refsLoading = signal(false);
  protected readonly gradesFailed = signal(false);
  /** Sunucu tavan hatası döndü (ChildAccountLimitReached / ParentLimitReached) — sayfa yenilenene kadar form gizli kalır. */
  protected readonly serverLimitMessage = signal<string | null>(null);
  protected readonly limitReached = computed(() => {
    const open = this.openChildren();
    return this.serverLimitMessage() !== null || (open !== null && open >= MAX_CHILD_ACCOUNTS);
  });

  protected readonly form = this.fb.nonNullable.group(
    {
      firstName: ['', [Validators.required, Validators.maxLength(100)]],
      lastName: ['', [Validators.required, Validators.maxLength(100)]],
      username: ['', [Validators.required, usernameValidator]],
      pw: ['', [Validators.required]],
      pwRepeat: ['', [Validators.required]],
      gradeId: [0, [Validators.required, Validators.min(1)]],
      studentNumber: ['', [Validators.maxLength(50)]],
      consent: [false, [Validators.requiredTrue]],
    },
    { validators: childPwGroupValidator }
  );

  /** Listeden seçilen okul; serbest metin okul sayılmaz. */
  protected readonly schoolId = signal<number | null>(null);

  ngOnInit(): void {
    this.loadReferenceData();
  }

  /** Sınıf listesini yükler; "Tekrar dene" de bunu çağırır. */
  protected loadReferenceData(): void {
    if (this.refsLoading()) return;

    this.refsLoading.set(true);
    this.gradesFailed.set(false);
    this.gradesService
      .getGrades()
      .pipe(
        finalize(() => this.refsLoading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (grades) => this.grades.set(grades ?? []),
        error: () => this.gradesFailed.set(true),
      });
  }

  protected toggleShowPw(): void {
    this.showPw.update((v) => !v);
  }

  protected hasGroupError(key: string): boolean {
    return this.form.hasError(key) && (this.form.controls.pw.touched || this.form.controls.pwRepeat.touched);
  }

  /** Enter ya da adım düğmesi: son adımda gönderir, öncekilerde adım geçerliyse ilerler. */
  protected submitStep(): void {
    if (this.step() === CREATE_STEPS.length - 1) {
      this.submit();
      return;
    }
    if (!this.stepValid(CREATE_STEPS[this.step()])) return;
    if (CREATE_STEPS[this.step()] === 'child') this.suggestUsername();
    this.step.update((n) => n + 1);
  }

  protected back(): void {
    this.step.update((n) => Math.max(0, n - 1));
  }

  /** Adımın alanlarını dokunulmuş sayar; geçersiz alan (giriş adımında şifre grubu hatası) varsa false. */
  private stepValid(step: CreateStep): boolean {
    const controls = STEP_CONTROLS[step].map((name) => this.form.get(name)!);
    controls.forEach((c) => c.markAsTouched());
    const groupError = step === 'login' && LOGIN_GROUP_ERRORS.some((key) => this.form.hasError(key));
    return controls.every((c) => c.valid) && !groupError;
  }

  /** Kullanıcı adı boşsa (ya da hâlâ önceki öneriyse) ad-soyaddan önerilir. */
  private suggestUsername(): void {
    const control = this.form.controls.username;
    if (control.value && !this.usernameSuggested()) return;
    const suggestion = suggestChildUsername(this.form.controls.firstName.value, this.form.controls.lastName.value);
    if (!suggestion) return;
    control.setValue(suggestion);
    control.markAsUntouched();
    this.usernameSuggested.set(true);
    const sub = control.valueChanges.subscribe((value) => {
      if (value !== suggestion) {
        this.usernameSuggested.set(false);
        sub.unsubscribe();
      }
    });
  }

  /** Onay adımındaki özet satırı. */
  protected reviewName(): string {
    const v = this.form.getRawValue();
    return `${v.firstName.trim()} ${v.lastName.trim()}`.trim();
  }

  protected reviewGrade(): string {
    return this.grades().find((g) => g.id === this.form.controls.gradeId.value)?.name ?? '';
  }

  protected reviewUsername(): string {
    return normalizeChildUsername(this.form.controls.username.value);
  }

  protected submit(): void {
    this.serverError.set(null);
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      // Geçersiz alan önceki bir adımdaysa o adıma dönülür.
      const invalidStep = CREATE_STEPS.findIndex((step) => !this.stepValid(step));
      if (invalidStep >= 0) this.step.set(invalidStep);
      return;
    }
    const v = this.form.getRawValue();
    const pw = v.pw;
    const request: CreateChildAccountRequest = {
      firstName: v.firstName.trim(),
      lastName: v.lastName.trim(),
      username: normalizeChildUsername(v.username),
      password: pw,
      gradeId: v.gradeId,
      schoolId: this.schoolId(),
      studentNumber: v.studentNumber.trim() || null,
      consentAccepted: v.consent,
      consentVersion: PARENT_CONSENT_VERSION,
    };

    this.submitting.set(true);
    this.service
      .create(request)
      .pipe(
        finalize(() => this.submitting.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (child) => {
          // Şifre bellekte tutulmaz: form tamamen sıfırlanır, özet yalnız kullanıcı adını gösterir.
          this.form.reset();
          this.step.set(0);
          this.usernameSuggested.set(false);
          this.schoolId.set(null);
          this.showPw.set(false);
          this.summary.set(child);
          this.created.emit(child);
        },
        error: (err: HttpErrorResponse) => this.applyServerError(err),
      });
  }

  protected createAnother(): void {
    this.summary.set(null);
    this.serverError.set(null);
  }

  private applyServerError(err: HttpErrorResponse): void {
    const code = this.linkService.errorCode(err);
    const message = this.linkService.extractError(err, this.transloco.translate<string>('parent-links.createChild.createError'));
    if (code === CHILD_ACCOUNT_ERROR_CODES.childAccountLimitReached || code === PARENT_LINK_ERROR_CODES.parentLimitReached) {
      // Tavan dolu: form gizlenir, sunucunun yerelleştirilmiş tavan mesajı bilgi kutusunda gösterilir.
      this.serverLimitMessage.set(message);
      return;
    }
    // Alan hataları ilgili alanın altında da işaretlenir; mesaj sunucunun yerelleştirilmiş metni.
    const field =
      code === CHILD_ACCOUNT_ERROR_CODES.usernameTaken || code === CHILD_ACCOUNT_ERROR_CODES.invalidUsername
        ? this.form.controls.username
        : code === CHILD_ACCOUNT_ERROR_CODES.passwordRejected
          ? this.form.controls.pw
          : null;
    if (field && this.steps[this.step()] !== 'login') {
      // #884: alan giriş adımında. Adım çizilince alan kontrole yeniden bağlanır ve doğrulama elle konan hatayı siler: hata
      // çizimden sonra konur.
      this.step.set(CREATE_STEPS.indexOf('login'));
      afterNextRender(() => this.markServerError(field, message), { injector: this.injector });
      return;
    }
    if (field) this.markServerError(field, message);
    else this.serverError.set(message);
  }

  private markServerError(field: AbstractControl, message: string): void {
    field.setErrors({ server: true });
    field.markAsTouched();
    this.serverError.set(message);
  }
}
