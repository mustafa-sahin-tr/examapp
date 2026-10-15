import { Injectable, computed, signal } from '@angular/core';

import { ParentStatus } from '../models/parent-status.model';

/**
 * Issue #440 / #441: öğrencinin veli durumu — tek reaktif kaynak (bağımlılıksız; komponentler HttpClient'a gerek duymadan
 * okur). Yazanlar: `AuthService.refreshProfile` (`student.parentStatus`), `StudentParentService` (durum ucu ve davet
 * yanıtları). Bellekte tutulur; sayfa yenilenince ilk guard/istek yeniden yükler. Öğrenci olmayanlarda hep `null`.
 */
@Injectable({ providedIn: 'root' })
export class ParentStatusStore {
  private readonly state = signal<ParentStatus | null>(null);

  /** Sunucudan gelen son durum (bu oturumda henüz yüklenmediyse null). */
  readonly status = this.state.asReadonly();
  readonly loaded = computed(() => this.state() !== null);
  readonly mode = computed(() => this.state()?.mode ?? null);

  /** #440: davet açık, veli aktive etmedi → booking / DM / yorum / liderlik kapalı. */
  readonly activationRestricted = computed(() => this.state()?.mode === 'AwaitingActivation');
  /** #441: hoşgörü süresi doldu → yalnız "veli bilgisi ekle" ekranı. */
  readonly expired = computed(() => this.state()?.mode === 'Expired');
  /** #441: hoşgörü süresi sürüyor ve son günler değil → kapatılabilir banner. */
  readonly graceBanner = computed(() => {
    const s = this.state();
    return s?.mode === 'Grace' && !s.isFinalWarning;
  });
  /** #441: hoşgörünün son günleri → kapatılamaz modal. */
  readonly finalWarning = computed(() => {
    const s = this.state();
    return s?.mode === 'Grace' && s.isFinalWarning === true;
  });
  readonly daysLeft = computed(() => this.state()?.daysLeft ?? null);
  /**
   * Açık / süresi dolmuş davet: `AwaitingActivation`'da ve (#440 review) süresi dolmuş davetle `Expired`'da gelir — yeniden gönder
   * ve "e-postayı düzelt" için.
   */
  readonly invitation = computed(() => {
    const s = this.state();
    return s?.mode === 'AwaitingActivation' || s?.mode === 'Expired' ? s.invitation ?? null : null;
  });
  /**
   * #440 review: `AwaitingActivation`'da da hoşgörü bilgisi gelir — veli aktive etmezse hesabın ne zaman kısıtlanacağı
   * (`daysLeft` 0 = süre doldu). Son günlerde (`isFinalWarning`) kart uyarı renginde; modal yalnız `Grace`'te.
   */
  readonly awaitingDeadline = computed(() => {
    const s = this.state();
    if (s?.mode !== 'AwaitingActivation' || typeof s.daysLeft !== 'number') {
      return null;
    }
    return { daysLeft: s.daysLeft, warning: s.isFinalWarning === true };
  });

  set(status: ParentStatus | null | undefined): void {
    if (status && typeof status === 'object' && typeof status.mode === 'string') {
      this.state.set(status);
    }
  }

  /** Çıkışta / kullanıcı değişince. */
  reset(): void {
    this.state.set(null);
  }
}
