import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { catchError, map, shareReplay } from 'rxjs/operators';

import { AuthService, TokenResponse, UserProfile } from '../../services/auth.service';
import { authErrorInterceptor, isRefreshExcluded, isCrossOrigin } from './auth-error.interceptor';
import { HttpRequest } from '@angular/common/http';

describe('authErrorInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let authServiceSpy: jasmine.SpyObj<AuthService>;

  // Test token examples (not real credentials)
  const newToken = 'example.jwt.token';
  const newToken2 = 'example.jwt.token2';
  const newToken3 = 'example.jwt.token3';
  const newToken4 = 'example.jwt.token4';
  const newToken5 = 'example.jwt.token5';
  const newToken6 = 'example.jwt.token6';
  const newToken7 = 'example.jwt.token7';

  beforeEach(() => {
    localStorage.removeItem('auth_token');
    localStorage.removeItem('user');

    authServiceSpy = jasmine.createSpyObj<AuthService>('AuthService', [
      'refreshToken',
      'clearLocalStorage',
    ]);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authErrorInterceptor])),
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
    localStorage.removeItem('user');
  });

  describe('isRefreshExcluded', () => {
    it('isRefreshExcluded_RefreshTokenPath_ReturnsTrue', () => {
      const req = new HttpRequest('POST' as any, '/api/auth/refresh-token');
      expect(isRefreshExcluded(req)).toBeTrue();
    });

    it('isRefreshExcluded_LoginPath_ReturnsTrue', () => {
      const req = new HttpRequest('POST' as any, '/api/auth/login');
      expect(isRefreshExcluded(req)).toBeTrue();
    });

    it('isRefreshExcluded_ExchangePath_ReturnsTrue', () => {
      const req = new HttpRequest('POST' as any, '/api/auth/exchange');
      expect(isRefreshExcluded(req)).toBeTrue();
    });

    it('isRefreshExcluded_LogoutPath_ReturnsTrue', () => {
      const req = new HttpRequest('POST' as any, '/api/exam/auth/logout');
      expect(isRefreshExcluded(req)).toBeTrue();
    });

    it('isRefreshExcluded_AuthUiLogoutPath_ReturnsFalse', () => {
      const req = new HttpRequest('GET', '/app/logout');
      expect(isRefreshExcluded(req)).toBeFalse();
    });

    it('isRefreshExcluded_AuthUiExchangePath_ReturnsFalse', () => {
      const req = new HttpRequest('GET', '/app/exchange');
      expect(isRefreshExcluded(req)).toBeFalse();
    });

    it('isRefreshExcluded_AuthUiRefreshTokenPath_ReturnsFalse', () => {
      const req = new HttpRequest('GET', '/app/refresh-token');
      expect(isRefreshExcluded(req)).toBeFalse();
    });

    it('isRefreshExcluded_NormalApiPath_ReturnsFalse', () => {
      const req = new HttpRequest('GET', '/api/exam/dashboard');
      expect(isRefreshExcluded(req)).toBeFalse();
    });

    it('isRefreshExcluded_AbsoluteUrl_MatchesByPathname', () => {
      const req = new HttpRequest('POST' as any, 'http://localhost:5678/api/auth/login');
      expect(isRefreshExcluded(req)).toBeTrue();
    });

    it('isRefreshExcluded_UrlWithQueryString_IgnoresQuery', () => {
      const req = new HttpRequest('POST' as any, '/api/auth/refresh-token?x=1');
      expect(isRefreshExcluded(req)).toBeTrue();
    });

    it('isRefreshExcluded_UrlWithHash_IgnoresHash', () => {
      const req = new HttpRequest('POST' as any, '/api/auth/login#section');
      expect(isRefreshExcluded(req)).toBeTrue();
    });

    it('isRefreshExcluded_TrailingSlash_StripsBothAndMatches', () => {
      const req = new HttpRequest('POST' as any, '/api/auth/login/');
      expect(isRefreshExcluded(req)).toBeTrue();
    });

    it('isRefreshExcluded_MultipleTrailingSlashes_StripsBothAndMatches', () => {
      const req = new HttpRequest('POST' as any, '/api/auth/refresh-token///');
      expect(isRefreshExcluded(req)).toBeTrue();
    });

    it('isRefreshExcluded_RootPath_ReturnsFalse', () => {
      const req = new HttpRequest('GET', '/');
      expect(isRefreshExcluded(req)).toBeFalse();
    });

    it('isRefreshExcluded_PathWithQueryAndTrailingSlash_MatchesCleanly', () => {
      const req = new HttpRequest('POST' as any, '/api/auth/exchange?code=abc#');
      expect(isRefreshExcluded(req)).toBeTrue();
    });
  });

  describe('isCrossOrigin', () => {
    it('isCrossOrigin_RelativeUrl_ReturnsFalse', () => {
      const req = new HttpRequest('GET', '/api/exam/dashboard');
      expect(isCrossOrigin(req)).toBeFalse();
    });

    it('isCrossOrigin_SameOriginAbsoluteUrl_ReturnsFalse', () => {
      const currentOrigin = location.origin;
      const req = new HttpRequest('GET', `${currentOrigin}/api/exam/dashboard`);
      expect(isCrossOrigin(req)).toBeFalse();
    });

    it('isCrossOrigin_DifferentOriginUrl_ReturnsTrue', () => {
      const req = new HttpRequest('GET', 'https://evil.example.com/api/data');
      expect(isCrossOrigin(req)).toBeTrue();
    });

    it('isCrossOrigin_DifferentPort_ReturnsTrue', () => {
      const currentHost = location.hostname;
      const differentPort = location.port === '80' ? '8080' : '80';
      const req = new HttpRequest('GET', `http://${currentHost}:${differentPort}/api/data`);
      expect(isCrossOrigin(req)).toBeTrue();
    });
  });

  describe('401 error handling on normal (non-excluded) endpoints', () => {
    it('ErrorResponse401_OnNormalEndpoint_RefreshesTokenAndRetriesRequest', (done) => {
      // Mock refreshToken to make the actual HTTP call
      authServiceSpy.refreshToken.and.callFake(() =>
        http.post<{ accessToken: string }>('/api/auth/refresh-token', {})
          .pipe(
            map(res => res.accessToken)
          )
      );

      http.get('/api/exam/dashboard').subscribe({
        next: (response) => {
          expect(response).toEqual({ data: 'success' });
          expect(authServiceSpy.refreshToken).toHaveBeenCalledTimes(1);
          done();
        },
        error: () => fail('should not error'),
      });

      const firstRequest = httpMock.expectOne('/api/exam/dashboard');
      expect(firstRequest.request.headers.has('Authorization')).toBeFalse();
      firstRequest.flush(null, { status: 401, statusText: 'Unauthorized' });

      const refreshRequest = httpMock.expectOne('/api/auth/refresh-token');
      const payload = { accessToken: newToken }; // example test data
      refreshRequest.flush(payload);

      const retryRequest = httpMock.expectOne('/api/exam/dashboard');
      expect(retryRequest.request.headers.get('Authorization')).toBe(`Bearer ${newToken}`);
      retryRequest.flush({ data: 'success' });
    });
  });

  describe('401 on excluded endpoints (no refresh)', () => {
    it('ErrorResponse401_OnRefreshTokenEndpoint_ThrowsWithoutRetry', (done) => {
      const error401 = { status: 401, statusText: 'Unauthorized' };

      http.post('/api/auth/refresh-token', {}).subscribe({
        next: () => fail('should error'),
        error: (err) => {
          expect(err.status).toBe(401);
          expect(authServiceSpy.refreshToken).not.toHaveBeenCalled();
          done();
        },
      });

      const req = httpMock.expectOne('/api/auth/refresh-token');
      req.flush(null, error401);
    });

    it('ErrorResponse401_OnLoginEndpoint_ThrowsWithoutRetry', (done) => {
      http.post('/api/auth/login', { username: 'user' }).subscribe({
        next: () => fail('should error'),
        error: (err) => {
          expect(err.status).toBe(401);
          expect(authServiceSpy.refreshToken).not.toHaveBeenCalled();
          done();
        },
      });

      const req = httpMock.expectOne('/api/auth/login');
      req.flush(null, { status: 401, statusText: 'Unauthorized' });
    });

    it('ErrorResponse401_OnExchangeEndpoint_ThrowsWithoutRetry', (done) => {
      http.post('/api/auth/exchange', { code: 'abc' }).subscribe({
        next: () => fail('should error'),
        error: (err) => {
          expect(err.status).toBe(401);
          expect(authServiceSpy.refreshToken).not.toHaveBeenCalled();
          done();
        },
      });

      const req = httpMock.expectOne('/api/auth/exchange');
      req.flush(null, { status: 401, statusText: 'Unauthorized' });
    });

    it('ErrorResponse401_OnLogoutEndpoint_ThrowsWithoutRetry', (done) => {
      http.post('/api/exam/auth/logout', {}).subscribe({
        next: () => fail('should error'),
        error: (err) => {
          expect(err.status).toBe(401);
          expect(authServiceSpy.refreshToken).not.toHaveBeenCalled();
          done();
        },
      });

      const req = httpMock.expectOne('/api/exam/auth/logout');
      req.flush(null, { status: 401, statusText: 'Unauthorized' });
    });
  });

  describe('401 on cross-origin requests (no refresh)', () => {
    it('ErrorResponse401_OnCrossOriginEndpoint_ThrowsWithoutRefresh', (done) => {
      http.get('https://evil.example.com/api/data').subscribe({
        next: () => fail('should error'),
        error: (err) => {
          expect(err.status).toBe(401);
          expect(authServiceSpy.refreshToken).not.toHaveBeenCalled();
          done();
        },
      });

      const req = httpMock.expectOne('https://evil.example.com/api/data');
      req.flush(null, { status: 401, statusText: 'Unauthorized' });
    });
  });

  describe('refresh token success and retry', () => {
    it('RefreshSuccess_RetriedRequestSucceeds_ReturnsRetryResponseToSubscriber', (done) => {
      authServiceSpy.refreshToken.and.callFake(() =>
        http.post<{ accessToken: string }>('/api/auth/refresh-token', {})
          .pipe(map(res => res.accessToken))
      );

      const expectedData = { id: 1, name: 'Test' };
      http.get('/api/exam/dashboard').subscribe({
        next: (response) => {
          expect(response).toEqual(expectedData);
          done();
        },
        error: () => fail('should not error'),
      });

      const firstReq = httpMock.expectOne('/api/exam/dashboard');
      firstReq.flush(null, { status: 401, statusText: 'Unauthorized' });

      const refreshReq = httpMock.expectOne('/api/auth/refresh-token');
      const refreshPayload = { accessToken: newToken2 }; // example test data
      refreshReq.flush(refreshPayload);

      const retryReq = httpMock.expectOne('/api/exam/dashboard');
      expect(retryReq.request.headers.get('Authorization')).toBe(`Bearer ${newToken2}`);
      retryReq.flush(expectedData);
    });

    it('RefreshSuccess_StoresToLocalStorage', (done) => {
      authServiceSpy.refreshToken.and.callFake(() =>
        http.post<{ accessToken: string }>('/api/auth/refresh-token', {})
          .pipe(map(res => res.accessToken))
      );

      http.get('/api/exam/dashboard').subscribe({
        next: () => {
          expect(localStorage.getItem('auth_token')).toBe(newToken3);
          done();
        },
        error: () => fail('should not error'),
      });

      const firstReq = httpMock.expectOne('/api/exam/dashboard');
      firstReq.flush(null, { status: 401, statusText: 'Unauthorized' });

      const refreshReq = httpMock.expectOne('/api/auth/refresh-token');
      const refreshPayload = { accessToken: newToken3 }; // example test data
      refreshReq.flush(refreshPayload);

      const retryReq = httpMock.expectOne('/api/exam/dashboard');
      retryReq.flush({ data: 'ok' });
    });
  });

  describe('refresh token failure', () => {
    it('RefreshFails_ThrowsOriginal401Error', (done) => {
      authServiceSpy.refreshToken.and.callFake(() =>
        http.post<{ accessToken: string }>('/api/auth/refresh-token', {})
          .pipe(
            map(res => res.accessToken),
            catchError(() => {
              authServiceSpy.clearLocalStorage();
              return throwError(() => new Error('Refresh failed'));
            })
          )
      );

      http.get('/api/exam/dashboard').subscribe({
        next: () => fail('should error'),
        error: (err) => {
          expect(err.status).toBe(401);
          expect(authServiceSpy.clearLocalStorage).toHaveBeenCalledTimes(1);
          done();
        },
      });

      const firstReq = httpMock.expectOne('/api/exam/dashboard');
      firstReq.flush(null, { status: 401, statusText: 'Unauthorized' });

      const refreshReq = httpMock.expectOne('/api/auth/refresh-token');
      refreshReq.error(new ProgressEvent('error'), { status: 401 });
    });

    it('RefreshFails_ClearsSessionAndCallsGoLogin', (done) => {
      authServiceSpy.refreshToken.and.callFake(() =>
        http.post<{ accessToken: string }>('/api/auth/refresh-token', {})
          .pipe(
            map(res => res.accessToken),
            catchError(() => {
              authServiceSpy.clearLocalStorage();
              return throwError(() => new Error('Refresh failed'));
            })
          )
      );

      http.get('/api/exam/dashboard').subscribe({
        next: () => fail('should error'),
        error: () => {
          expect(authServiceSpy.clearLocalStorage).toHaveBeenCalledTimes(1);
          done();
        },
      });

      const firstReq = httpMock.expectOne('/api/exam/dashboard');
      firstReq.flush(null, { status: 401, statusText: 'Unauthorized' });

      const refreshReq = httpMock.expectOne('/api/auth/refresh-token');
      refreshReq.error(new ProgressEvent('error'));
    });
  });

  describe('retry request error handling', () => {
    it('RetryRequest403_DoesNotClearSession', (done) => {
      authServiceSpy.refreshToken.and.callFake(() =>
        http.post<{ accessToken: string }>('/api/auth/refresh-token', {})
          .pipe(map(res => res.accessToken))
      );

      http.get('/api/exam/dashboard').subscribe({
        next: () => fail('should error'),
        error: (err) => {
          expect(err.status).toBe(403);
          expect(authServiceSpy.clearLocalStorage).not.toHaveBeenCalled();
          done();
        },
      });

      const firstReq = httpMock.expectOne('/api/exam/dashboard');
      firstReq.flush(null, { status: 401, statusText: 'Unauthorized' });

      const refreshReq = httpMock.expectOne('/api/auth/refresh-token');
      const refreshPayload = { accessToken: newToken4 }; // example test data
      refreshReq.flush(refreshPayload);

      const retryReq = httpMock.expectOne('/api/exam/dashboard');
      retryReq.flush(null, { status: 403, statusText: 'Forbidden' });
    });

    it('RetryRequest500_DoesNotClearSession', (done) => {
      authServiceSpy.refreshToken.and.callFake(() =>
        http.post<{ accessToken: string }>('/api/auth/refresh-token', {})
          .pipe(map(res => res.accessToken))
      );

      http.get('/api/exam/dashboard').subscribe({
        next: () => fail('should error'),
        error: (err) => {
          expect(err.status).toBe(500);
          expect(authServiceSpy.clearLocalStorage).not.toHaveBeenCalled();
          done();
        },
      });

      const firstReq = httpMock.expectOne('/api/exam/dashboard');
      firstReq.flush(null, { status: 401, statusText: 'Unauthorized' });

      const refreshReq = httpMock.expectOne('/api/auth/refresh-token');
      const refreshPayload = { accessToken: newToken5 }; // example test data
      refreshReq.flush(refreshPayload);

      const retryReq = httpMock.expectOne('/api/exam/dashboard');
      retryReq.flush(null, { status: 500, statusText: 'Internal Server Error' });
    });
  });

  describe('concurrent 401 errors', () => {
    it('TwoConcurrent401_OnSameEndpoint_OnlyRefreshesOnce', (done) => {
      // Mock refreshToken with single-flight behavior using shareReplay
      let refreshObservable: any = null;
      authServiceSpy.refreshToken.and.callFake(() => {
        if (!refreshObservable) {
          refreshObservable = http.post<{ accessToken: string }>('/api/auth/refresh-token', {})
            .pipe(
              map(res => res.accessToken),
              catchError(() => {
                authServiceSpy.clearLocalStorage();
                refreshObservable = null;
                return throwError(() => new Error('Refresh failed'));
              }),
              shareReplay({ bufferSize: 1, refCount: false })
            );
        }
        return refreshObservable;
      });

      let responseCount = 0;

      http.get('/api/exam/dashboard').subscribe({
        next: () => {
          responseCount++;
          if (responseCount === 2) {
            expect(authServiceSpy.refreshToken).toHaveBeenCalled();
            done();
          }
        },
        error: () => fail('should not error'),
      });

      http.get('/api/exam/dashboard').subscribe({
        next: () => {
          responseCount++;
          if (responseCount === 2) {
            expect(authServiceSpy.refreshToken).toHaveBeenCalled();
            done();
          }
        },
        error: () => fail('should not error'),
      });

      const firstRequests = httpMock.match('/api/exam/dashboard');
      expect(firstRequests.length).toBe(2);
      firstRequests[0].flush(null, { status: 401, statusText: 'Unauthorized' });
      firstRequests[1].flush(null, { status: 401, statusText: 'Unauthorized' });

      // With single-flight pattern, only one refresh request should be made
      const refreshRequest = httpMock.expectOne('/api/auth/refresh-token');
      const refreshPayload = { accessToken: newToken6 }; // example test data
      refreshRequest.flush(refreshPayload);

      const retryRequests = httpMock.match('/api/exam/dashboard');
      expect(retryRequests.length).toBe(2);
      retryRequests[0].flush({ data: 'ok' });
      retryRequests[1].flush({ data: 'ok' });
    });
  });

  describe('non-401 error handling', () => {
    it('Error400_PassesThroughWithoutRefresh', (done) => {
      http.get('/api/exam/dashboard').subscribe({
        next: () => fail('should error'),
        error: (err) => {
          expect(err.status).toBe(400);
          expect(authServiceSpy.refreshToken).not.toHaveBeenCalled();
          done();
        },
      });

      const req = httpMock.expectOne('/api/exam/dashboard');
      req.flush(null, { status: 400, statusText: 'Bad Request' });
    });

    it('Error403_PassesThroughWithoutRefresh', (done) => {
      http.get('/api/exam/dashboard').subscribe({
        next: () => fail('should error'),
        error: (err) => {
          expect(err.status).toBe(403);
          expect(authServiceSpy.refreshToken).not.toHaveBeenCalled();
          done();
        },
      });

      const req = httpMock.expectOne('/api/exam/dashboard');
      req.flush(null, { status: 403, statusText: 'Forbidden' });
    });

    it('Error404_PassesThroughWithoutRefresh', (done) => {
      http.get('/api/exam/dashboard').subscribe({
        next: () => fail('should error'),
        error: (err) => {
          expect(err.status).toBe(404);
          expect(authServiceSpy.refreshToken).not.toHaveBeenCalled();
          done();
        },
      });

      const req = httpMock.expectOne('/api/exam/dashboard');
      req.flush(null, { status: 404, statusText: 'Not Found' });
    });

    it('Error500_PassesThroughWithoutRefresh', (done) => {
      http.get('/api/exam/dashboard').subscribe({
        next: () => fail('should error'),
        error: (err) => {
          expect(err.status).toBe(500);
          expect(authServiceSpy.refreshToken).not.toHaveBeenCalled();
          done();
        },
      });

      const req = httpMock.expectOne('/api/exam/dashboard');
      req.flush(null, { status: 500, statusText: 'Internal Server Error' });
    });
  });


  describe('retry request 401 handling', () => {
    it('RetryRequest_401FromRetry_ClearsSessionAndThrows', (done) => {
      authServiceSpy.refreshToken.and.callFake(() =>
        http.post<{ accessToken: string }>('/api/auth/refresh-token', {})
          .pipe(map(res => res.accessToken))
      );

      http.get('/api/exam/dashboard').subscribe({
        next: () => fail('should error'),
        error: (err) => {
          expect(err.status).toBe(401);
          expect(authServiceSpy.refreshToken).toHaveBeenCalledTimes(1);
          expect(authServiceSpy.clearLocalStorage).toHaveBeenCalledTimes(1);
          done();
        },
      });

      const firstReq = httpMock.expectOne('/api/exam/dashboard');
      firstReq.flush(null, { status: 401, statusText: 'Unauthorized' });

      const refreshReq = httpMock.expectOne('/api/auth/refresh-token');
      const refreshPayload = { accessToken: newToken7 }; // example test data
      refreshReq.flush(refreshPayload);

      const retryReq = httpMock.expectOne('/api/exam/dashboard');
      retryReq.flush(null, { status: 401, statusText: 'Unauthorized' });
    });

    it('RetryRequest_403FromRetry_DoesNotClearSession', (done) => {
      authServiceSpy.refreshToken.and.callFake(() =>
        http.post<{ accessToken: string }>('/api/auth/refresh-token', {})
          .pipe(map(res => res.accessToken))
      );

      http.get('/api/exam/dashboard').subscribe({
        next: () => fail('should error'),
        error: (err) => {
          expect(err.status).toBe(403);
          expect(authServiceSpy.clearLocalStorage).not.toHaveBeenCalled();
          done();
        },
      });

      const firstReq = httpMock.expectOne('/api/exam/dashboard');
      firstReq.flush(null, { status: 401, statusText: 'Unauthorized' });

      const refreshReq = httpMock.expectOne('/api/auth/refresh-token');
      const refreshPayload = { accessToken: newToken7 }; // example test data
      refreshReq.flush(refreshPayload);

      const retryReq = httpMock.expectOne('/api/exam/dashboard');
      retryReq.flush(null, { status: 403, statusText: 'Forbidden' });
    });
  });
});
