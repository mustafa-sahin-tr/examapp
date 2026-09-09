import { ApplicationConfig, importProvidersFrom, provideZoneChangeDetection } from '@angular/core';
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
import { CoreModule } from './core/core.module';
import { provideClientHydration } from '@angular/platform-browser';
import { MAT_DATE_LOCALE, MatDateFormats } from '@angular/material/core';
import { provideDateFnsAdapter } from '@angular/material-date-fns-adapter';
import { tr } from 'date-fns/locale/tr';
import { registerLocaleData } from '@angular/common';
import localeTr from '@angular/common/locales/tr';

// `date`/`number` pipe'larının 'tr' locale'iyle çalışabilmesi için Angular locale verisi kaydı
// (NG0701 "Missing locale data for the locale 'tr'" hatasının kalıcı çözümü). LOCALE_ID varsayılan
// (en-US) olarak bırakıldı; mevcut varsayılan format davranışı değişmiyor.
registerLocaleData(localeTr);

// Tüm datepicker'lar için Türkçe GG/AA/YYYY parse + display formatı.
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
    provideHttpClient(withInterceptors([cacheInterceptor, authInterceptor, authErrorInterceptor])),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withRouterConfig({ onSameUrlNavigation: 'reload' })),
    provideAnimationsAsync(),
    provideStore(reducers),
    importProvidersFrom(ReactiveFormsModule, CoreModule),
    provideClientHydration(),
    provideDateFnsAdapter(TR_DATE_FORMATS),
    { provide: MAT_DATE_LOCALE, useValue: tr },
  ],
};
