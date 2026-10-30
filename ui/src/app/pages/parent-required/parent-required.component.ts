import { Component, DestroyRef, OnInit, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { PARENT_INVITATION_ERROR_CODES, formatServerDateTime, parentErrorCode, parseServerDate } from '../../models/parent-status.model';
import { LocaleService } from '../../services/locale.service';
import { ParentStatusStore } from '../../services/parent-status.store';
import { StudentParentService } from '../../services/student-parent.service';
import {
  PARENT_EMAIL_MAX_LENGTH,
  PARENT_NAME_MAX_LENGTH,
  PARENT_PHONE_INPUT_MAX_LENGTH,
  parentEmailValidator,
  parentNameValidator,
  parentPhoneValidator,
} from '../../shared/utils/parent-contact.util';
import { describeParentInvitationError } from '../../shared/utils/parent-invitation-error';
import { PhoneMaskDirective } from '../../shared/directives/phone-mask.directive';

/** Başarılı davetten sonra (kısıtlı mod) gidilen ana sayfa. */
export const PARENT_REQUIRED_DONE_URL = '/dashboard';

/**
 * Issue #440 / #441: "Veli bilgisi ekle" — hoşgörü süresi dolan öğrencinin tek ekranı (`parentRequirementGuard`), hoşgörü
 * banner'ının / son uyarı modalının hedefi ve davet kartındaki "E-postayı düzelt". Form `POST student-parent/invitation`'a gider:
 * başarıda durum `AwaitingActivation` olur (kısıtlı mod) ve öğrenci ana sayfaya döner. Telefon durum yanıtında olmadığı için
 * yalnız veli adı önceden doldurulur.
 */
@Component({
  selector: 'app-parent-required',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    RouterLink,
    TranslocoDirective,
    TranslocoPipe,
    PhoneMaskDirective,
  ],
  templateUrl: './parent-required.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './parent-required.component.scss',
})
export class ParentRequiredComponent implements OnInit {
  private readonly service = inject(StudentParentService);
  private readonly store = inject(ParentStatusStore);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(FormBuilder);

  protected readonly nameMaxLength = PARENT_NAME_MAX_LENGTH;
  protected readonly emailMaxLength = PARENT_EMAIL_MAX_LENGTH;
  protected readonly phoneMaxLength = PARENT_PHONE_INPUT_MAX_LENGTH;
  protected readonly dashboardUrl = PARENT_REQUIRED_DONE_URL;

  readonly loading = signal(false);
  readonly loadError = signal(false);
  readonly submitting = signal(false);
  readonly submitError = signal<string | null>(null);
  readonly resending = signal(false);
  readonly resendError = signal<string | null>(null);

  readonly status = this.store.status;
  readonly mode = this.store.mode;
  readonly invitation = this.store.invitation;
  /** Form gösterilsin mi: velisi bağlı (`Ok`) öğrencide gerek yok. */
  readonly showForm = computed(() => {
    const mode = this.mode();
    return mode === 'Grace' || mode === 'Expired' || mode === 'AwaitingActivation';
  });
  /** Farklı e-posta kilidi (1 saatte 3 deneme) hâlâ sürüyorsa bitiş metni. */
  readonly emailLockedUntil = computed(() => {
    const until = this.invitation()?.emailChangeLockedUntil;
    const date = parseServerDate(until);
    return date && date.getTime() > Date.now() ? this.formatDate(until) : null;
  });

  readonly form = this.fb.nonNullable.group({
    parentFullName: ['', [Validators.required, parentNameValidator]],
    parentEmail: ['', [Validators.required, parentEmailValidator]],
    parentPhone: ['', [Validators.required, parentPhoneValidator]],
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(false);
    this.service
      .reload()
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: () => this.prefill(),
        error: () => this.loadError.set(true),
      });
  }

  hasError(control: 'parentFullName' | 'parentEmail' | 'parentPhone', error: string): boolean {
    return this.form.controls[control].hasError(error);
  }

  submit(): void {
    if (this.submitting()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { parentFullName, parentEmail, parentPhone } = this.form.getRawValue();
    this.submitting.set(true);
    this.submitError.set(null);
    this.service
      .startInvitation({
        parentFullName: parentFullName.trim(),
        parentEmail: parentEmail.trim(),
        parentPhone: parentPhone.trim(),
      })
      .pipe(
        finalize(() => this.submitting.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: () => {
          this.snackBar.open(this.t('parentRequirement.page.success'), this.t('common.close'), { duration: 4000 });
          void this.router.navigateByUrl(PARENT_REQUIRED_DONE_URL);
        },
        error: (err: unknown) => {
          const text = describeParentInvitationError(err, this.locale());
          this.submitError.set(this.t(text.key, text.params));
          if (parentErrorCode(err) === PARENT_INVITATION_ERROR_CODES.parentAlreadyLinked) {
            // Bu arada veli aktive etmiş: güncel durum (`Ok`) formu kapatır.
            this.service.reload().subscribe({ error: () => undefined });
          }
        },
      });
  }

  /**
   * #440 review: süresi dolmuş davetle `Expired` — aynı e-postaya yeniden gönder. Davet yeniden açılınca durum kısıtlı moda
   * (`AwaitingActivation`) geçer ve ana sayfaya dönülür; sunucu hâlâ `Expired` derse ekranda kalınır.
   */
  resend(): void {
    if (this.resending()) {
      return;
    }
    this.resending.set(true);
    this.resendError.set(null);
    this.service
      .resend()
      .pipe(
        finalize(() => this.resending.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: () => {
          this.snackBar.open(this.t('parentRequirement.card.resent'), this.t('common.close'), { duration: 4000 });
          if (!this.store.expired()) {
            void this.router.navigateByUrl(PARENT_REQUIRED_DONE_URL);
          }
        },
        error: (err: unknown) => {
          const text = describeParentInvitationError(err, this.locale());
          this.resendError.set(this.t(text.key, text.params));
          this.service.reload().subscribe({ error: () => undefined });
        },
      });
  }

  /** "E-postayı düzelt": adı mevcut davetten doldur (telefon durum yanıtında yok). */
  private prefill(): void {
    const name = this.invitation()?.parentName;
    if (name && !this.form.controls.parentFullName.value) {
      this.form.controls.parentFullName.setValue(name);
    }
  }

  protected formatDate(iso: string | null | undefined): string {
    return formatServerDateTime(iso, this.locale());
  }

  private locale(): string {
    return this.localeService.localeDefinition().angularLocale;
  }

  private t(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(key, params) ?? '';
  }
}
