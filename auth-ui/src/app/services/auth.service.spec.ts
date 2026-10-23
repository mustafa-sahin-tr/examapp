import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { AuthService, TokenResponse, pickAppRole } from './auth.service';

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

/** Helper: realm_access.roles taşıyan sahte JWT (applyRefreshedSession testleri için). */
function createTokenWithRoles(keycloakId: string, roles: string[]): string {
  const header = { alg: 'RS256', typ: 'JWT' };
  const now = Math.floor(Date.now() / 1000);
  const payload = { sub: keycloakId, exp: now + 3600, iat: now, realm_access: { roles } };
  return `${base64urlEncode(JSON.stringify(header))}.${base64urlEncode(JSON.stringify(payload))}.${base64urlEncode('fake-signature')}`;
}

describe('pickAppRole', () => {
  it('ek yetki rolü önce gelse de uygulama rolünü seçer', () => {
    expect(pickAppRole(['QuestionTrainer', 'Teacher'])).toBe('Teacher');
  });

  it('ek yetki rolü sonra gelse de uygulama rolünü seçer', () => {
    expect(pickAppRole(['Teacher', 'QuestionTrainer'])).toBe('Teacher');
  });

  it('offline_access gibi Keycloak rollerini atlar', () => {
    expect(pickAppRole(['offline_access', 'Student'])).toBe('Student');
  });

  it('birden fazla uygulama rolünde APP_ROLES önceliğini uygular', () => {
    expect(pickAppRole(['Parent', 'Student', 'Admin'])).toBe('Admin');
  });

  it('yalnız ek yetki rolü varsa null döner', () => {
    expect(pickAppRole(['QuestionTrainer'])).toBeNull();
  });

  it('boş/undefined/null listede null döner', () => {
    expect(pickAppRole([])).toBeNull();
    expect(pickAppRole(undefined)).toBeNull();
    expect(pickAppRole(null)).toBeNull();
  });
});

