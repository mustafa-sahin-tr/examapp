import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { NEVER, of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';

import {
  CallbackComponent,
  STATE_MISMATCH_MESSAGE,
  oidcErrorMessage,
  postLoginDestination,
} from './callback.component';
import { AuthService, TokenResponse } from '../../services/auth.service';
import { OIDC_FLOW_KEY_PREFIX, OidcLoginRecord } from '../../services/oidc-flow.service';
import { MatSnackBar } from '@angular/material/snack-bar';

/**
 * Issue #347: callback yalnızca bu sekmede başlatılmış login'in `state`'ini kabul eder, code'u PKCE
 * `code_verifier` ile değiştirir ve hedefi `state`'ten değil saklanan kayıttan (allowlist'li) hesaplar.
 * Tam sayfa yönlendirme `redirect()` seam'i üzerinden yapılır; spec onu spy'lar (#28'deki engel kalktı).
 */
describe('CallbackComponent', () => {
  const STATE = 'S'.repeat(43);
  const VERIFIER = 'v'.repeat(43);

  let routerSpy: jasmine.SpyObj<Router>;
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let snackBarSpy: jasmine.SpyObj<MatSnackBar>;
  let redirectSpy: jasmine.Spy<(url: string) => void>;

  function seedRecord(overrides: Partial<OidcLoginRecord> = {}): void {
    const record: OidcLoginRecord = {
      state: STATE,
      codeVerifier: VERIFIER,
      intent: null,
      returnPath: null,
      createdAt: Date.now(),
      ...overrides,
    };
    sessionStorage.setItem(OIDC_FLOW_KEY_PREFIX + record.state, JSON.stringify(record));
  }

  function tokenResponse(roles: string[]): TokenResponse {
    return { token: 'token', roles } as TokenResponse;
  }

  function createComponent(queryParams: Record<string, string>): ComponentFixture<CallbackComponent> {
    routerSpy = jasmine.createSpyObj('Router', ['navigate']);
    snackBarSpy = jasmine.createSpyObj('MatSnackBar', ['open']);
    authServiceSpy = jasmine.createSpyObj('AuthService', ['exchangeCodeForToken', 'hasValidSessionToken', 'getRealmRoles']);
    authServiceSpy.exchangeCodeForToken.and.returnValue(NEVER);
    authServiceSpy.hasValidSessionToken.and.returnValue(false);
    authServiceSpy.getRealmRoles.and.returnValue([]);

    TestBed.configureTestingModule({
      imports: [CallbackComponent],
      providers: [
        { provide: ActivatedRoute, useValue: { queryParams: of(queryParams) } },
        { provide: Router, useValue: routerSpy },
        { provide: AuthService, useValue: authServiceSpy },
        { provide: MatSnackBar, useValue: snackBarSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(CallbackComponent);
    redirectSpy = spyOn(fixture.componentInstance as unknown as { redirect: (url: string) => void }, 'redirect');
    return fixture;
  }

  beforeEach(() => sessionStorage.clear());
  afterEach(() => sessionStorage.clear());

  it('should create', () => {
    const fixture = createComponent({});
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('ngOnInit_NoCodeParam_NavigatesToLoginWithoutCallingAuthService', () => {
    const fixture = createComponent({});
    fixture.detectChanges();

    expect(routerSpy.navigate).toHaveBeenCalledWith(['/login']);
    expect(authServiceSpy.exchangeCodeForToken).not.toHaveBeenCalled();
  });

  it(
    'ngOnInit_MatchingState_ExchangesCodeWithStoredVerifierAndConsumesRecord',
    fakeAsync(() => {
      seedRecord();
      const fixture = createComponent({ code: 'auth-code', state: STATE });
      fixture.detectChanges();
      tick(150);

      expect(authServiceSpy.exchangeCodeForToken).toHaveBeenCalledOnceWith('auth-code', VERIFIER);
      // Tek kullanımlık: kayıt tüketildi, aynı state tekrar kullanılamaz.
      expect(sessionStorage.getItem(OIDC_FLOW_KEY_PREFIX + STATE)).toBeNull();
    })
  );

  describe('state doğrulaması (login CSRF)', () => {
    const cases: [string, Record<string, string>][] = [
      ['state yok', { code: 'auth-code' }],
      ['boş state', { code: 'auth-code', state: '' }],
      ['eski biçim state (open redirect denemesi)', { code: 'auth-code', state: 'https://evil.example~student' }],
      ['bilinmeyen state', { code: 'auth-code', state: 'X'.repeat(43) }],
    ];
    for (const [name, params] of cases) {
      it(
        `${name} → kod değişimi yok, hata mesajı, /login`,
        fakeAsync(() => {
          seedRecord();
          const fixture = createComponent(params);
          fixture.detectChanges();
          tick(150);

          expect(authServiceSpy.exchangeCodeForToken).not.toHaveBeenCalled();
          expect(redirectSpy).not.toHaveBeenCalled();
          expect(snackBarSpy.open).toHaveBeenCalledOnceWith(STATE_MISMATCH_MESSAGE, 'Kapat', { duration: 3000 });
          tick(2000);
          expect(routerSpy.navigate).toHaveBeenCalledOnceWith(['/login']);
        })
      );
    }

    it(
      'süresi geçmiş kayıt → kod değişimi yok',
      fakeAsync(() => {
        seedRecord({ createdAt: 0 });
        const fixture = createComponent({ code: 'auth-code', state: STATE });
        fixture.detectChanges();
        tick(2150);

        expect(authServiceSpy.exchangeCodeForToken).not.toHaveBeenCalled();
        expect(routerSpy.navigate).toHaveBeenCalledOnceWith(['/login']);
      })
    );
  });

  describe('Keycloak ?error= dönüşü', () => {
    it(
      'access_denied → kayıt tüketilir, mesaj + Tekrar dene, otomatik yönlendirme ve kod değişimi yok',
      fakeAsync(() => {
        seedRecord();
        const fixture = createComponent({ error: 'access_denied', state: STATE });
        fixture.detectChanges();
        tick(5000);
        fixture.detectChanges();

        expect(sessionStorage.getItem(OIDC_FLOW_KEY_PREFIX + STATE)).toBeNull();
        expect(authServiceSpy.exchangeCodeForToken).not.toHaveBeenCalled();
        expect(routerSpy.navigate).not.toHaveBeenCalled();
        expect(redirectSpy).not.toHaveBeenCalled();
        const el: HTMLElement = fixture.nativeElement;
        expect(el.querySelector('[role="alert"] h2')?.textContent).toContain('Giriş iptal edildi.');

        const button = el.querySelector<HTMLButtonElement>('.ms-retry-button');
        expect(button?.textContent).toContain('Tekrar dene');
        button!.click();
        expect(routerSpy.navigate).toHaveBeenCalledOnceWith(['/login']);
      })
    );

    it(
      'bilinmeyen hata → genel mesaj; error_description sayfaya yazılmaz; code olsa da değişim yapılmaz',
      fakeAsync(() => {
        seedRecord();
        const fixture = createComponent({
          error: 'server_error',
          error_description: 'Sahte destek hattini arayin',
          code: 'auth-code',
          state: STATE,
        });
        fixture.detectChanges();
        tick(3000);
        fixture.detectChanges();

        const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
        expect(text).toContain('Giriş tamamlanamadı. Lütfen tekrar deneyin.');
        expect(text).not.toContain('Sahte destek');
        expect(authServiceSpy.exchangeCodeForToken).not.toHaveBeenCalled();
        expect(routerSpy.navigate).not.toHaveBeenCalled();
      })
    );

    it('oidcErrorMessage bilinen kodları eşler', () => {
      expect(oidcErrorMessage('temporarily_unavailable')).toContain('kullanılamıyor');
      expect(oidcErrorMessage('login_required')).toContain('tekrar giriş');
    });
  });

  describe('geri tuşu: tüketilmiş state + geçerli oturum', () => {
    it(
      'hata snackbar ı olmadan mevcut oturumun hedefine gider, code kullanılmaz',
      fakeAsync(() => {
        const fixture = createComponent({ code: 'used-code', state: STATE });
        authServiceSpy.hasValidSessionToken.and.returnValue(true);
        authServiceSpy.getRealmRoles.and.returnValue(['Teacher']);
        fixture.detectChanges();
        tick(2500);

        expect(redirectSpy).toHaveBeenCalledOnceWith('/dashboard');
        expect(snackBarSpy.open).not.toHaveBeenCalled();
        expect(routerSpy.navigate).not.toHaveBeenCalled();
        expect(authServiceSpy.exchangeCodeForToken).not.toHaveBeenCalled();
      })
    );

    it(
      'oturum yoksa yine state hatası verir',
      fakeAsync(() => {
        const fixture = createComponent({ code: 'used-code', state: STATE });
        fixture.detectChanges();
        tick(2500);

        expect(redirectSpy).not.toHaveBeenCalled();
        expect(snackBarSpy.open).toHaveBeenCalledOnceWith(STATE_MISMATCH_MESSAGE, 'Kapat', { duration: 3000 });
      })
    );
  });

  // Issue #231: exchange 401/503 `{ message }` — snackbar + 2 sn sonra /login.
  it(
    'ngOnInit_Exchange401WithMessage_ShowsBackendMessageThenNavigatesToLoginAfterDelay',
    fakeAsync(() => {
      seedRecord();
      const fixture = createComponent({ code: 'expired-code', state: STATE });
      authServiceSpy.exchangeCodeForToken.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 401, error: { message: 'Oturum kodu geçersiz.' } }))
      );
      fixture.detectChanges();
      tick(150);

      expect(snackBarSpy.open).toHaveBeenCalledOnceWith('Oturum kodu geçersiz.', 'Kapat', { duration: 3000 });
      expect(routerSpy.navigate).not.toHaveBeenCalled();

      tick(2000);
      expect(routerSpy.navigate).toHaveBeenCalledOnceWith(['/login']);
    })
  );

  it(
    'ngOnInit_Exchange503WithoutBody_FallsBackToStaticMessage',
    fakeAsync(() => {
      seedRecord();
      const fixture = createComponent({ code: 'auth-code', state: STATE });
      authServiceSpy.exchangeCodeForToken.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 503, error: null }))
      );
      fixture.detectChanges();
      tick(150);

      expect(snackBarSpy.open).toHaveBeenCalledOnceWith(
        'Giriş başarısız! Lütfen bilgilerinizi kontrol edin.',
        'Kapat',
        { duration: 3000 }
      );
      tick(2000);
      expect(routerSpy.navigate).toHaveBeenCalledOnceWith(['/login']);
    })
  );

  describe('başarılı değişim sonrası yönlendirme (issue #28, #86, #347)', () => {
    function runSuccess(roles: string[], record: Partial<OidcLoginRecord> = {}): string {
      seedRecord(record);
      const fixture = createComponent({ code: 'auth-code', state: STATE });
      authServiceSpy.exchangeCodeForToken.and.returnValue(of(tokenResponse(roles)));
      fixture.detectChanges();
      tick(300);
      expect(redirectSpy).toHaveBeenCalledTimes(1);
      return redirectSpy.calls.mostRecent().args[0];
    }

    it('Admin → /admin/dashboard', fakeAsync(() => {
      expect(runSuccess(['Admin'])).toBe('/admin/dashboard');
    }));

    it('Admin + Teacher → /admin/dashboard', fakeAsync(() => {
      expect(runSuccess(['Teacher', 'Admin'])).toBe('/admin/dashboard');
    }));

    it('Teacher → /dashboard', fakeAsync(() => {
      expect(runSuccess(['Teacher'])).toBe('/dashboard');
    }));

    // Issue #420: velinin varsayılan hedefi veli paneli; istenen güvenli yol yine önceliklidir.
    it('Parent → /parent', fakeAsync(() => {
      expect(runSuccess(['Parent'])).toBe('/parent');
    }));

    it('Parent + saklanan returnPath → returnPath', fakeAsync(() => {
      expect(runSuccess(['Parent'], { returnPath: '/my-children' })).toBe('/my-children');
    }));

    it('rol yok → /app/complete-profile', fakeAsync(() => {
      expect(runSuccess([])).toBe('/app/complete-profile');
    }));

    it('rol yok + kayıt niyeti → /app/complete-profile?role=<niyet>', fakeAsync(() => {
      expect(runSuccess([], { intent: 'teacher' })).toBe('/app/complete-profile?role=teacher');
    }));

    it('saklanan güvenli returnPath kullanılır', fakeAsync(() => {
      expect(runSuccess(['Student'], { returnPath: '/tests/7' })).toBe('/tests/7');
    }));

    it('saklanan kayıt kurcalanıp dış URL yazılsa da reddedilir', fakeAsync(() => {
      expect(runSuccess(['Student'], { returnPath: 'https://evil.example/phish' })).toBe('/dashboard');
    }));
  });

  describe('postLoginDestination', () => {
    it('protokol-göreli / javascript: / ters eğik çizgi hedefleri varsayılana düşer', () => {
      for (const returnPath of ['//evil.example', 'javascript:alert(1)', '/\\evil.example', '\\\\evil.example']) {
        expect(postLoginDestination(['Student'], { intent: null, returnPath })).toBe('/dashboard');
      }
    });

    it('rolsüz kullanıcıda returnPath profil tamamlamayı atlatmaz', () => {
      expect(postLoginDestination([], { intent: null, returnPath: '/tests/1' })).toBe('/app/complete-profile');
    });
  });
});
