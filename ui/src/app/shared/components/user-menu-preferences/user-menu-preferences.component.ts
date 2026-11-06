import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonToggleChange, MatButtonToggleModule } from '@angular/material/button-toggle';
import { TranslocoDirective } from '@jsverse/transloco';

import { AppLocale, SUPPORTED_LOCALES } from '../../../models/locale';
import { ColorScheme, ColorSchemeService } from '../../../services/color-scheme.service';
import { LocalePreferenceService } from '../../../services/locale-preference.service';
import { LocaleService } from '../../../services/locale.service';

/**
 * #846: kullanıcı menüsünün içindeki tema ve dil seçimi (üst çubukta yer kaplamasın). İki küçük segmentli seçim; menünün
 * içinde durduğu için tıklama menüyü kapatmaz. Dil seçimi `LocalePreferenceService` ile (profil + sayfa yenilenir), tema
 * `ColorSchemeService` ile.
 */
@Component({
  selector: 'app-user-menu-preferences',
  standalone: true,
  imports: [MatButtonToggleModule, TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(click)': '$event.stopPropagation()', '(keydown)': '$event.stopPropagation()' },
  template: `
    <div class="ump" *transloco="let t; prefix: 'layout.preferences'">
      <div class="ump__row">
        <span class="ump__label" id="ump-theme">{{ t('theme') }}</span>
        <mat-button-toggle-group hideSingleSelectionIndicator aria-labelledby="ump-theme" data-testid="ump-theme"
          [value]="colorScheme()" (change)="setScheme($event)">
          <mat-button-toggle value="dark">{{ t('dark') }}</mat-button-toggle>
          <mat-button-toggle value="light">{{ t('light') }}</mat-button-toggle>
        </mat-button-toggle-group>
      </div>
      <div class="ump__row">
        <span class="ump__label" id="ump-language">{{ t('language') }}</span>
        <mat-button-toggle-group hideSingleSelectionIndicator aria-labelledby="ump-language" data-testid="ump-language"
          [value]="locale()" (change)="setLocale($event)">
          @for (l of locales; track l.code) {
            <mat-button-toggle [value]="l.code" [attr.aria-label]="l.label">{{ l.code.toUpperCase() }}</mat-button-toggle>
          }
        </mat-button-toggle-group>
      </div>
    </div>
  `,
  styleUrl: './user-menu-preferences.component.scss',
})
export class UserMenuPreferencesComponent {
  private readonly colorSchemeService = inject(ColorSchemeService);
  private readonly localePreference = inject(LocalePreferenceService);

  readonly locales = SUPPORTED_LOCALES;
  readonly colorScheme = this.colorSchemeService.colorScheme;
  readonly locale = inject(LocaleService).locale;

  setScheme(event: MatButtonToggleChange): void {
    this.colorSchemeService.setScheme(event.value as ColorScheme);
  }

  setLocale(event: MatButtonToggleChange): void {
    if (event.value !== this.locale()) {
      this.localePreference.persistPreference(event.value as AppLocale).subscribe();
    }
  }
}