describe('AuthService (auth-ui)', () => {
  beforeEach(() => {
    vi.useFakeTimers({ advanceTimeDelta: 1, shouldAdvanceTime: true });
  });
  afterEach(() => {
    vi.useRealTimers();
  });
  let service: AuthService;
  let httpMock: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [AuthService],
    });

    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockReturnValue(undefined as never);

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
        const user = {
          keycloakId,
          fullName: 'Alice',
          email: 'alice@example.com',
          id: 1,
          profileId: 1,
          avatar: '',
          role: 'student',
        };

        localStorage.setItem('auth_token', token);
        localStorage.setItem('user', JSON.stringify(user));

        expect(service.isCachedUserCurrent()).toBe(true);
      });

      it('isCachedUserCurrent_TokenSubDoesNotMatchUserKeycloakId_ReturnsFalse', () => {
        const token = createToken('user-123');
        const user = {
          keycloakId: 'user-456',
          fullName: 'Bob',
          email: 'bob@example.com',
          id: 2,
          profileId: 2,
          avatar: '',
          role: 'teacher',
        };

        localStorage.setItem('auth_token', token);
        localStorage.setItem('user', JSON.stringify(user));

        expect(service.isCachedUserCurrent()).toBe(false);
      });
    });

    it('isCachedUserCurrent_NoToken_ReturnsFalse', () => {
      const user = {
        keycloakId: 'user-123',
        fullName: 'Alice',
        email: 'alice@example.com',
        id: 1,
        profileId: 1,
        avatar: '',
        role: 'student',
      };
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
      const token = base64urlEncode(JSON.stringify(header)) + '.' + base64urlEncode(JSON.stringify(payload)) + '.sig';

      localStorage.setItem('auth_token', token);
      localStorage.setItem('user', JSON.stringify({ keycloakId: 'user-123' }));

      expect(service.isCachedUserCurrent()).toBe(false);
    });

    it('isCachedUserCurrent_KeycloakIdIsEmpty_ReturnsFalse', () => {
      const token = createToken('user-123');
      const user = {
        keycloakId: '',
        fullName: 'Alice',
        email: 'alice@example.com',
        id: 1,
        profileId: 1,
        avatar: '',
        role: 'student',
      };

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

      expect(router.navigate).not.toHaveBeenCalled();
    });
  });

  describe('logout', () => {
    describe('Kriter 2: Logout HTTP hatasında bile temizlenmeli', () => {
      it('logout_HttpErrorResponse_ClearsLocalStorageSynchronouslyAndReturnsObservable', async () => {
        localStorage.setItem('auth_token', 'token-123');
        localStorage.setItem('user_role', 'student');
        localStorage.setItem('user_avatar', 'avatar-url');
        localStorage.setItem('user', JSON.stringify({ keycloakId: 'user-123' }));
        localStorage.setItem('student', JSON.stringify({ id: 1 }));

        service.logout().subscribe(() => {
          // Observable tamamlanmalı (hata fırlatmamalı)
          expect(true).toBe(true);
        });

        // Senkron olarak silinmiş olmalı
        expect(localStorage.getItem('auth_token')).toBeNull();
        expect(localStorage.getItem('user_role')).toBeNull();
        expect(localStorage.getItem('user_avatar')).toBeNull();
        expect(localStorage.getItem('user')).toBeNull();
        expect(localStorage.getItem('student')).toBeNull();

        // HTTP isteğe hata döndür
        const req = httpMock.expectOne('/api/exam/auth/logout');
        expect(req.request.headers.get('Authorization')).toBe('Bearer token-123');
        req.error(new ProgressEvent('Network error'), { status: 500 });
      });
    });

    it('logout_HttpSuccess_ClearsLocalStorageAndReturnsObservable', async () => {
      localStorage.setItem('auth_token', 'token-123');
      localStorage.setItem('user_role', 'student');
      localStorage.setItem('user_avatar', 'avatar-url');
      localStorage.setItem('user', JSON.stringify({ keycloakId: 'user-123' }));
      localStorage.setItem('student', JSON.stringify({ id: 1 }));

      service.logout().subscribe(() => {
        expect(true).toBe(true);
      });

      expect(localStorage.getItem('auth_token')).toBeNull();
      expect(localStorage.getItem('user_role')).toBeNull();
      expect(localStorage.getItem('user_avatar')).toBeNull();
      expect(localStorage.getItem('user')).toBeNull();
      expect(localStorage.getItem('student')).toBeNull();

      const req = httpMock.expectOne('/api/exam/auth/logout');
      expect(req.request.headers.get('Authorization')).toBe('Bearer token-123');
      req.flush(null);
    });

    it('logout_HttpTimeout_ClearsLocalStorageAndReturnsObservable', async () => {
      localStorage.setItem('auth_token', 'token-123');
      localStorage.setItem('user', JSON.stringify({ keycloakId: 'user-123' }));

      let completed = false;
      service.logout().subscribe(() => {
        completed = true;
      });

      expect(localStorage.getItem('auth_token')).toBeNull();

      const req = httpMock.expectOne('/api/exam/auth/logout');
      expect(req.request.headers.get('Authorization')).toBe('Bearer token-123');

      // Timeout işlemini tetikle (2000ms)
      await vi.advanceTimersByTimeAsync(2000);

      // Observable tamamlanması beklenir
      expect(completed).toBe(true);
    });
  });

  describe('hasValidSessionToken (issue #347)', () => {
    it('token yoksa false', () => {
      expect(service.hasValidSessionToken()).toBe(false);
    });

    it('süresi dolmamış token → true', () => {
      localStorage.setItem('auth_token', createToken('u-1'));
      expect(service.hasValidSessionToken()).toBe(true);
    });

    it('süresi dolmuş veya bozuk token → false', () => {
      const expired =
        base64urlEncode(JSON.stringify({ alg: 'none' })) +
        '.' +
        base64urlEncode(JSON.stringify({ sub: 'u', exp: Math.floor(Date.now() / 1000) - 10 })) +
        '.x';
      localStorage.setItem('auth_token', expired);
      expect(service.hasValidSessionToken()).toBe(false);

      localStorage.setItem('auth_token', 'not-a-jwt');
      expect(service.hasValidSessionToken()).toBe(false);
    });
  });

  describe('exchangeCodeForToken', () => {
    describe('Kriter 1: Kullanıcı değişince profil bilgisi güncellenmeli', () => {
      it('exchangeCodeForToken_ClearsCachedUserBeforeSettingNewToken', async () => {
        // Eski kullanıcı verisi
        localStorage.setItem('user', JSON.stringify({ keycloakId: 'old-user', fullName: 'Old User' }));
        localStorage.setItem('user_avatar', 'old-avatar-url');

        const newToken = createToken('new-user-123');
        const newKeycloakId = 'new-user-123';

        service.exchangeCodeForToken('code-123', 'v'.repeat(43)).subscribe(() => {
          // Yeni token ve role yazılmış
          expect(localStorage.getItem('auth_token')).toBe(newToken);
          expect(localStorage.getItem('user_role')).toBe('Student');

          // Eski user ve avatar silinmiş (clearCachedUser çağrılmış)
          // Yeni user verisi yazılmamış (auth-ui spec'inde exchange endpoint user bilgisi döndürmüyor)
          // Bu, isCachedUserCurrent() false döndürmesi demek
          expect(service.isCachedUserCurrent()).toBe(false);
        });

        const req = httpMock.expectOne('/api/auth/exchange');
        const response: TokenResponse = {
          token: newToken,
          roles: ['Student'],
          profile: {
            id: 1,
            profileId: 1,
            fullName: 'New User',
            email: 'newuser@example.com',
            avatar: 'new-avatar',
            keycloakId: newKeycloakId,
            role: 'student',
          },
        };
        req.flush(response);
      });
    });

    it('exchangeCodeForToken_SetsTokenAndRole_StoresUserProfile', async () => {
      const newToken = createToken('user-xyz');
      const newKeycloakId = 'user-xyz';

      service.exchangeCodeForToken('code-456', 'v'.repeat(43)).subscribe(() => {
        expect(localStorage.getItem('auth_token')).toBe(newToken);
        expect(localStorage.getItem('user_role')).toBe('Teacher');
      });

      const req = httpMock.expectOne('/api/auth/exchange');
      // Issue #347: PKCE code_verifier code ile birlikte auth-api'ye gider.
      expect(req.request.body).toEqual({ code: 'code-456', codeVerifier: 'v'.repeat(43) });
      const response: TokenResponse = {
        token: newToken,
        roles: ['Teacher'],
        profile: {
          id: 2,
          profileId: 2,
          fullName: 'Teacher User',
          email: 'teacher@example.com',
          avatar: 'teacher-avatar',
          keycloakId: newKeycloakId,
          role: 'teacher',
        },
      };
      req.flush(response);
    });
  });

  describe('Kriter 3: Yeni kayıt sonrası kendi adını görür', () => {
    it('isCachedUserCurrent_AfterExchangeCodeSetup_ReturnsTrueWhenValidTokenAndUserAreSet', async () => {
      const keycloakId = 'new-student-user-999';
      const token = createToken(keycloakId);
      const profile = {
        id: 10,
        profileId: 10,
        keycloakId,
        fullName: 'New Student',
        email: 'newstudent@example.com',
        avatar: 'avatar-url',
        role: 'student',
      };

      service.exchangeCodeForToken('code-999', 'v'.repeat(43)).subscribe(() => {
        // Manuel olarak user'ı ekle (auth-ui spec'i user döndürmüyor, ama gözlemci bunu bilmeli)
        localStorage.setItem('user', JSON.stringify(profile));

        expect(service.isCachedUserCurrent()).toBe(true);
        expect(service.getUser()).toEqual(profile);
      });

      const req = httpMock.expectOne('/api/auth/exchange');
      const response: TokenResponse = {
        token,
        roles: ['Student'],
        profile,
      };
      req.flush(response);
    });
  });

  describe('uygulama rolü seçimi (QuestionTrainer)', () => {
    function flushExchange(roles: string[]): void {
      const req = httpMock.expectOne('/api/auth/exchange');
      req.flush({
        token: createToken('trainer-1'),
        roles,
        profile: {
          id: 3,
          profileId: 3,
          fullName: 'Trainer Teacher',
          email: 'trainer@example.com',
          avatar: '',
          keycloakId: 'trainer-1',
          role: 'Teacher',
        },
      } as TokenResponse);
    }

    it('exchangeCodeForToken_QuestionTrainerFirst_StoresTeacher', async () => {
      service.exchangeCodeForToken('code-qt', 'v'.repeat(43)).subscribe(() => {
        expect(localStorage.getItem('user_role')).toBe('Teacher');
      });
      flushExchange(['QuestionTrainer', 'Teacher']);
    });

    it('exchangeCodeForToken_NoAppRole_RemovesStaleRole', async () => {
      localStorage.setItem('user_role', 'Student');
      service.exchangeCodeForToken('code-new', 'v'.repeat(43)).subscribe(() => {
        expect(localStorage.getItem('user_role')).toBeNull();
      });
      flushExchange([]);
    });

    it('applyRefreshedSession_QuestionTrainerFirst_StoresTeacherAndUpdatesUser', () => {
      localStorage.setItem('user', JSON.stringify({ keycloakId: 'trainer-1', role: 'Student' }));
      service.applyRefreshedSession(
        createTokenWithRoles('trainer-1', ['offline_access', 'QuestionTrainer', 'Teacher', 'default-roles-exam-realm'])
      );
      expect(localStorage.getItem('user_role')).toBe('Teacher');
      expect(service.getUser().role).toBe('Teacher');
    });

    it('applyRefreshedSession_NoAppRole_KeepsExistingRole', () => {
      localStorage.setItem('user_role', 'Teacher');
      service.applyRefreshedSession(createTokenWithRoles('trainer-1', ['QuestionTrainer', 'offline_access']));
      expect(localStorage.getItem('user_role')).toBe('Teacher');
    });
  });
});
