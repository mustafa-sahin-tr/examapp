import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';

import { LoginComponent } from './login.component';
import { LOCALE_STORAGE_KEY } from '../../services/locale-hint.service';
import { OIDC_FLOW_KEY_PREFIX, OidcLoginRecord } from '../../services/oidc-flow.service';
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
  let redirectSpy: jasmine.Spy<(url: string) => void>;
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
    redirectSpy = spyOn(component as unknown as { redirect: (url: string) => void }, 'redirect');
    redirected = new Promise<string>((resolve) => redirectSpy.and.callFake(resolve));
  });

  afterEach(() => {
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

  it('mevcut query parametrelerini koruyarak ui_locales ekler', () => {
    localStorage.setItem(LOCALE_STORAGE_KEY, 'tr');

    const url = new URL(
      (component as unknown as { withLoginLocale: (u: string) => string }).withLoginLocale(
        '/oidc-login?redirect_uri=%2Fdashboard'
      ),
      'http://localhost'
    );

    expect(url.searchParams.get('redirect_uri')).toBe('/dashboard');
    expect(url.searchParams.get('ui_locales')).toBe('tr');
  });
});
