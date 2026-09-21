import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { AuthService } from './auth.service';

/**
 * Helper: base64url encode (JWT format)
 */
function base64urlEncode(str: string): string {
  const utf8 = unescape(encodeURIComponent(str));
  const bytes = [];
  for (let i = 0; i < utf8.length; i++) {
    bytes.push(utf8.charCodeAt(i));
  }
  return btoa(String.fromCharCode(...bytes))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=/g, '');
}

/**
 * Helper: Create a fake JWT token with given sub claim
 * Signature is fake but jwtDecode will parse it successfully.
 */
function createToken(keycloakId: string): string {
  const header = { alg: 'RS256', typ: 'JWT' };
  const now = Math.floor(Date.now() / 1000);
  const payload = {
    sub: keycloakId,
    exp: now + 3600, // 1 hour from now
    iat: now,
  };
  const headerB64 = base64urlEncode(JSON.stringify(header));
  const payloadB64 = base64urlEncode(JSON.stringify(payload));
  const signature = base64urlEncode('fake-signature');
  return `${headerB64}.${payloadB64}.${signature}`;
}

describe('AuthService (ui)', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;
  let routerSpy: jasmine.Spy;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [AuthService],
    });

    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
    routerSpy = spyOn(service, 'goLogin');

    // Clear localStorage before each test
    localStorage.clear();
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  describe('isCachedUserCurrent', () => {
    describe('Kriter 1: Kullanıcı değişince profil bilgisi güncellenmeli', () => {
      it('isCachedUserCurrent_TokenAndUserMatch_ReturnsTrue', () => {
        const keycloakId = 'user-123';
        const token = createToken(keycloakId);
        const user = { keycloakId, fullName: 'Alice', email: 'alice@example.com' };

        localStorage.setItem('auth_token', token);
        localStorage.setItem('user', JSON.stringify(user));

        expect(service.isCachedUserCurrent()).toBe(true);
      });

      it('isCachedUserCurrent_TokenSubDoesNotMatchUserKeycloakId_ReturnsFalse', () => {
        const token = createToken('user-123');
        const user = { keycloakId: 'user-456', fullName: 'Bob', email: 'bob@example.com' };

        localStorage.setItem('auth_token', token);
        localStorage.setItem('user', JSON.stringify(user));

        expect(service.isCachedUserCurrent()).toBe(false);
      });
    });

    it('isCachedUserCurrent_NoToken_ReturnsFalse', () => {
      const user = { keycloakId: 'user-123', fullName: 'Alice', email: 'alice@example.com' };
      localStorage.setItem('user', JSON.stringify(user));

      expect(service.isCachedUserCurrent()).toBe(false);
    });

    it('isCachedUserCurrent_NoUser_ReturnsFalse', () => {
      const token = createToken('user-123');
      localStorage.setItem('auth_token', token);

      expect(service.isCachedUserCurrent()).toBe(false);
    });

    it('isCachedUserCurrent_UserJsonMalformed_ReturnsFalse', () => {
      const token = createToken('user-123');
      localStorage.setItem('auth_token', token);
      localStorage.setItem('user', 'not-valid-json');

      expect(service.isCachedUserCurrent()).toBe(false);
    });

    it('isCachedUserCurrent_TokenDecodeFails_ReturnsFalse', () => {
      localStorage.setItem('auth_token', 'invalid.token.here');
      localStorage.setItem('user', JSON.stringify({ keycloakId: 'user-123' }));

      expect(service.isCachedUserCurrent()).toBe(false);
    });

    it('isCachedUserCurrent_SubIsEmpty_ReturnsFalse', () => {
      const header = { alg: 'RS256', typ: 'JWT' };
      const now = Math.floor(Date.now() / 1000);
      const payload = { sub: '', exp: now + 3600 };
      const token =
        base64urlEncode(JSON.stringify(header)) +
        '.' +
        base64urlEncode(JSON.stringify(payload)) +
        '.sig';

      localStorage.setItem('auth_token', token);
      localStorage.setItem('user', JSON.stringify({ keycloakId: 'user-123' }));

      expect(service.isCachedUserCurrent()).toBe(false);
    });

    it('isCachedUserCurrent_KeycloakIdIsEmpty_ReturnsFalse', () => {
      const token = createToken('user-123');
      const user = { keycloakId: '', fullName: 'Alice' };

      localStorage.setItem('auth_token', token);
      localStorage.setItem('user', JSON.stringify(user));

      expect(service.isCachedUserCurrent()).toBe(false);
    });
  });

  describe('clearCachedUser', () => {
    it('clearCachedUser_RemovesUserAndAvatar_KeepsTokenAndRole', () => {
      localStorage.setItem('auth_token', 'token-123');
      localStorage.setItem('user_role', 'student');
      localStorage.setItem('user_avatar', 'avatar-url');
      localStorage.setItem('user', JSON.stringify({ keycloakId: 'user-123' }));
      localStorage.setItem('student', JSON.stringify({ id: 1 }));

      service.clearCachedUser();

      expect(localStorage.getItem('auth_token')).toBe('token-123');
      expect(localStorage.getItem('user_role')).toBe('student');
      expect(localStorage.getItem('user')).toBeNull();
      expect(localStorage.getItem('user_avatar')).toBeNull();
      expect(localStorage.getItem('student')).toBeNull();
    });

    it('clearCachedUser_DoesNotNavigate', () => {
      localStorage.setItem('user', JSON.stringify({ keycloakId: 'user-123' }));
      localStorage.setItem('user_avatar', 'avatar-url');

      service.clearCachedUser();

      expect(routerSpy).not.toHaveBeenCalled();
    });
  });

  describe('logout', () => {
    describe('Kriter 2: Logout HTTP hatasında bile temizlenmeli', () => {
      it('logout_HttpErrorResponse_ClearsLocalStorageSynchronously', () => {
        localStorage.setItem('auth_token', 'token-123');
        localStorage.setItem('user_role', 'student');
        localStorage.setItem('user_avatar', 'avatar-url');
        localStorage.setItem('user', JSON.stringify({ keycloakId: 'user-123' }));
        localStorage.setItem('student', JSON.stringify({ id: 1 }));

        service.logout();

        // Senkron olarak silinmişti
        expect(localStorage.getItem('auth_token')).toBeNull();
        expect(localStorage.getItem('user_role')).toBeNull();
        expect(localStorage.getItem('user_avatar')).toBeNull();
        expect(localStorage.getItem('user')).toBeNull();
        expect(localStorage.getItem('student')).toBeNull();

        // HTTP isteğe hata döndür
        const req = httpMock.expectOne('/api/exam/auth/logout');
        expect(req.request.headers.get('Authorization')).toBe('Bearer token-123');
        req.error(new ProgressEvent('Network error'), { status: 500 });

        // goLogin bir kez çağrılmalı
        expect(routerSpy).toHaveBeenCalledTimes(1);
      });
    });

    it('logout_HttpSuccess_ClearsLocalStorageAndCallsGoLogin', () => {
      localStorage.setItem('auth_token', 'token-123');
      localStorage.setItem('user_role', 'student');
      localStorage.setItem('user_avatar', 'avatar-url');
      localStorage.setItem('user', JSON.stringify({ keycloakId: 'user-123' }));
      localStorage.setItem('student', JSON.stringify({ id: 1 }));

      service.logout();

      expect(localStorage.getItem('auth_token')).toBeNull();
      expect(localStorage.getItem('user_role')).toBeNull();
      expect(localStorage.getItem('user_avatar')).toBeNull();
      expect(localStorage.getItem('user')).toBeNull();
      expect(localStorage.getItem('student')).toBeNull();

      const req = httpMock.expectOne('/api/exam/auth/logout');
      expect(req.request.headers.get('Authorization')).toBe('Bearer token-123');
      req.flush(null);

      expect(routerSpy).toHaveBeenCalledTimes(1);
    });

    it('logout_HttpTimeout_ClearsLocalStorageAndCallsGoLogin', fakeAsync(() => {
      localStorage.setItem('auth_token', 'token-123');
      localStorage.setItem('user', JSON.stringify({ keycloakId: 'user-123' }));

      service.logout();

      expect(localStorage.getItem('auth_token')).toBeNull();

      const req = httpMock.expectOne('/api/exam/auth/logout');
      expect(req.request.headers.get('Authorization')).toBe('Bearer token-123');

      // Timeout işlemini tetikle (2000ms)
      tick(2000);

      // goLogin çağrılması beklenir
      expect(routerSpy).toHaveBeenCalledTimes(1);
    }));
  });

  describe('Kriter 3: Yeni kayıt sonrası kendi adını görür', () => {
    it('isCachedUserCurrent_AfterSuccessfulLogin_ReturnsTrueForSameUser', () => {
      const keycloakId = 'user-999';
      const token = createToken(keycloakId);
      const profile = {
        keycloakId,
        fullName: 'Charlie',
        email: 'charlie@example.com',
        avatar: 'avatar-url',
      };

      // Login flow
      localStorage.setItem('auth_token', token);
      localStorage.setItem('user_role', 'student');
      localStorage.setItem('user_avatar', profile.avatar);
      localStorage.setItem('user', JSON.stringify(profile));

      expect(service.isCachedUserCurrent()).toBe(true);
      expect(service.getUser()).toEqual(profile);
    });
  });
});
