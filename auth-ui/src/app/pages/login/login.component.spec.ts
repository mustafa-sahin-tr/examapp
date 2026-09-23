import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';

import { LoginComponent } from './login.component';
import { LOCALE_STORAGE_KEY } from '../../services/locale-hint.service';
import { authErrorInterceptor } from '../../shared/interceptors/auth-error.interceptor';

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
      providers: [
        // Gerçek authErrorInterceptor ile: login 401'inin yönlendirmeye yol açmadığını uçtan uca doğrular (issue #231).
        provideHttpClient(withInterceptors([authErrorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
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

  it('token yokken /oidc-login adresine ui_locales ile yönlendirir', () => {
    localStorage.setItem(LOCALE_STORAGE_KEY, 'en');

    fixture.detectChanges();

    expect(redirectSpy).toHaveBeenCalledTimes(1);
    const url = new URL(redirectSpy.calls.mostRecent().args[0], 'http://localhost');
    expect(url.pathname).toBe('/oidc-login');
    expect(url.searchParams.get('ui_locales')).toBe('en');
  });

  it('mevcut query parametrelerini koruyarak ui_locales ekler', () => {
    localStorage.setItem(LOCALE_STORAGE_KEY, 'tr');

    const url = new URL(
      (component as unknown as { withLoginLocale: (u: string) => string }).withLoginLocale(
        '/oidc-login?redirect_uri=%2Fdashboard'
      ),
      'http://localhost'
    );

    expect(url.searchParams.get('redirect_uri')).toBe('/dashboard');
    expect(url.searchParams.get('ui_locales')).toBe('tr');
  });

  describe('onSubmit hata yolu (issue #231)', () => {
    const fallback = 'Giriş başarısız! Lütfen bilgilerinizi kontrol edin.';
    let httpMock: HttpTestingController;
    let navigateSpy: jasmine.Spy;
    let snackSpy: jasmine.Spy;

    beforeEach(() => {
      httpMock = TestBed.inject(HttpTestingController);
      navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
      snackSpy = spyOn(component.snackBar, 'open');
      component.loginForm.setValue({ email: 'ogrenci@example.com', password: 'yanlis-parola' });
    });

    afterEach(() => httpMock.verify());

    it('401 → /login e yönlendirmez ve backend mesajını gösterir', () => {
      component.onSubmit();
      httpMock
        .expectOne('/api/auth/login')
        .flush({ message: 'E-posta veya şifre hatalı.' }, { status: 401, statusText: 'Unauthorized' });

      expect(navigateSpy).not.toHaveBeenCalled();
      expect(snackSpy).toHaveBeenCalledOnceWith('E-posta veya şifre hatalı.', 'Kapat', { duration: 3000 });
      expect(component.isLoading).toBeFalse();
    });

    it('503 → backend mesajını gösterir, yönlendirmez', () => {
      component.onSubmit();
      httpMock
        .expectOne('/api/auth/login')
        .flush({ message: 'Kimlik servisine şu anda ulaşılamıyor.' }, { status: 503, statusText: 'Service Unavailable' });

      expect(navigateSpy).not.toHaveBeenCalled();
      expect(snackSpy).toHaveBeenCalledOnceWith('Kimlik servisine şu anda ulaşılamıyor.', 'Kapat', { duration: 3000 });
    });

    it('gövdesiz hata → mevcut sabit metne düşer', () => {
      component.onSubmit();
      httpMock.expectOne('/api/auth/login').flush(null, { status: 500, statusText: 'Server Error' });

      expect(snackSpy).toHaveBeenCalledOnceWith(fallback, 'Kapat', { duration: 3000 });
    });
  });
});
