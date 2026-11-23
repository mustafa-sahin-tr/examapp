import { Injectable, computed, inject, signal } from '@angular/core';
import { AssistantService } from './assistant.service';
import { AuthService } from './auth.service';

/**
 * Asistan panelinin görünürlüğü ve açık / kapalı durumu (#507). Giriş noktaları (FAB, üst çubuk düğmesi, Ctrl+K) yalnız
 * `available` iken görünür: sunucu rolü açık (`/status` 200 + enabled) ve öğrencide velisi kapatmamış (#523).
 * Test çözme / tam ekran gizlemesi layout'ta (`suppressed`).
 */
@Injectable({ providedIn: 'root' })
export class AssistantPanelService {
  private readonly api = inject(AssistantService);
  private readonly auth = inject(AuthService);

  private readonly enabled = signal(false);
  private readonly allowedByParent = signal(true);
  /** Layout: test çözme, etkinlik oynatıcısı ya da tam ekran. */
  readonly suppressed = signal(false);
  readonly isOpen = signal(false);

  readonly available = computed(() => this.enabled() && this.allowedByParent());
  readonly visible = computed(() => this.available() && !this.suppressed());

  private loaded = false;

  /** Oturum açık kabukta bir kez sorulur; asistan kapalıysa sunucu 200 + `enabled: false` döner (console hatası yok). */
  load(): void {
    if (this.loaded || !this.auth.hasToken()) return;
    this.loaded = true;
    this.api.status().subscribe({
      next: (status) => {
        this.enabled.set(status.enabled);
        if (status.enabled && this.auth.hasRealmRole('Student')) this.checkParent();
      },
      error: () => this.enabled.set(false),
    });
  }

  /** Velinin seçimi sonradan değişebilir: panel açılırken yeniden bakılır. */
  checkParent(): void {
    if (!this.auth.hasRealmRole('Student')) return;
    this.api.studentAccess().subscribe({
      next: (r) => {
        this.allowedByParent.set(r.allowed);
        if (!r.allowed) this.isOpen.set(false);
      },
      // Okunamazsa gizlemeyiz; sunucu yine her turda denetler (403 AssistantDisabledByParent).
      error: () => undefined,
    });
  }

  open(): void {
    if (!this.visible()) return;
    this.isOpen.set(true);
    this.checkParent();
  }

  close(): void {
    this.isOpen.set(false);
  }

  toggle(): void {
    if (this.isOpen()) this.close();
    else this.open();
  }

  /** Velinin kapatması sohbet sırasında öğrenildi (403). */
  markBlockedByParent(): void {
    this.allowedByParent.set(false);
  }
}
