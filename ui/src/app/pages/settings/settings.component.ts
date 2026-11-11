import { Component, DestroyRef, OnInit, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { SettingsRole } from '../../models/account-settings.model';
import { AppLocale, SUPPORTED_LOCALES } from '../../models/locale';
import { AuthService } from '../../services/auth.service';
import { LocaleService } from '../../services/locale.service';
import { LocalePreferenceService } from '../../services/locale-preference.service';
import { ParentService } from '../../services/parent.service';
import { AccountSecurityCardComponent } from '../../shared/components/account-security-card/account-security-card.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { KEYCLOAK_ACCOUNT_CONSOLE_PATH } from '../../models/account-settings.model';

const SCOPE = 'settings';

/**
 * Issue #417 — öğretmen ve admin Ayarlar sayfası (öğrenci kendi `/student-profile` sayfasını kullanır).
 *
 * Ortak: ad/e-posta/avatar (salt-okunur — öğretmen/admin için güncelleme ucu yok), dil tercihi
 * (`LocalePreferenceService` → `PUT /api/auth/me/locale`), Keycloak hesap konsolu kartı (şifre değiştirme, `AccountSecurityCardComponent`).
 * Öğretmen: okul bilgisi (salt-okunur) ve bağımsız öğretmene özel ders profili bağlantısı (#384 kuralı).
 * Veli: Bildirimler kartı — haftalık raporun e-postası (#1112, `GET|PUT /api/exam/parent/settings`).
 */
@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [
    PageHeaderComponent,
    MatButtonModule,
    MatButtonToggleModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSlideToggleModule,
    RouterLink,
    TranslocoDirective,
    AccountSecurityCardComponent,
  ],
  providers: [provideTranslocoScope(SCOPE)],
  templateUrl: './settings.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './settings.component.scss',
})
export class SettingsComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly localeService = inject(LocaleService);
  private readonly localePreference = inject(LocalePreferenceService);
  private readonly parentService = inject(ParentService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly loading = signal(false);
  protected readonly error = signal(false);
  /** Dil kaydediliyor — başarıda sayfa yeniden yüklenir; akış (reload'suz da) bitince kilit kalkar. */
  protected readonly savingLocale = signal(false);
  /** Avatar görseli yüklenemedi (kırık/eski yol) → baş harflere düşülür. */
  protected readonly avatarFailed = signal(false);

  protected readonly profile = this.authService.user;
  protected readonly locales = SUPPORTED_LOCALES;
  protected readonly activeLocale = this.localeService.locale;

  protected readonly isTeacher = this.authService.hasRealmRole('Teacher');
  protected readonly isAdmin = this.authService.hasRealmRole('Admin');
  /** Rol rozeti: admin yetkisi öğretmenliği bastırır (her iki rolde de admin yazılır). */
  /** #1112: Bildirimler kartı yalnız veliye. */
  protected readonly isParent = this.authService.hasRealmRole('Parent');
  /** Haftalık rapor e-postası; yüklenene kadar null (anahtar kapalı görünmez, devre dışı). */
  protected readonly weeklyReportEmail = signal<boolean | null>(null);
  protected readonly parentSettingsError = signal<'load' | 'save' | null>(null);
  protected readonly savingParentSettings = signal(false);
  protected readonly primaryRole: SettingsRole = this.isAdmin ? 'Admin' : this.isTeacher ? 'Teacher' : 'Parent';

  /** Okul adı; bağımsız/okulsuz öğretmende null. */
  /** #918: ad / e-posta değişikliği Keycloak hesap sayfasında ("Hesabım" kartıyla aynı adres). */
  protected readonly accountConsoleUrl = KEYCLOAK_ACCOUNT_CONSOLE_PATH;
  protected readonly schoolName = computed(() => this.profile()?.teacher?.schoolName?.trim() || null);
  /**
   * #384 kuralı: özel ders profili yalnız bağımsız öğretmene. Bayrak bilinmiyorsa bağlantı gösterilir — asıl kapı
   * `independentTeacherGuard` ve backend (menüdeki `onlyIndependentTeacher` ile aynı davranış).
   */
  protected readonly showTutorProfileLink = computed(
    () => this.isTeacher && AuthService.isIndependentTutorOf(this.profile()) !== false
  );
  protected readonly initials = computed(() => {
    const name = this.profile()?.fullName?.trim() ?? '';
    return name
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part.charAt(0).toLocaleUpperCase())
      .join('');
  });

  ngOnInit(): void {
    this.load();
    if (this.isParent) {
      this.loadParentSettings();
    }
  }

  private loadParentSettings(): void {
    this.parentService
      .getSettings()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (s) => this.weeklyReportEmail.set(s.weeklyReportEmail),
        error: () => this.parentSettingsError.set('load'),
      });
  }

  /** Anahtar hemen döner; kayıt başarısızsa eski değere geri alınır. */
  setWeeklyReportEmail(enabled: boolean): void {
    const previous = this.weeklyReportEmail();
    this.weeklyReportEmail.set(enabled);
    this.parentSettingsError.set(null);
    this.savingParentSettings.set(true);
    this.parentService
      .updateSettings({ weeklyReportEmail: enabled })
      .pipe(
        finalize(() => this.savingParentSettings.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        error: () => {
          this.weeklyReportEmail.set(previous);
          this.parentSettingsError.set('save');
        },
      });
  }

  /** Güncel profili (okul, bağımsızlık bayrağı) exam-api'den tazeler; sonuç `AuthService.user`'a yazılır. */
  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.authService
      .refreshProfile()
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({ error: () => this.error.set(true) });
  }

  selectLocale(code: AppLocale): void {
    if (code === this.activeLocale() || this.savingLocale()) {
      return;
    }
    this.savingLocale.set(true);
    // Başarılı akış sayfayı yeniden yükler (LocaleService.setLocale); servis hataları kendisi yutar (hata dalı yok).
    this.localePreference
      .persistPreference(code)
      .pipe(
        finalize(() => this.savingLocale.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }
}
