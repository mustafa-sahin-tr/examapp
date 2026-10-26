import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';

import { LoginComponent } from './login.component';
import { LOCALE_STORAGE_KEY } from '../../services/locale-hint.service';
import { OIDC_FLOW_KEY_PREFIX, OidcFlowService, OidcLoginRecord } from '../../services/oidc-flow.service';
import { PRERENDERING_CHANGE_EVENT } from '../../shared/utils/when-activated.util';
import { computeCodeChallenge } from '../../shared/utils/pkce.util';

/**
 * LoginComponent tek iş yapar: Keycloak girişini (`/oidc-login`) başlatır. Eski e-posta/şifre formu
 * (onSubmit, issue #231 testleri) şablonsuz ölü koddu; #347'de kaldırıldı.
 * Gerçek navigasyon testte çalışmasın diye `redirect()` spy'lanır — bu yüzden
 * komponentte ayrı bir `protected redirect(url)` metodu var.
 * Issue #347: URL'de rastgele state + S256 code_challenge taşınır; niyet ve returnUrl sessionStorage kaydında.
 */
describe('LoginComponent', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let component: LoginComponent;
  let redirectSpy: Mock;
  let redirected: Promise<string>;
  const routeStub = { snapshot: { queryParamMap: convertToParamMap({}) } };

  /** startOidcLogin crypto.subtle'ı (zone dışı native promise) bekler; yönlendirmeyi promise olarak yakala. */
  async function redirectUrl(): Promise<URL> {
    return new URL(await redirected, 'http://localhost');
  }

  function storedRecord(state: string): OidcLoginRecord {
    return JSON.parse(sessionStorage.getItem(OIDC_FLOW_KEY_PREFIX + state)!) as OidcLoginRecord;
  }

  beforeEach(async () => {
    localStorage.clear();
    sessionStorage.clear();
    routeStub.snapshot.queryParamMap = convertToParamMap({});

    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [{ provide: ActivatedRoute, useValue: routeStub }],
    }).compileComponents();

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    // ngOnInit'ten (detectChanges) önce kur, aksi halde tarayıcı gerçekten yönlenir.
    redirectSpy = vi
      .spyOn(
        component as unknown as {
          redirect: (url: string) => void;
        },
        'redirect'
      )
      .mockReturnValue(undefined);
    redirected = new Promise<string>((resolve) => redirectSpy.mockImplementation(resolve));
  });

  afterEach(() => {
    // Ön-yükleme testinin gölgelediği özelliği kaldır (Document.prototype getter'ı geri gelir).
    Reflect.deleteProperty(document, 'prerendering');
    localStorage.clear();
    sessionStorage.clear();
  });

  it('should create', async () => {
    fixture.detectChanges();
    // Yönlendirme async (crypto.subtle); spy spec bitince kaldırıldığı için beklenmeli, yoksa gerçek navigasyon olur.
    await redirected;

    expect(component).toBeTruthy();
  });

  it('/oidc-login adresine ui_locales ile yönlendirir', async () => {
    localStorage.setItem(LOCALE_STORAGE_KEY, 'en');

    fixture.detectChanges();
    const url = await redirectUrl();

    expect(redirectSpy).toHaveBeenCalledTimes(1);
    expect(url.pathname).toBe('/oidc-login');
    expect(url.searchParams.get('ui_locales')).toBe('en');
  });

  it('issue #347: state + S256 challenge gönderir, verifier yalnızca sessionStorage kaydında', async () => {
    fixture.detectChanges();
    const url = await redirectUrl();

    const state = url.searchParams.get('state')!;
    expect(state).toMatch(/^[A-Za-z0-9_-]{43}$/);
    expect(url.searchParams.get('code_challenge_method')).toBe('S256');
    const record = storedRecord(state);
    expect(url.searchParams.get('code_challenge')).toBe(await computeCodeChallenge(record.codeVerifier));
    expect(url.toString()).not.toContain(record.codeVerifier);
  });

  it('issue #347: ?intent=teacher gateway e iletilir ve kayıtta tutulur, state e gömülmez', async () => {
    routeStub.snapshot.queryParamMap = convertToParamMap({ intent: 'teacher', returnUrl: '/tests/3' });

    fixture.detectChanges();
    const url = await redirectUrl();

    const state = url.searchParams.get('state')!;
    expect(url.searchParams.get('intent')).toBe('teacher');
    expect(state).not.toContain('~');
    expect(storedRecord(state).intent).toBe('teacher');
    expect(storedRecord(state).returnPath).toBe('/tests/3');
  });

  it('issue #347: dış returnUrl kayda yazılmaz', async () => {
    routeStub.snapshot.queryParamMap = convertToParamMap({ returnUrl: 'https://evil.example' });

    fixture.detectChanges();
    const url = await redirectUrl();

    expect(storedRecord(url.searchParams.get('state')!).returnPath).toBeNull();
  });

  it('issue #595: normal açılışta begin hemen bir kez çağrılır', async () => {
    const begin = vi.spyOn(TestBed.inject(OidcFlowService), 'begin');

    fixture.detectChanges();
    expect(begin).toHaveBeenCalledTimes(1);
    await redirected;
    expect(redirectSpy).toHaveBeenCalledTimes(1);
  });

  it('issue #595: ön-yüklemede begin çağrılmaz; prerenderingchange sonrası bir kez başlatır', async () => {
    const begin = vi.spyOn(TestBed.inject(OidcFlowService), 'begin');
    Object.defineProperty(document, 'prerendering', { value: true, configurable: true, writable: true });

    fixture.detectChanges();
    // Korumasız ngOnInit begin'i senkron çağırır; yönlendirme ve kayıt yazımı yalnız begin'den sonra gelir.
    // Bu yüzden negatif iddialar zamanlamaya bağlı değil — whenStable yalnızca bekleyen işleri boşaltır.
    expect(begin).not.toHaveBeenCalled();
    await fixture.whenStable();

    expect(begin).not.toHaveBeenCalled();
    expect(redirectSpy).not.toHaveBeenCalled();
    expect(Object.keys(sessionStorage).filter((k) => k.startsWith(OIDC_FLOW_KEY_PREFIX))).toEqual([]);

    Object.defineProperty(document, 'prerendering', { value: false, configurable: true, writable: true });
    document.dispatchEvent(new Event(PRERENDERING_CHANGE_EVENT));
    const url = await redirectUrl();
    document.dispatchEvent(new Event(PRERENDERING_CHANGE_EVENT));

    expect(begin).toHaveBeenCalledTimes(1);
    expect(redirectSpy).toHaveBeenCalledTimes(1);
    expect(storedRecord(url.searchParams.get('state')!).state).toBe(url.searchParams.get('state'));
  });

  it('issue #595: ön-yüklemede yok edilen sayfa etkinleşince başlatmaz', async () => {
    const begin = vi.spyOn(TestBed.inject(OidcFlowService), 'begin');
    Object.defineProperty(document, 'prerendering', { value: true, configurable: true, writable: true });

    fixture.detectChanges();
    fixture.destroy();
    Object.defineProperty(document, 'prerendering', { value: false, configurable: true, writable: true });
    document.dispatchEvent(new Event(PRERENDERING_CHANGE_EVENT));
    // Etkinleşme dinleyicisi kaldırılmasaydı begin bu dispatch içinde senkron çağrılırdı.
    expect(begin).not.toHaveBeenCalled();
    await fixture.whenStable();

    expect(begin).not.toHaveBeenCalled();
    expect(redirectSpy).not.toHaveBeenCalled();
  });

  it('mevcut query parametrelerini koruyarak ui_locales ekler', () => {
    localStorage.setItem(LOCALE_STORAGE_KEY, 'tr');

    const url = new URL(
      (
        component as unknown as {
          withLoginLocale: (u: string) => string;
        }
      ).withLoginLocale('/oidc-login?redirect_uri=%2Fdashboard'),
      'http://localhost'
    );

    expect(url.searchParams.get('redirect_uri')).toBe('/dashboard');
    expect(url.searchParams.get('ui_locales')).toBe('tr');
  });
});
