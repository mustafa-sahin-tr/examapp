import type { MockedObject } from 'vitest';
import { HttpClient, provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { AuthService } from '../../services/auth.service';
import { authInterceptor } from './auth.interceptor';

/** Issue #255: exclude listesi pathname tam eşleşmesi + aynı-origin kontrolü. */
describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let authServiceSpy: MockedObject<AuthService>;

  beforeEach(() => {
    localStorage.removeItem('auth_token');
    authServiceSpy = {
      isExpiringSoon: vi.fn().mockName('AuthService.isExpiringSoon'),
      refreshToken: vi.fn().mockName('AuthService.refreshToken'),
    } as unknown as MockedObject<AuthService>;
    authServiceSpy.isExpiringSoon.mockReturnValue(false);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withXhr(), withInterceptors([authInterceptor])),
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

  it('intercept_RelativeApiUrlWithToken_AttachesBearerAndCredentials', () => {
    localStorage.setItem('auth_token', 'token-123');

    http.get('/api/school').subscribe();

    const request = httpMock.expectOne('/api/school');
    expect(request.request.headers.get('Authorization')).toBe('Bearer token-123');
    expect(request.request.withCredentials).toBe(true);
    request.flush([]);
  });

  it('intercept_AbsoluteSameOriginUrl_AttachesBearer', () => {
    localStorage.setItem('auth_token', 'token-123');
    const url = `${location.origin}/api/exam/parent/register`;

    http.post(url, {}).subscribe();

    const request = httpMock.expectOne(url);
    expect(request.request.headers.get('Authorization')).toBe('Bearer token-123');
    request.flush({});
  });

  it('intercept_ApiUrlContainingExcludedSubstring_AttachesBearer', () => {
    localStorage.setItem('auth_token', 'token-123');

    http.get('/api/exam/x/api/auth/refresh-token').subscribe();

    const request = httpMock.expectOne('/api/exam/x/api/auth/refresh-token');
    expect(request.request.headers.get('Authorization')).toBe('Bearer token-123');
    request.flush({});
  });

  it('intercept_WithoutToken_DoesNotAttachBearer', () => {
    http.get('/api/grades').subscribe();

    const request = httpMock.expectOne('/api/grades');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush([]);
  });

  for (const path of [
    '/api/auth/refresh-token',
    '/api/auth/login',
    '/api/auth/register',
    '/api/auth/exchange',
    // Issue #440: anonim veli aktivasyon uçları.
    '/api/exam/parent-activation/lookup',
    '/api/exam/parent-activation/activate',
    '/api/exam/parent-activation/resend',
  ]) {
    it(`intercept_ExcludedPath_${path}_SkipsBearerAndRefresh`, () => {
      localStorage.setItem('auth_token', 'stale-token');
      authServiceSpy.isExpiringSoon.mockReturnValue(true);

      http.post(`${path}/?x=1`, {}).subscribe();

      const request = httpMock.expectOne(`${path}/?x=1`);
      expect(request.request.headers.has('Authorization')).toBe(false);
      expect(request.request.withCredentials).toBe(true);
      expect(authServiceSpy.refreshToken).not.toHaveBeenCalled();
      request.flush({});
    });
  }

  it('intercept_ParentActivationAccept_AttachesBearer (#440: oturum açmış veli)', () => {
    localStorage.setItem('auth_token', 'token-123');

    http.post('/api/exam/parent-activation/accept', {}).subscribe();

    const request = httpMock.expectOne('/api/exam/parent-activation/accept');
    expect(request.request.headers.get('Authorization')).toBe('Bearer token-123');
    request.flush({});
  });

  it('intercept_CrossOriginRequest_DoesNotAttachBearerOrCredentials', () => {
    localStorage.setItem('auth_token', 'token-123');
    const url = 'https://storage.example.org/bucket/a.png?X-Amz-Signature=abc';

    http.get(url).subscribe();

    const request = httpMock.expectOne(url);
    expect(request.request.headers.has('Authorization')).toBe(false);
    expect(request.request.withCredentials).toBe(false);
    expect(authServiceSpy.isExpiringSoon).not.toHaveBeenCalled();
    request.flush({});
  });

  it('intercept_TokenExpiringSoon_AttachesRefreshedToken', () => {
    localStorage.setItem('auth_token', 'old-token');
    authServiceSpy.isExpiringSoon.mockReturnValue(true);
    authServiceSpy.refreshToken.mockReturnValue(of('new-token'));

    http.get('/api/school').subscribe();

    const request = httpMock.expectOne('/api/school');
    expect(request.request.headers.get('Authorization')).toBe('Bearer new-token');
    expect(localStorage.getItem('auth_token')).toBe('new-token');
    request.flush([]);
  });
});
