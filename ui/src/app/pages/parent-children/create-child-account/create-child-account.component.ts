import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, input, output, signal, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoPipe, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Observable, catchError, finalize, forkJoin, map, of, startWith } from 'rxjs';

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
import { School } from '../../../models/taxonomy';
import { GradesService } from '../../../services/grades.service';
import { ParentChildAccountService } from '../../../services/parent-child-account.service';
import { ParentLinkService } from '../../../services/parent-link.service';
import { SchoolService } from '../../../services/school.service';
import { PARENT_LINKS_SCOPE } from '../../../shared/utils/parent-link-ui';

/** Okul otomatik tamamlamasında en fazla bu kadar öneri (okul listesi binlerce satır olabilir). */
const MAX_SCHOOL_OPTIONS = 50;

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
    MatAutocompleteModule,
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
  providers: [provideTranslocoScope(PARENT_LINKS_SCOPE)],
  templateUrl: './create-child-account.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./create-child-account.component.scss'],
})
export class CreateChildAccountComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(ParentChildAccountService);
  private readonly linkService = inject(ParentLinkService);
  private readonly gradesService = inject(GradesService);
  private readonly schoolService = inject(SchoolService);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);

  /**
   * Velinin açık çocuk bağlantısı sayısı (üst sayfanın listesi); bilinmiyorsa null. Tavana (10) ulaşınca form gizlenir — sunucu
   * tavanı yine uygular; "açılmış hesap" tavanı istemcide hesaplanamadığından sunucu yanıtıyla da gizlenir.
   */
  readonly openChildren = input<number | null>(null);

  /** Hesap açıldı — üst sayfa çocuk listesini tazeler. */
  readonly created = output<CreatedChildAccount>();

  protected readonly consentVersion = PARENT_CONSENT_VERSION;
  protected readonly maxChildren = MAX_CHILD_ACCOUNTS;
  protected readonly grades = signal<Grade[]>([]);
  protected readonly schools = signal<School[]>([]);
  protected readonly submitting = signal(false);
  protected readonly serverError = signal<string | null>(null);
  protected readonly summary = signal<CreatedChildAccount | null>(null);
  protected readonly showPw = signal(false);
  /** Sınıf / okul listeleri yükleniyor ya da yüklenemedi (sınıf zorunlu: liste gelmeden form tamamlanamaz). */
  protected readonly refsLoading = signal(false);
  protected readonly gradesFailed = signal(false);
  protected readonly schoolsFailed = signal(false);
  protected readonly refsFailed = computed(() => this.gradesFailed() || this.schoolsFailed());
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
      schoolQuery: [''],
      studentNumber: ['', [Validators.maxLength(50)]],
      consent: [false, [Validators.requiredTrue]],
    },
    { validators: childPwGroupValidator }
  );

  /** Seçilen okul (otomatik tamamlamadan); serbest metin okul sayılmaz. */
  protected readonly selectedSchool = signal<School | null>(null);

  private readonly schoolQuery = toSignal(
    this.form.controls.schoolQuery.valueChanges.pipe(
      startWith(''),
      map((v) => (typeof v === 'string' ? v : ''))
    ),
    { initialValue: '' }
  );

  protected readonly schoolOptions = computed(() => {
    const q = this.schoolQuery().trim().toLocaleLowerCase();
    const all = this.schools();
    const matches = q ? all.filter((s) => s.name.toLocaleLowerCase().includes(q)) : all;
    return matches.slice(0, MAX_SCHOOL_OPTIONS);
  });

  ngOnInit(): void {
    this.loadReferenceData();
  }

  /** Sınıf ve okul listelerini yükler; "Tekrar dene" yalnız başarısız olanları yeniden ister. */
  protected loadReferenceData(): void {
    if (this.refsLoading()) return;
    const needGrades = this.grades().length === 0;
    const needSchools = this.schools().length === 0;
    if (!needGrades && !needSchools) return;

    this.refsLoading.set(true);
    if (needGrades) this.gradesFailed.set(false);
    if (needSchools) this.schoolsFailed.set(false);
    forkJoin({
      grades: needGrades
        ? this.gradesService.getGrades().pipe(
            map((g): Grade[] | null => g ?? []),
            catchErrorAs<Grade[]>()
          )
        : of(this.grades()),
      schools: needSchools
        ? this.schoolService.getSchools().pipe(
            map((list): School[] | null => list ?? []),
            catchErrorAs<School[]>()
          )
        : of(this.schools()),
    })
      .pipe(
        finalize(() => this.refsLoading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe(({ grades, schools }) => {
        if (grades === null) this.gradesFailed.set(true);
        else this.grades.set(grades);
        if (schools === null) this.schoolsFailed.set(true);
        else this.schools.set(schools);
      });
  }

  protected schoolLabel(school: School | string | null): string {
    return typeof school === 'string' ? school : (school?.name ?? '');
  }

  protected onSchoolSelected(event: MatAutocompleteSelectedEvent): void {
    const school = event.option.value as School;
    this.selectedSchool.set(school);
    this.form.controls.schoolQuery.setValue(school.name, { emitEvent: true });
  }

  protected onSchoolInput(): void {
    // Seçimden sonra metin değişirse seçim düşer (yanlış okul id'si gönderilmesin).
    const selected = this.selectedSchool();
    if (selected && this.form.controls.schoolQuery.value !== selected.name) {
      this.selectedSchool.set(null);
    }
  }

  protected clearSchool(): void {
    this.selectedSchool.set(null);
    this.form.controls.schoolQuery.setValue('');
  }

  protected toggleShowPw(): void {
    this.showPw.update((v) => !v);
  }

  protected hasGroupError(key: string): boolean {
    return this.form.hasError(key) && (this.form.controls.pw.touched || this.form.controls.pwRepeat.touched);
  }

  protected submit(): void {
    this.serverError.set(null);
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
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
      schoolId: this.selectedSchool()?.id ?? null,
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
          this.selectedSchool.set(null);
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
    if (code === CHILD_ACCOUNT_ERROR_CODES.usernameTaken || code === CHILD_ACCOUNT_ERROR_CODES.invalidUsername) {
      this.form.controls.username.setErrors({ server: true });
      this.form.controls.username.markAsTouched();
    } else if (code === CHILD_ACCOUNT_ERROR_CODES.passwordRejected) {
      this.form.controls.pw.setErrors({ server: true });
      this.form.controls.pw.markAsTouched();
    }
    this.serverError.set(message);
  }
}

/** Hata durumunda akışı `null` ile tamamlar (forkJoin diğer isteği beklesin, hangi listenin düştüğü ayırt edilsin). */
function catchErrorAs<T>(): (source: Observable<T | null>) => Observable<T | null> {
  return (source) => source.pipe(catchError(() => of(null)));
}
