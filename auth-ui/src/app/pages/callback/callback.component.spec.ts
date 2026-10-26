import type { Mock, MockedObject } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
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
import { OIDC_FLOW_KEY_PREFIX, OidcFlowService, OidcLoginRecord } from '../../services/oidc-flow.service';
import { PRERENDERING_CHANGE_EVENT } from '../../shared/utils/when-activated.util';
import { MatSnackBar } from '@angular/material/snack-bar';
import { BRAND_NAME } from '../../shared/brand';

/**
 * Issue #347: callback yalnızca bu sekmede başlatılmış login'in `state`'ini kabul eder, code'u PKCE
 * `code_verifier` ile değiştirir ve hedefi `state`'ten değil saklanan kayıttan (allowlist'li) hesaplar.
 * Tam sayfa yönlendirme `redirect()` seam'i üzerinden yapılır; spec onu spy'lar (#28'deki engel kalktı).
 */
describe('CallbackComponent', () => {
  beforeEach(() => {
    vi.useFakeTimers({ advanceTimeDelta: 1, shouldAdvanceTime: true });
  });
  afterEach(() => {
    vi.useRealTimers();
  });
  const STATE = 'S'.repeat(43);
  const VERIFIER = 'v'.repeat(43);

  let routerSpy: MockedObject<Router>;
  let authServiceSpy: MockedObject<AuthService>;
  let snackBarSpy: MockedObject<MatSnackBar>;
  let redirectSpy: Mock;

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
    routerSpy = {
      navigate: vi.fn().mockName('Router.navigate'),
    } as unknown as MockedObject<Router>;
    snackBarSpy = {
      open: vi.fn().mockName('MatSnackBar.open'),
    } as unknown as MockedObject<MatSnackBar>;
    authServiceSpy = {
      exchangeCodeForToken: vi.fn().mockName('AuthService.exchangeCodeForToken'),
      hasValidSessionToken: vi.fn().mockName('AuthService.hasValidSessionToken'),
      getRealmRoles: vi.fn().mockName('AuthService.getRealmRoles'),
    } as unknown as MockedObject<AuthService>;
    authServiceSpy.exchangeCodeForToken.mockReturnValue(NEVER);
    authServiceSpy.hasValidSessionToken.mockReturnValue(false);
    authServiceSpy.getRealmRoles.mockReturnValue([]);

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
    redirectSpy = vi
      .spyOn(
        fixture.componentInstance as unknown as {
          redirect: (url: string) => void;
        },
        'redirect'
      )
      .mockReturnValue(undefined);
    return fixture;
  }

  beforeEach(() => sessionStorage.clear());
  afterEach(() => {
    Reflect.deleteProperty(document, 'prerendering');
    sessionStorage.clear();
  });

  it('should create', () => {
    const fixture = createComponent({});
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('brandTitle_ShowsBrandName (issue #409)', () => {
    const fixture = createComponent({});
    fixture.detectChanges();

    const title = (fixture.nativeElement as HTMLElement).querySelector('.ms-app-title')?.textContent?.trim();
    expect(title).toBe(BRAND_NAME);
    expect(BRAND_NAME).toBe('Hedef Okul');
  });

  it('ngOnInit_NoCodeParam_NavigatesToLoginWithoutCallingAuthService', () => {
    const fixture = createComponent({});
    fixture.detectChanges();

    expect(routerSpy.navigate).toHaveBeenCalledWith(['/login']);
    expect(authServiceSpy.exchangeCodeForToken).not.toHaveBeenCalled();
  });

  it('ngOnInit_MatchingState_ExchangesCodeWithStoredVerifierAndConsumesRecord', async () => {
    seedRecord();
    const fixture = createComponent({ code: 'auth-code', state: STATE });
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(150);

    expect(authServiceSpy.exchangeCodeForToken).toHaveBeenCalledTimes(1);

    expect(authServiceSpy.exchangeCodeForToken).toHaveBeenCalledWith('auth-code', VERIFIER);
    // Tek kullanımlık: kayıt tüketildi, aynı state tekrar kullanılamaz.
    expect(sessionStorage.getItem(OIDC_FLOW_KEY_PREFIX + STATE)).toBeNull();
  });

  describe('state doğrulaması (login CSRF)', () => {
    const cases: [string, Record<string, string>][] = [
      ['state yok', { code: 'auth-code' }],
      ['boş state', { code: 'auth-code', state: '' }],
      ['eski biçim state (open redirect denemesi)', { code: 'auth-code', state: 'https://evil.example~student' }],
      ['bilinmeyen state', { code: 'auth-code', state: 'X'.repeat(43) }],
    ];
    for (const [name, params] of cases) {
      it(`${name} → kod değişimi yok, hata mesajı, /login`, async () => {
        seedRecord();
        const fixture = createComponent(params);
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(150);

        expect(authServiceSpy.exchangeCodeForToken).not.toHaveBeenCalled();
        expect(redirectSpy).not.toHaveBeenCalled();
        expect(snackBarSpy.open).toHaveBeenCalledTimes(1);
        expect(snackBarSpy.open).toHaveBeenCalledWith(STATE_MISMATCH_MESSAGE, 'Kapat', { duration: 3000 });
        await vi.advanceTimersByTimeAsync(2000);
        expect(routerSpy.navigate).toHaveBeenCalledTimes(1);
        expect(routerSpy.navigate).toHaveBeenCalledWith(['/login']);
      });
    }

    it('süresi geçmiş kayıt → kod değişimi yok', async () => {
      seedRecord({ createdAt: 0 });
      const fixture = createComponent({ code: 'auth-code', state: STATE });
      fixture.detectChanges();
      await vi.advanceTimersByTimeAsync(2150);

      expect(authServiceSpy.exchangeCodeForToken).not.toHaveBeenCalled();
      expect(routerSpy.navigate).toHaveBeenCalledTimes(1);
      expect(routerSpy.navigate).toHaveBeenCalledWith(['/login']);
    });
  });

  describe('Keycloak ?error= dönüşü', () => {
    it('access_denied → kayıt tüketilir, mesaj + Tekrar dene, otomatik yönlendirme ve kod değişimi yok', async () => {
      seedRecord();
      const fixture = createComponent({ error: 'access_denied', state: STATE });
      fixture.detectChanges();
      await vi.advanceTimersByTimeAsync(5000);
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
      expect(routerSpy.navigate).toHaveBeenCalledTimes(1);
      expect(routerSpy.navigate).toHaveBeenCalledWith(['/login']);
    });

    it('bilinmeyen hata → genel mesaj; error_description sayfaya yazılmaz; code olsa da değişim yapılmaz', async () => {
      seedRecord();
      const fixture = createComponent({
        error: 'server_error',
        error_description: 'Sahte destek hattini arayin',
        code: 'auth-code',
        state: STATE,
      });
      fixture.detectChanges();
      await vi.advanceTimersByTimeAsync(3000);
      fixture.detectChanges();

      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('Giriş tamamlanamadı. Lütfen tekrar deneyin.');
      expect(text).not.toContain('Sahte destek');
      expect(authServiceSpy.exchangeCodeForToken).not.toHaveBeenCalled();
      expect(routerSpy.navigate).not.toHaveBeenCalled();
    });

    it('oidcErrorMessage bilinen kodları eşler', () => {
      expect(oidcErrorMessage('temporarily_unavailable')).toContain('kullanılamıyor');
      expect(oidcErrorMessage('login_required')).toContain('tekrar giriş');
    });
  });

  describe('geri tuşu: tüketilmiş state + geçerli oturum', () => {
    it('hata snackbar ı olmadan mevcut oturumun hedefine gider, code kullanılmaz', async () => {
      const fixture = createComponent({ code: 'used-code', state: STATE });
      authServiceSpy.hasValidSessionToken.mockReturnValue(true);
      authServiceSpy.getRealmRoles.mockReturnValue(['Teacher']);
      fixture.detectChanges();
      await vi.advanceTimersByTimeAsync(2500);

      expect(redirectSpy).toHaveBeenCalledTimes(1);

      expect(redirectSpy).toHaveBeenCalledWith('/dashboard');
      expect(snackBarSpy.open).not.toHaveBeenCalled();
      expect(routerSpy.navigate).not.toHaveBeenCalled();
      expect(authServiceSpy.exchangeCodeForToken).not.toHaveBeenCalled();
    });

    it('oturum yoksa yine state hatası verir', async () => {
      const fixture = createComponent({ code: 'used-code', state: STATE });
      fixture.detectChanges();
      await vi.advanceTimersByTimeAsync(2500);

      expect(redirectSpy).not.toHaveBeenCalled();
      expect(snackBarSpy.open).toHaveBeenCalledTimes(1);
      expect(snackBarSpy.open).toHaveBeenCalledWith(STATE_MISMATCH_MESSAGE, 'Kapat', { duration: 3000 });
    });
  });

  // Issue #231: exchange 401/503 `{ message }` — snackbar + 2 sn sonra /login.
  it('ngOnInit_Exchange401WithMessage_ShowsBackendMessageThenNavigatesToLoginAfterDelay', async () => {
    seedRecord();
    const fixture = createComponent({ code: 'expired-code', state: STATE });
    authServiceSpy.exchangeCodeForToken.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 401, error: { message: 'Oturum kodu geçersiz.' } }))
    );
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(150);

    expect(snackBarSpy.open).toHaveBeenCalledTimes(1);

    expect(snackBarSpy.open).toHaveBeenCalledWith('Oturum kodu geçersiz.', 'Kapat', { duration: 3000 });
    expect(routerSpy.navigate).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(2000);
    expect(routerSpy.navigate).toHaveBeenCalledTimes(1);
    expect(routerSpy.navigate).toHaveBeenCalledWith(['/login']);
  });

  it('ngOnInit_Exchange503WithoutBody_FallsBackToStaticMessage', async () => {
    seedRecord();
    const fixture = createComponent({ code: 'auth-code', state: STATE });
    authServiceSpy.exchangeCodeForToken.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 503, error: null }))
    );
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(150);

    expect(snackBarSpy.open).toHaveBeenCalledTimes(1);

    expect(snackBarSpy.open).toHaveBeenCalledWith('Giriş başarısız! Lütfen bilgilerinizi kontrol edin.', 'Kapat', {
      duration: 3000,
    });
    await vi.advanceTimersByTimeAsync(2000);
    expect(routerSpy.navigate).toHaveBeenCalledTimes(1);
    expect(routerSpy.navigate).toHaveBeenCalledWith(['/login']);
  });

  describe('başarılı değişim sonrası yönlendirme (issue #28, #86, #347)', () => {
    async function runSuccess(roles: string[], record: Partial<OidcLoginRecord> = {}): Promise<string> {
      seedRecord(record);
      const fixture = createComponent({ code: 'auth-code', state: STATE });
      authServiceSpy.exchangeCodeForToken.mockReturnValue(of(tokenResponse(roles)));
      fixture.detectChanges();
      await vi.advanceTimersByTimeAsync(300);
      expect(redirectSpy).toHaveBeenCalledTimes(1);
      return vi.mocked(redirectSpy).mock.lastCall![0];
    }

    it('Admin → /admin/dashboard', async () => {
      expect(await runSuccess(['Admin'])).toBe('/admin/dashboard');
    });

    it('Admin + Teacher → /admin/dashboard', async () => {
      expect(await runSuccess(['Teacher', 'Admin'])).toBe('/admin/dashboard');
    });

    it('Teacher → /dashboard', async () => {
      expect(await runSuccess(['Teacher'])).toBe('/dashboard');
    });

    // Issue #420: velinin varsayılan hedefi veli paneli; istenen güvenli yol yine önceliklidir.
    it('Parent → /parent', async () => {
      expect(await runSuccess(['Parent'])).toBe('/parent');
    });

    it('Parent + saklanan returnPath → returnPath', async () => {
      expect(await runSuccess(['Parent'], { returnPath: '/my-children' })).toBe('/my-children');
    });

    // Issue #438: veli panelinden açılan çocuk hesabı kullanıcı adıyla girer (e-postasız); öğrenci kaydı ve Student rolü hazır
    // olduğu için profil tamamlamaya DÜŞMEZ, öğrenci paneline gider.
    it('veli tarafından açılan çocuk hesabı (yalnız Student) → /dashboard, complete-profile değil', async () => {
      const target = await runSuccess(['Student']);
      expect(target).toBe('/dashboard');
      expect(target).not.toContain('complete-profile');
    });

    it('rol yok → /app/complete-profile', async () => {
      expect(await runSuccess([])).toBe('/app/complete-profile');
    });

    it('rol yok + kayıt niyeti → /app/complete-profile?role=<niyet>', async () => {
      expect(await runSuccess([], { intent: 'teacher' })).toBe('/app/complete-profile?role=teacher');
    });

    it('saklanan güvenli returnPath kullanılır', async () => {
      expect(await runSuccess(['Student'], { returnPath: '/tests/7' })).toBe('/tests/7');
    });

    it('saklanan kayıt kurcalanıp dış URL yazılsa da reddedilir', async () => {
      expect(await runSuccess(['Student'], { returnPath: 'https://evil.example/phish' })).toBe('/dashboard');
    });
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

  describe('issue #595: ön-yükleme (prerender)', () => {
    function setPrerendering(value: boolean): void {
      Object.defineProperty(document, 'prerendering', { value, configurable: true, writable: true });
    }

    it('prerender_NoConsumeNoExchange_UntilActivated_ThenOnce', async () => {
      seedRecord();
      setPrerendering(true);
      const fixture = createComponent({ code: 'auth-code', state: STATE });
      const consume = vi.spyOn(TestBed.inject(OidcFlowService), 'consume');

      fixture.detectChanges();
      await vi.advanceTimersByTimeAsync(150);

      expect(consume).not.toHaveBeenCalled();
      expect(authServiceSpy.exchangeCodeForToken).not.toHaveBeenCalled();
      expect(sessionStorage.getItem(OIDC_FLOW_KEY_PREFIX + STATE)).not.toBeNull();

      setPrerendering(false);
      document.dispatchEvent(new Event(PRERENDERING_CHANGE_EVENT));
      document.dispatchEvent(new Event(PRERENDERING_CHANGE_EVENT));
      await vi.advanceTimersByTimeAsync(150);

      expect(consume).toHaveBeenCalledTimes(1);
      expect(authServiceSpy.exchangeCodeForToken).toHaveBeenCalledTimes(1);
      expect(authServiceSpy.exchangeCodeForToken).toHaveBeenCalledWith('auth-code', VERIFIER);
      expect(sessionStorage.getItem(OIDC_FLOW_KEY_PREFIX + STATE)).toBeNull();
    });

    it('prerender_DestroyedBeforeActivation_NeverConsumes', async () => {
      seedRecord();
      setPrerendering(true);
      const fixture = createComponent({ code: 'auth-code', state: STATE });
      const consume = vi.spyOn(TestBed.inject(OidcFlowService), 'consume');

      fixture.detectChanges();
      fixture.destroy();
      setPrerendering(false);
      document.dispatchEvent(new Event(PRERENDERING_CHANGE_EVENT));
      await vi.advanceTimersByTimeAsync(150);

      expect(consume).not.toHaveBeenCalled();
      expect(authServiceSpy.exchangeCodeForToken).not.toHaveBeenCalled();
    });

    it('noPrerender_ConsumesImmediately', async () => {
      seedRecord();
      const fixture = createComponent({ code: 'auth-code', state: STATE });
      const consume = vi.spyOn(TestBed.inject(OidcFlowService), 'consume');

      fixture.detectChanges();
      expect(consume).toHaveBeenCalledTimes(1);
      await vi.advanceTimersByTimeAsync(150);
      expect(authServiceSpy.exchangeCodeForToken).toHaveBeenCalledTimes(1);
    });
  });
});
