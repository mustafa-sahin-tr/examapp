import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';

import { LoginComponent } from './login.component';
import { LOCALE_STORAGE_KEY } from '../../services/locale-hint.service';

/**
 * LoginComponent tek iş yapar: token yoksa Keycloak'a (`/oidc-login`) yönlendirir.
 * Gerçek navigasyon testte çalışmasın diye `redirect()` spy'lanır — bu yüzden
 * komponentte ayrı bir `protected redirect(url)` metodu var.
 */
describe('LoginComponent', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let component: LoginComponent;
  let redirectSpy: jasmine.Spy<(url: string) => void>;

  beforeEach(async () => {
    localStorage.clear();

    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    // ngOnInit'ten (detectChanges) önce kur, aksi halde tarayıcı gerçekten yönlenir.
    redirectSpy = spyOn(component as unknown as { redirect: (url: string) => void }, 'redirect');
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('should create', () => {
    fixture.detectChanges();

    expect(component).toBeTruthy();
  });

  it('token yokken /oidc-login adresine kc_locale ile yönlendirir', () => {
    localStorage.setItem(LOCALE_STORAGE_KEY, 'en');

    fixture.detectChanges();

    expect(redirectSpy).toHaveBeenCalledTimes(1);
    const url = new URL(redirectSpy.calls.mostRecent().args[0], 'http://localhost');
    expect(url.pathname).toBe('/oidc-login');
    expect(url.searchParams.get('kc_locale')).toBe('en');
  });

  it('mevcut query parametrelerini koruyarak kc_locale ekler', () => {
    localStorage.setItem(LOCALE_STORAGE_KEY, 'tr');

    const url = new URL(
      (component as unknown as { withLoginLocale: (u: string) => string }).withLoginLocale(
        '/oidc-login?redirect_uri=%2Fdashboard'
      ),
      'http://localhost'
    );

    expect(url.searchParams.get('redirect_uri')).toBe('/dashboard');
    expect(url.searchParams.get('kc_locale')).toBe('tr');
  });
});
