import {
  ApplicationConfig,
  LOCALE_ID,
  importProvidersFrom,
  inject,
  isDevMode,
  provideAppInitializer,
  provideZoneChangeDetection,
} from '@angular/core';
import { provideRouter, withRouterConfig } from '@angular/router';

import { routes } from './app.routes';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';

import { provideStore } from '@ngrx/store';
import { reducers } from './state/app.state';
import { ReactiveFormsModule } from '@angular/forms';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { authInterceptor } from './shared/interceptors/auth.interceptor';
import { authErrorInterceptor } from './shared/interceptors/auth-error.interceptor';
import { cacheInterceptor } from './shared/interceptors/cache.interceptor';
import { localeInterceptor } from './shared/interceptors/locale.interceptor';
import { CoreModule } from './core/core.module';
import { provideClientHydration } from '@angular/platform-browser';
import { MAT_DATE_LOCALE, MatDateFormats } from '@angular/material/core';
import { provideDateFnsAdapter } from '@angular/material-date-fns-adapter';
import { registerLocaleData } from '@angular/common';
import { TranslocoService, provideTransloco } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { SUPPORTED_LOCALES, SUPPORTED_LOCALE_CODES } from './models/locale';
import { LocaleService, readStoredLocale } from './services/locale.service';
import { TranslocoHttpLoader } from './services/transloco-http.loader';

// Desteklenen tüm diller için Angular locale verisi kaydı — `date`/`number` pipe'ları LOCALE_ID
// hangi dile ayarlanırsa ayarlansın veriyi hazır bulur (NG0701'in kalıcı çözümü).
SUPPORTED_LOCALES.forEach((locale) => registerLocaleData(locale.angularLocaleData, locale.angularLocale));

// Tüm datepicker'lar için GG/AA/YYYY parse + display formatı.
export const TR_DATE_FORMATS: MatDateFormats = {
  parse: {
    dateInput: 'dd/MM/yyyy',
  },
  display: {
    dateInput: 'dd/MM/yyyy',
    monthYearLabel: 'MMMM yyyy',
    dateA11yLabel: 'dd MMMM yyyy',
    monthYearA11yLabel: 'MMMM yyyy',
  },
};

export const appConfig: ApplicationConfig = {
  providers: [
    provideHttpClient(
      withInterceptors([cacheInterceptor, localeInterceptor, authInterceptor, authErrorInterceptor])
    ),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withRouterConfig({ onSameUrlNavigation: 'reload' })),
    provideAnimationsAsync(),
    provideStore(reducers),
    importProvidersFrom(ReactiveFormsModule, CoreModule),
    provideClientHydration(),
    provideTransloco({
      config: {
        availableLangs: [...SUPPORTED_LOCALE_CODES],
        // Başlangıç dili: kullanıcının localStorage tercihi (yoksa DEFAULT_LOCALE).
        // LocaleService enjekte edilmez — TRANSLOCO_CONFIG ↔ LocaleService döngüsü oluşmasın.
        defaultLang: readStoredLocale(),
        fallbackLang: SUPPORTED_LOCALE_CODES[0],
        reRenderOnLangChange: true,
        prodMode: !isDevMode(),
      },
      loader: TranslocoHttpLoader,
    }),
    // Aktif dilin sözlüğü bootstrap tamamlanmadan yüklenir: komponentler `translate()` çağırdığında
    // sözlük hazır olur, ilk render'da anahtar metni görünmez. Yükleme başarısız olursa uygulama
    // yine de açılır (eksik anahtarlar Transloco'nun missing handler'ına düşer).
    provideAppInitializer(() => {
      const transloco = inject(TranslocoService);
      return firstValueFrom(transloco.load(transloco.getActiveLang())).catch((error: unknown) => {
        console.warn('i18n sözlüğü yüklenemedi', error);
        return undefined;
      });
    }),
    provideDateFnsAdapter(TR_DATE_FORMATS),
    // LOCALE_ID ve MAT_DATE_LOCALE tek kaynaktan (LocaleService → SUPPORTED_LOCALES) türer.
    { provide: LOCALE_ID, useFactory: () => inject(LocaleService).localeDefinition().angularLocale },
    { provide: MAT_DATE_LOCALE, useFactory: () => inject(LocaleService).localeDefinition().dateFnsLocale },
  ],
};
