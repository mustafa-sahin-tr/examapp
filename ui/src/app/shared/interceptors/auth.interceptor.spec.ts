import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

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
  it('intercept_ApiUrlContainingExcludedSubstring_AttachesBearerToken', () => {
    // Eski `includes` eşleşmesinde `/terms` ve `/api/auth/refresh-token` alt dizeleri Bearer'ı düşürüyordu.
    localStorage.setItem('auth_token', 'token-123');

    http.get('/api/exam/worksheet/terms').subscribe();
    http.get('/api/exam/faq/api/auth/refresh-token').subscribe();

    const first = httpMock.expectOne('/api/exam/worksheet/terms');
    const second = httpMock.expectOne('/api/exam/faq/api/auth/refresh-token');

    expect(first.request.headers.get('Authorization')).toBe('Bearer token-123');
    expect(second.request.headers.get('Authorization')).toBe('Bearer token-123');
    first.flush({});
    second.flush({});
  });

  it('intercept_ExcludedPathWithQueryAndTrailingSlash_SkipsTokenCheck', () => {
    http.post('/api/auth/refresh-token/?x=1', {}).subscribe();

    const request = httpMock.expectOne('/api/auth/refresh-token/?x=1');

    expect(request.request.headers.has('Authorization')).toBeFalse();
    expect(request.request.withCredentials).toBeTrue();
    expect(authServiceSpy.logout).not.toHaveBeenCalled();
    expect(authServiceSpy.refreshToken).not.toHaveBeenCalled();
    request.flush({});
  });

  it('intercept_ExcludedPathWithSession_DoesNotAttachBearerOrRefresh', () => {
    localStorage.setItem('auth_token', 'token-123');
    authServiceSpy.isExpiringSoon.and.returnValue(true);

    http.post('/api/auth/refresh-token', {}).subscribe();

    const request = httpMock.expectOne('/api/auth/refresh-token');

    expect(request.request.headers.has('Authorization')).toBeFalse();
    expect(authServiceSpy.refreshToken).not.toHaveBeenCalled();
    request.flush({});
  });

  it('intercept_FormerRouterPathAsRequest_IsNoLongerExcluded', () => {
    localStorage.setItem('auth_token', 'token-123');

    http.get('/terms').subscribe();

    const request = httpMock.expectOne('/terms');

    expect(request.request.headers.get('Authorization')).toBe('Bearer token-123');
    request.flush({});
  });

  it('intercept_CrossOriginRequest_DoesNotAttachBearerOrCredentials', () => {
    localStorage.setItem('auth_token', 'token-123');
    const url = 'https://minio.example.org/bucket/file.png?X-Amz-Signature=abc';

    http.get(url).subscribe();

    const request = httpMock.expectOne(url);

    expect(request.request.headers.has('Authorization')).toBeFalse();
    expect(request.request.withCredentials).toBeFalse();
    request.flush({});
  });

  it('intercept_CrossOriginRequestWithoutToken_DoesNotLogOut', () => {
    http.get('https://meet.jit.si/external_api.js').subscribe();

    const request = httpMock.expectOne('https://meet.jit.si/external_api.js');

    expect(authServiceSpy.logout).not.toHaveBeenCalled();
    request.flush({});
  });

  it('intercept_AbsoluteSameOriginUrl_AttachesBearerToken', () => {
    localStorage.setItem('auth_token', 'token-123');
    const url = `${location.origin}/api/exam/dashboard`;

    http.get(url).subscribe();

    const request = httpMock.expectOne(url);

    expect(request.request.headers.get('Authorization')).toBe('Bearer token-123');
    request.flush({});
  });

  it('intercept_RelativeUrlWithoutToken_DoesNotAttachBearer', () => {
    http.get('/api/exam/dashboard').subscribe();

    const request = httpMock.expectOne('/api/exam/dashboard');

    expect(request.request.headers.has('Authorization')).toBeFalse();
    request.flush({});
  });

  it('intercept_TokenExpiringSoon_AttachesRefreshedToken', () => {
    localStorage.setItem('auth_token', 'old-token');
    authServiceSpy.isExpiringSoon.and.returnValue(true);
    authServiceSpy.refreshToken.and.returnValue(of('new-token'));

    http.get('/api/exam/dashboard').subscribe();

    const request = httpMock.expectOne('/api/exam/dashboard');

    expect(request.request.headers.get('Authorization')).toBe('Bearer new-token');
    expect(localStorage.getItem('auth_token')).toBe('new-token');
    request.flush({});
  });

  it('intercept_AnyRequest_DoesNotWriteToConsole', () => {
    const logSpy = spyOn(console, 'log');
    localStorage.setItem('auth_token', 'token-123');

    http.get('/api/exam/dashboard').subscribe();
    httpMock.expectOne('/api/exam/dashboard').flush({});

    expect(logSpy).not.toHaveBeenCalled();
  });
  for (const path of ['/api/auth/login', '/api/auth/exchange']) {
    it(`intercept_PreLoginEndpoint_${path}_WithoutToken_DoesNotLogOut`, () => {
      http.post(path, {}).subscribe();

      const request = httpMock.expectOne(path);

      expect(authServiceSpy.logout).not.toHaveBeenCalled();
      expect(request.request.headers.has('Authorization')).toBeFalse();
      request.flush({});
    });
  }

  it('intercept_AbsoluteSameOriginI18nUrl_SkipsBearerAndLogout', () => {
    localStorage.setItem('auth_token', 'token-123');
    const url = `${location.origin}/i18n/tr.json`;

    http.get(url).subscribe();

    const request = httpMock.expectOne(url);

    expect(request.request.headers.has('Authorization')).toBeFalse();
    expect(request.request.withCredentials).toBeFalse();
    expect(authServiceSpy.logout).not.toHaveBeenCalled();
    request.flush({});
  });
});
