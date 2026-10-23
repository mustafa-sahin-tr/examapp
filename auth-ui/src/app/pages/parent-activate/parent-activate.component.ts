import { Component, OnDestroy, OnInit, computed, inject, signal, DOCUMENT, ChangeDetectionStrategy } from '@angular/core';
import { Location } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Meta } from '@angular/platform-browser';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';

import {
  PARENT_HOME_URL,
  PARENT_INVITATION_ERROR_CODES,
  PARENT_PASSWORD_MAX_LENGTH,
  PARENT_PASSWORD_MIN_LENGTH,
  ParentActivationLookup,
  ParentActivationResult,
  ParentInvitationErrorBody,
  PRIVACY_POLICY_URL,
  formatServerDateTime,
  parentConsentText,
} from '../../models/parent-activation.model';
import { ParentActivationService } from '../../services/parent-activation.service';
import { ParentActivationPending, ParentActivationPendingService } from '../../services/parent-activation-pending.service';
import { AuthService } from '../../services/auth.service';
import { BRAND_NAME } from '../../shared/brand';

/** Sayfanın auth-ui içindeki yolu (taban `/app/` → `/app/parent-activate#token=...`). */
export const PARENT_ACTIVATE_PATH = '/parent-activate';

export type ParentActivateView = 'loading' | 'invalid' | 'error' | 'used' | 'expired' | 'form' | 'done' | 'accepted';

/** Mevcut hesaplı veli giriş turundan bu sayfaya döner (`OidcFlowService` `returnPath` allowlist'i: aynı origin göreli yol). */
export const PARENT_ACTIVATE_LOGIN_URL = `/app/login?returnUrl=${encodeURIComponent('/app' + PARENT_ACTIVATE_PATH)}`;

/**
 * Bağlantı token'ı URL parçasında (`#token=...`) gelir — parça sunucuya/erişim loglarına gitmez. Sorgu (`?token=`) bilerek
 * okunmaz. Önce router'ın parçası, yoksa tarayıcının `location.hash`'i.
 */
