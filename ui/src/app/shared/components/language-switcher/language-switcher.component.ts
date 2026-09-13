import { Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoDirective } from '@jsverse/transloco';

import { AppLocale, LocaleDefinition, SUPPORTED_LOCALES } from '../../../models/locale';
import { LocaleService } from '../../../services/locale.service';

/**
 * Global toolbar'daki dil seçici (issue #180). Menü içeriği `SUPPORTED_LOCALES`'ten üretilir;
 * yeni bir dil eklendiğinde bu komponentte değişiklik gerekmez.
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

  readonly locales: readonly LocaleDefinition[] = SUPPORTED_LOCALES;
  readonly activeLocale = this.localeService.locale;
  readonly activeLocaleLabel = computed(() => this.localeService.localeDefinition().code.toUpperCase());

  isActive(code: string): boolean {
    return code === this.activeLocale();
  }

  selectLocale(code: string): void {
    this.localeService.setLocale(code as AppLocale);
  }
}
