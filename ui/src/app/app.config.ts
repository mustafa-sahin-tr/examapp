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


export const appConfig: ApplicationConfig = {
  providers: [
    provideHttpClient(withInterceptors([authInterceptor,authErrorInterceptor])), 
    provideZoneChangeDetection({ eventCoalescing: true }), 
    provideRouter(routes, withRouterConfig({onSameUrlNavigation: 'reload'})), 
    provideAnimationsAsync(),
    provideStore(reducers),
    importProvidersFrom(ReactiveFormsModule)    
   ]
};
