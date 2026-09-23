import { TestBed } from '@angular/core/testing';
import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';

import { authErrorInterceptor } from './auth-error.interceptor';

/**
 * Issue #231: login/exchange artık yanlış kimlik bilgisinde 401 `{ message }` döndürüyor.
 * Bu uçlarda interceptor yönlendirme yapmamalı (hata bileşene kalır); diğer uçlarda
 * 401 hâlâ /login'e yönlendirir.
 */
describe('authErrorInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let navigateSpy: jasmine.Spy;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authErrorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
  });

  afterEach(() => httpMock.verify());

  function postAndFail(url: string, status: number, body: { message: string } | null): HttpErrorResponse | undefined {
    let received: HttpErrorResponse | undefined;
    http.post(url, {}).subscribe({ error: (e: HttpErrorResponse) => (received = e) });
    httpMock.expectOne(url).flush(body, { status, statusText: 'Error' });
    return received;
  }

  it('/api/auth/login 401 → yönlendirmez, hatayı mesajıyla iletir', () => {
    const error = postAndFail('/api/auth/login', 401, { message: 'E-posta veya şifre hatalı.' });

    expect(navigateSpy).not.toHaveBeenCalled();
    expect(error?.status).toBe(401);
    expect(error?.error).toEqual({ message: 'E-posta veya şifre hatalı.' });
  });

  it('/api/auth/exchange 401 → yönlendirmez', () => {
    const error = postAndFail('/api/auth/exchange', 401, { message: 'Oturum kodu geçersiz.' });

    expect(navigateSpy).not.toHaveBeenCalled();
    expect(error?.status).toBe(401);
  });

  it('başka bir uçta 401 → /login e yönlendirir', () => {
    const error = postAndFail('/api/auth/complete-profile', 401, null);

    expect(navigateSpy).toHaveBeenCalledOnceWith(['/login']);
    expect(error?.status).toBe(401);
  });

  it('/api/auth/logout 401 → yönlendirir (login ile ön ek eşleşmesi yok)', () => {
    postAndFail('/api/auth/logout', 401, null);

    expect(navigateSpy).toHaveBeenCalledOnceWith(['/login']);
  });

  it('/api/auth/login 503 → yönlendirmez, hatayı iletir', () => {
    const error = postAndFail('/api/auth/login', 503, { message: 'Kimlik servisine ulaşılamıyor.' });

    expect(navigateSpy).not.toHaveBeenCalled();
    expect(error?.status).toBe(503);
  });
});