export function activationTokenFromFragment(fragment: string | null | undefined): string | null {
  const raw = (fragment ?? '').replace(/^#/, '');
  if (!raw) {
    return null;
  }
  const token = new URLSearchParams(raw).get('token')?.trim();
  return token || null;
}

/** Sunucu kuralıyla aynı (`ChildAccountRules.IsValidPassword`): harf + rakam, baş/son boşluk yok. */
export const parentPasswordValidator: ValidatorFn = (control: AbstractControl<string | null>): ValidationErrors | null => {
  const value = control.value ?? '';
  if (!value) {
    return null;
  }
  if (/^\s|\s$/.test(value)) {
    return { passwordWhitespace: true };
  }
  return /\p{L}/u.test(value) && /\p{Nd}/u.test(value) ? null : { passwordComposition: true };
};

/** Şifre ile tekrarı aynı mı (şifre alanı devre dışıysa — mevcut veli — kontrol edilmez). */
const matchingPasswordsValidator: ValidatorFn = (group: AbstractControl): ValidationErrors | null => {
  const first = group.get('password');
  const second = group.get('confirmPassword');
  if (!first || !second || first.disabled || !second.value) {
    return null;
  }
  return first.value === second.value ? null : { passwordMismatch: true };
};

/**
 * Issue #440: velinin e-postasındaki aktivasyon bağlantısı (`/app/parent-activate?token=...`). Token sorgudan okunup hemen
 * adres çubuğundan silinir (geçmişte/ekran paylaşımında kalmasın) ve yalnız bellekte tutulur; sayfa `no-referrer` ile açılır.
 * Durumlar: geçersiz, kullanılmış, süresi dolmuş (+ yeniden gönder), yeni veli (şifre + rıza → anonim `activate`) ve
 * hesabı olan veli (`accountExists` ya da `activate` 409 `LoginRequired`): anonim aktivasyon mevcut hesaba BAĞLAMAZ — rıza
 * alınır, token + rıza yalnız sessionStorage'a (`ParentActivationPendingService`, 30 dk) yazılır, giriş (`/app/login`,
 * PKCE, `returnUrl` bu sayfa) yapılır ve dönüşte `accept` (Bearer) çağrılır. Oturum zaten açıksa doğrudan kabul edilir.
 */
@Component({
  selector: 'app-parent-activate',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './parent-activate.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['../complete-profile/complete-profile.component.scss', './parent-activate.component.scss'],
})
export class ParentActivateComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly document = inject(DOCUMENT);
  private readonly location = inject(Location);
  private readonly meta = inject(Meta);
  private readonly service = inject(ParentActivationService);
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly pending = inject(ParentActivationPendingService);

  protected readonly brandName = BRAND_NAME;
  protected readonly privacyPolicyUrl = PRIVACY_POLICY_URL;
  protected readonly minLength = PARENT_PASSWORD_MIN_LENGTH;
  protected readonly maxLength = PARENT_PASSWORD_MAX_LENGTH;
  protected readonly parentHomeUrl = PARENT_HOME_URL;

  /** Yalnız bellekte; adres çubuğundan silinir. */
  private token: string | null = null;
  /** Giriş turundan dönüşte bekleyen kabul (rıza girişten önce alındı). */
  private resumed: ParentActivationPending | null = null;

  readonly view = signal<ParentActivateView>('loading');
  readonly lookup = signal<ParentActivationLookup | null>(null);
  readonly result = signal<ParentActivationResult | null>(null);
  readonly submitting = signal(false);
  readonly submitError = signal<string | null>(null);
  readonly resending = signal(false);
  readonly resendMessage = signal<string | null>(null);
  readonly resendError = signal<string | null>(null);
  /** Saklı, süresi dolmamış oturum token'ı var mı (varsa giriş adımı atlanır). */
  readonly loggedIn = signal(false);

  /** Yeni hesap: şifre belirlenir. Mevcut veli: yalnız rıza + onay. */
  readonly isNewAccount = computed(() => this.lookup()?.requiresPasswordSetup === true);
  /** Bu e-postayla hesap var → giriş yapıp kabul. */
  readonly accountExists = computed(() => {
    const lookup = this.lookup();
    return !!lookup && (lookup.accountExists === true || lookup.requiresPasswordSetup === false);
  });
  readonly consentText = computed(() => parentConsentText(this.lookup()?.consentTextKey));
  readonly expiresAtText = computed(() => formatServerDateTime(this.lookup()?.expiresAt));

  readonly form = this.fb.nonNullable.group(
    {
      password: [
        '',
        [
          Validators.required,
          Validators.minLength(PARENT_PASSWORD_MIN_LENGTH),
          Validators.maxLength(PARENT_PASSWORD_MAX_LENGTH),
          parentPasswordValidator,
        ],
      ],
      confirmPassword: ['', [Validators.required]],
      consent: [false, [Validators.requiredTrue]],
    },
    { validators: matchingPasswordsValidator }
  );

  constructor() {
    // Bağlantıdaki token dış kaynaklara (font, görsel) Referer ile sızmasın.
    this.meta.updateTag({ name: 'referrer', content: 'no-referrer' });
    this.token =
      activationTokenFromFragment(this.route.snapshot.fragment) ??
      activationTokenFromFragment(this.document.location?.hash);
    const pending = this.pending.peek();
    if (!this.token && pending) {
      // Giriş turundan dönüş: token yalnız sessionStorage'daydı.
      this.token = pending.token;
      this.resumed = pending;
    } else if (pending && pending.token !== this.token) {
      // Yeni bir bağlantıyla gelindi: eski bekleyen kabul geçersiz.
      this.pending.clear();
    } else if (pending) {
      this.resumed = pending;
    }
    this.loggedIn.set(this.auth.hasValidSessionToken());
    // Token'ı (parçayla birlikte) adres çubuğundan ve geçmişten hemen sil (yalnız bellekte kalır).
    this.location.replaceState(PARENT_ACTIVATE_PATH);
  }

  ngOnInit(): void {
    if (!this.token) {
      this.view.set('invalid');
      return;
    }
    this.loadLookup();
  }

  ngOnDestroy(): void {
    this.meta.removeTag("name='referrer'");
  }

  loadLookup(): void {
    if (!this.token) {
      this.view.set('invalid');
      return;
    }
    this.view.set('loading');
    this.service.lookup(this.token).subscribe({
      next: (lookup) => {
        this.lookup.set(lookup);
        this.applyAccountKind(lookup.requiresPasswordSetup === true);
        if (lookup.used) {
          this.pending.clear();
          this.view.set('used');
        } else if (lookup.expired) {
          this.pending.clear();
          this.view.set('expired');
        } else {
          this.view.set('form');
          this.resumeAfterLogin(lookup);
        }
      },
      error: (err: HttpErrorResponse) => {
        if (err?.status === 404) {
          this.pending.clear();
        }
        this.view.set(err?.status === 404 ? 'invalid' : 'error');
      },
    });
  }

  hasError(control: 'password' | 'confirmPassword' | 'consent', error: string): boolean {
    const c = this.form.controls[control];
    return c.touched && c.hasError(error);
  }

  get mismatch(): boolean {
    return this.form.controls.confirmPassword.touched && this.form.hasError('passwordMismatch');
  }

  submit(): void {
    const lookup = this.lookup();
    if (!this.token || !lookup || this.submitting()) {
      return;
    }
    if (!lookup.requiresPasswordSetup) {
      this.loginAndAccept();
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const chosen = this.form.controls.password.value;
    this.submitting.set(true);
    this.submitError.set(null);
    this.service
      .activate({
        token: this.token,
        ...(lookup.requiresPasswordSetup ? { password: chosen } : {}),
        consentAccepted: true,
        consentVersion: lookup.consentVersion,
      })
      .pipe(finalize(() => this.submitting.set(false)))
      .subscribe({
        next: (result) => {
          // Şifre formda da kalmasın.
          this.form.reset();
          this.result.set(result);
          this.view.set('done');
        },
        error: (err: HttpErrorResponse) => this.onActivateError(err),
      });
  }

  resend(): void {
    if (!this.token || this.resending()) {
      return;
    }
    this.resending.set(true);
    this.resendError.set(null);
    this.resendMessage.set(null);
    this.service
      .resend(this.token)
      .pipe(finalize(() => this.resending.set(false)))
      .subscribe({
        next: () => this.resendMessage.set('Yeni bir aktivasyon bağlantısı e-posta adresine gönderildi.'),
        error: (err: HttpErrorResponse) => {
          const body = errorBody(err);
          if (err?.status === 404) {
            this.view.set('invalid');
          } else if (body?.errorCode === PARENT_INVITATION_ERROR_CODES.activationTokenUsed) {
            this.view.set('used');
          } else if (err?.status === 429) {
            const retry = formatServerDateTime(body?.retryAt);
            this.resendError.set(
              retry
                ? `Yeni bağlantı kısa süre önce istendi. ${retry} sonrasında tekrar deneyebilirsin.`
                : 'Yeniden gönderim sınırına ulaşıldı. Öğrencinin yeni bir davet göndermesini isteyebilirsin.'
            );
          } else {
            this.resendError.set('Bağlantı şu anda gönderilemedi. Lütfen biraz sonra tekrar dene.');
          }
        },
      });
  }

  private onActivateError(err: HttpErrorResponse): void {
    const body = errorBody(err);
    switch (body?.errorCode) {
      case PARENT_INVITATION_ERROR_CODES.consentRequired:
        this.submitError.set('Devam etmek için rıza metnini onaylamalısın.');
        return;
      case PARENT_INVITATION_ERROR_CODES.consentVersionMismatch:
        this.submitError.set('Rıza metni güncellendi. Lütfen güncel metni okuyup yeniden onayla.');
        this.form.controls.consent.setValue(false);
        this.refreshLookupSilently();
        return;
      case PARENT_INVITATION_ERROR_CODES.passwordRejected:
        this.submitError.set(
          body.message?.trim() ||
            `Şifre kabul edilmedi. En az ${PARENT_PASSWORD_MIN_LENGTH} karakter, harf ve rakam içeren daha güçlü bir şifre seç.`
        );
        return;
      case PARENT_INVITATION_ERROR_CODES.activationTokenExpired:
        this.loadLookup();
        return;
      case PARENT_INVITATION_ERROR_CODES.activationTokenUsed:
        this.view.set('used');
        return;
      case PARENT_INVITATION_ERROR_CODES.loginRequired: {
        // Bu e-postayla hesap var: anonim aktivasyon bağlamaz → giriş yapıp kabul et (şifre alanları kalkar, rıza korunur).
        const consent = this.form.controls.consent.value;
        this.lookup.update((l) => (l ? { ...l, accountExists: true, requiresPasswordSetup: false } : l));
        this.form.reset({ consent });
        this.applyAccountKind(false);
        return;
      }
    }
    if (err?.status === 404) {
      this.view.set('invalid');
      return;
    }
    if (err?.status === 429) {
      this.submitError.set('Çok fazla deneme yapıldı. Lütfen birkaç dakika sonra tekrar dene.');
      return;
    }
    if (err?.status === 502) {
      this.submitError.set('Hesap servisine şu anda ulaşılamıyor. Lütfen biraz sonra tekrar dene.');
      return;
    }
    this.submitError.set('Hesap aktive edilemedi. Lütfen tekrar dene.');
  }

  /**
   * Hesabı olan veli: rıza zorunlu. Oturum açıksa doğrudan `accept`; değilse token + rıza sessionStorage'a yazılır ve giriş
   * bu sayfaya dönecek şekilde başlatılır (token URL'ye yazılmaz).
   */
  loginAndAccept(): void {
    const lookup = this.lookup();
    if (!this.token || !lookup || this.submitting()) {
      return;
    }
    const consent = this.form.controls.consent;
    if (!consent.value) {
      consent.markAsTouched();
      return;
    }
    if (this.loggedIn()) {
      this.accept(lookup.consentVersion);
      return;
    }
    this.pending.save(this.token, lookup.consentVersion);
    this.redirect(PARENT_ACTIVATE_LOGIN_URL);
  }

  /** Tarayıcı navigasyonu — spec'te spy'lanır. */
  redirect(url: string): void {
    window.location.href = url;
  }

  /** Giriş turundan dönüş: rıza aynı sürüme verildiyse ve oturum açıksa kabul kendiliğinden yapılır. */
  private resumeAfterLogin(lookup: ParentActivationLookup): void {
    const resumed = this.resumed;
    this.resumed = null;
    if (!resumed || !this.accountExists()) {
      return;
    }
    if (resumed.consentAccepted && resumed.consentVersion === lookup.consentVersion) {
      this.form.controls.consent.setValue(true);
      if (this.loggedIn()) {
        this.accept(lookup.consentVersion);
        return;
      }
    }
    // Oturum yok ya da rıza metni değişti: kullanıcı yeniden onaylar.
    this.pending.clear();
  }

  private accept(consentVersion: string): void {
    if (!this.token) {
      return;
    }
    this.submitting.set(true);
    this.submitError.set(null);
    this.service
      .accept({ token: this.token, consentAccepted: true, consentVersion })
      .pipe(finalize(() => this.submitting.set(false)))
      .subscribe({
        next: (result) => {
          this.pending.clear();
          this.form.reset();
          this.result.set(result);
          this.view.set('accepted');
        },
        error: (err: HttpErrorResponse) => this.onAcceptError(err),
      });
  }

  private onAcceptError(err: HttpErrorResponse): void {
    const body = errorBody(err);
    if (err?.status === 401 && this.token) {
      // Oturum düşmüş: bekleyen kabulü sakla, yeniden giriş yap.
      this.loggedIn.set(false);
      this.pending.save(this.token, this.lookup()?.consentVersion ?? '');
      this.redirect(PARENT_ACTIVATE_LOGIN_URL);
      return;
    }
    switch (body?.errorCode) {
      case PARENT_INVITATION_ERROR_CODES.consentRequired:
      case PARENT_INVITATION_ERROR_CODES.consentVersionMismatch:
        this.pending.clear();
        this.submitError.set('Rıza metni güncellendi. Lütfen güncel metni okuyup yeniden onayla.');
        this.form.controls.consent.setValue(false);
        this.refreshLookupSilently();
        return;
      case PARENT_INVITATION_ERROR_CODES.invitationEmailMismatch:
        this.pending.clear();
        this.submitError.set('Bu davet başka bir e-posta adresine gönderildi; davetin geldiği e-postayla giriş yapın.');
        return;
      case PARENT_INVITATION_ERROR_CODES.parentAccountRequired:
        this.pending.clear();
        this.submitError.set('Bu daveti yalnız bir veli hesabı kabul edebilir.');
        return;
      case PARENT_INVITATION_ERROR_CODES.studentLimitReached:
        this.pending.clear();
        this.submitError.set(body.message?.trim() || 'Öğrencinin bağlı veli sınırına ulaşıldı; davet kabul edilemedi.');
        return;
      case PARENT_INVITATION_ERROR_CODES.activationTokenExpired:
        this.loadLookup();
        return;
      case PARENT_INVITATION_ERROR_CODES.activationTokenUsed:
        this.pending.clear();
        this.view.set('used');
        return;
    }
    this.pending.clear();
    if (err?.status === 404) {
      this.view.set('invalid');
    } else if (err?.status === 403) {
      this.submitError.set('Bu daveti kabul etme yetkin yok.');
    } else if (err?.status === 429) {
      this.submitError.set('Çok fazla deneme yapıldı. Lütfen birkaç dakika sonra tekrar dene.');
    } else if (err?.status === 502) {
      this.submitError.set('Hesap servisine şu anda ulaşılamıyor. Lütfen biraz sonra tekrar dene.');
    } else {
      this.submitError.set('Davet kabul edilemedi. Lütfen tekrar dene.');
    }
  }

  /** Rıza sürümü değişti: görünümü bozmadan güncel sürümü al. */
  private refreshLookupSilently(): void {
    if (!this.token) {
      return;
    }
    this.service.lookup(this.token).subscribe({
      next: (lookup) => this.lookup.set(lookup),
      error: () => undefined,
    });
  }

  /** Mevcut veli (şifresi var) için şifre alanları formdan çıkar. */
  private applyAccountKind(newAccount: boolean): void {
    const controls = [this.form.controls.password, this.form.controls.confirmPassword];
    for (const control of controls) {
      if (newAccount) {
        control.enable({ emitEvent: false });
      } else {
        control.disable({ emitEvent: false });
      }
    }
    this.form.updateValueAndValidity({ emitEvent: false });
  }
}

function errorBody(err: HttpErrorResponse): ParentInvitationErrorBody | null {
  return err?.error && typeof err.error === 'object' ? (err.error as ParentInvitationErrorBody) : null;
}
