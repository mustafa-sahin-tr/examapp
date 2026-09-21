import { Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoDirective } from '@jsverse/transloco';

import { AppLocale, SUPPORTED_LOCALES } from '../../../models/locale';
import { LocaleService } from '../../../services/locale.service';
import { LocalePreferenceService } from '../../../services/locale-preference.service';

/**
 * Global toolbar'daki dil seçici (issue #180, #181). Menü içeriği `SUPPORTED_LOCALES`'ten üretilir;
 * yeni bir dil eklendiğinde bu komponentte değişiklik gerekmez.
 *
 * Seçim {@link LocalePreferenceService} üzerinden yapılır: oturum açmış kullanıcıda tercih
 * profile yazılır, ardından dil uygulanır (sayfa yeniden yüklenir).
 */
@Component({
  selector: 'app-language-switcher',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatMenuModule, MatTooltipModule, TranslocoDirective],
  templateUrl: './language-switcher.component.html',
  styleUrl: './language-switcher.component.scss',
})
export class LanguageSwitcherComponent {
  private readonly localeService = inject(LocaleService);
  private readonly localePreference = inject(LocalePreferenceService);

  /** `as const` tipi korunur: `locale.code` `AppLocale` olarak daralır, cast gerekmez. */
  readonly locales = SUPPORTED_LOCALES;
  readonly activeLocale = this.localeService.locale;
  readonly activeLocaleLabel = computed(() => this.localeService.localeDefinition().code.toUpperCase());

  isActive(code: AppLocale): boolean {
    return code === this.activeLocale();
  }

  selectLocale(code: AppLocale): void {
    this.localePreference.persistPreference(code).subscribe();
  }
}
