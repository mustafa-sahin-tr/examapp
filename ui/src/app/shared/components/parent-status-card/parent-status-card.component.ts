import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import {
  PARENT_INVITATION_ERROR_CODES,
  PARENT_REQUIRED_URL,
  formatServerDateTime,
  parentErrorCode,
  parseServerDate,
  safeDevActivationUrl,
} from '../../../models/parent-status.model';
import { AuthService } from '../../../services/auth.service';
import { LocaleService } from '../../../services/locale.service';
import { ParentStatusStore } from '../../../services/parent-status.store';
import { StudentParentService } from '../../../services/student-parent.service';
import { describeParentInvitationError } from '../../utils/parent-invitation-error';

/** Hoşgörü banner'ının kapatıldığını tutan sessionStorage anahtarı (kullanıcı başına; yeni oturumda geri gelir). */
export const PARENT_GRACE_BANNER_DISMISS_KEY = 'parentGraceBannerDismissed';

/**
 * Issue #440 / #441: öğrenci panelinin veli durumu alanı.
 * - `AwaitingActivation`: "Velinize davet gönderildi: {maskeli e-posta}" kartı — bitiş, süresi dolunca "Davet süresi doldu,
 *   yeniden gönder", yeniden gönder (24 saatte 1 / toplam 5; `nextResendAt`, `resendsLeft`), "E-postayı düzelt" (veli formu),
 *   e-posta kilidi ve yalnız Development'ta "dev: aktivasyon bağlantısı".
 * - `Grace` (son günler hariç): kapatılabilir "Velini ekle, X gün kaldı" banner'ı — kapatma sessionStorage'da (her oturumda
 *   geri gelir). Son günlerde banner yerine layout'taki kapatılamaz modal (`ParentFinalWarningComponent`) devreye girer.
 * Durum `ParentStatusStore`'dan okunur; yoksa (öğrenci olmayan / henüz yüklenmedi) hiçbir şey render edilmez.
 */
@Component({
  selector: 'app-parent-status-card',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, RouterLink, TranslocoDirective],
  templateUrl: './parent-status-card.component.html',
  styleUrl: './parent-status-card.component.scss',
})
export class ParentStatusCardComponent {
  private readonly store = inject(ParentStatusStore);
  private readonly service = inject(StudentParentService);
  private readonly auth = inject(AuthService);
  private readonly localeService = inject(LocaleService);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly parentRequiredUrl = PARENT_REQUIRED_URL;
  protected readonly invitation = this.store.invitation;
  protected readonly daysLeft = this.store.daysLeft;
  protected readonly awaitingDeadline = this.store.awaitingDeadline;

  readonly resending = signal(false);
  readonly resendError = signal<string | null>(null);
  readonly resendDone = signal(false);
  private readonly dismissed = signal(this.readDismissed());

  readonly showBanner = computed(() => this.store.graceBanner() && !this.dismissed());
  readonly expiresAtText = computed(() => this.formatDate(this.invitation()?.expiresAt));
  readonly nextResendText = computed(() => this.formatDate(this.invitation()?.nextResendAt));
  readonly emailLockedText = computed(() => {
    const until = this.invitation()?.emailChangeLockedUntil;
    const date = parseServerDate(until);
    return date && date.getTime() > Date.now() ? this.formatDate(until) : null;
  });
  readonly devActivationUrl = computed(() => safeDevActivationUrl(this.invitation()?.devActivationUrl));

  resend(): void {
    if (this.resending() || !this.invitation()?.canResend) {
      return;
    }
    this.resending.set(true);
    this.resendError.set(null);
    this.resendDone.set(false);
    this.service
      .resend()
      .pipe(
        finalize(() => this.resending.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: () => this.resendDone.set(true),
        error: (err: unknown) => {
          const text = describeParentInvitationError(err, this.locale());
          this.resendError.set(this.transloco.translate<string>(text.key, text.params) ?? '');
          const code = parentErrorCode(err);
          if (code === PARENT_INVITATION_ERROR_CODES.noOpenInvitation || code === PARENT_INVITATION_ERROR_CODES.resendLimited) {
            // Sunucu durumu değişmiş (davet kapanmış / sayaç): kart güncel bilgiyle yeniden çizilsin.
            this.service.reload().subscribe({ error: () => undefined });
          }
        },
      });
  }

  dismissBanner(): void {
    this.dismissed.set(true);
    try {
      sessionStorage.setItem(this.dismissKey(), '1');
    } catch {
      /* depolama yoksa yalnız bu görünümde kapanır */
    }
  }

  private readDismissed(): boolean {
    try {
      return typeof sessionStorage !== 'undefined' && sessionStorage.getItem(this.dismissKey()) === '1';
    } catch {
      return false;
    }
  }

  private dismissKey(): string {
    const userId = this.auth.user()?.id;
    return userId ? `${PARENT_GRACE_BANNER_DISMISS_KEY}:${userId}` : PARENT_GRACE_BANNER_DISMISS_KEY;
  }

  private formatDate(iso: string | null | undefined): string {
    return formatServerDateTime(iso, this.locale());
  }

  private locale(): string {
    return this.localeService.localeDefinition().angularLocale;
  }
}
