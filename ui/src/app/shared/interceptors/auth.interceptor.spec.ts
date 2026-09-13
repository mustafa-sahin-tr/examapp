import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AuthService } from '../../services/auth.service';
import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let authServiceSpy: jasmine.SpyObj<AuthService>;

  beforeEach(() => {
    localStorage.removeItem('auth_token');
    authServiceSpy = jasmine.createSpyObj<AuthService>('AuthService', [
      'logout',
      'isExpiringSoon',
      'refreshToken',
    ]);
    authServiceSpy.isExpiringSoon.and.returnValue(false);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: authServiceSpy },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.removeItem('auth_token');
  });

  it('intercept_AnonymousI18nRequest_DoesNotTriggerLogout', () => {
    http.get('/i18n/tr.json').subscribe();

    const request = httpMock.expectOne('/i18n/tr.json');

    expect(authServiceSpy.logout).not.toHaveBeenCalled();
    expect(request.request.headers.has('Authorization')).toBeFalse();
    expect(request.request.withCredentials).toBeFalse();
    request.flush({});
  });

  it('intercept_I18nRequestWithSession_DoesNotAttachBearerToken', () => {
    localStorage.setItem('auth_token', 'token-123');

    http.get('/i18n/en.json').subscribe();

    const request = httpMock.expectOne('/i18n/en.json');

    expect(request.request.headers.has('Authorization')).toBeFalse();
    request.flush({});
  });

  it('intercept_ApiRequestWithoutToken_LogsOut', () => {
    http.get('/api/exam/dashboard').subscribe();

    const request = httpMock.expectOne('/api/exam/dashboard');

    expect(authServiceSpy.logout).toHaveBeenCalledTimes(1);
    request.flush({});
  });

  it('intercept_ApiRequestWithValidToken_AttachesBearerToken', () => {
    localStorage.setItem('auth_token', 'token-123');

    http.get('/api/exam/dashboard').subscribe();

    const request = httpMock.expectOne('/api/exam/dashboard');

    expect(request.request.headers.get('Authorization')).toBe('Bearer token-123');
    expect(authServiceSpy.logout).not.toHaveBeenCalled();
    request.flush({});
  });
});
