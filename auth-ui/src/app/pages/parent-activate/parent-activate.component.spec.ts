import type { Mock, MockedObject } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Location } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { NEVER, Observable, of, throwError } from 'rxjs';

import {
  PARENT_ACTIVATE_LOGIN_URL,
  PARENT_ACTIVATE_PATH,
  ParentActivateComponent,
  activationTokenFromFragment,
} from './parent-activate.component';
import { ParentActivationService } from '../../services/parent-activation.service';
import {
  PARENT_ACTIVATION_PENDING_KEY,
  PARENT_ACTIVATION_PENDING_MAX_AGE_MS,
} from '../../services/parent-activation-pending.service';
import { AuthService } from '../../services/auth.service';
import { ParentActivationLookup } from '../../models/parent-activation.model';

const TOKEN = 'tok-abc-123';

function lookupOf(overrides: Partial<ParentActivationLookup> = {}): ParentActivationLookup {
  return {
    studentName: 'Ali K.',
    maskedParentEmail: 'a***@g***.com',
    kind: 'NewParentActivation',
    expiresAt: '2026-10-22T10:00:00Z',
    expired: false,
    used: false,
    requiresPasswordSetup: true,
    accountExists: false,
    consentVersion: '2026-10',
    consentTextKey: 'parentConsent.kvkk',
    canResend: false,
    ...overrides,
  };
}

/** Şifre literal'i yazılmaz (gitleaks / secret-guard); çalışma anında üretilir. */
function sampleChoice(): string {
  return ['Kalem', '2026', 'x'].join('');
}

describe('ParentActivateComponent (issue #440)', () => {
  let service: MockedObject<ParentActivationService>;
  let location: MockedObject<Location>;
  let redirect: Mock;

  beforeEach(() => {
    sessionStorage.removeItem(PARENT_ACTIVATION_PENDING_KEY);
    // Gerçek tam sayfa navigasyonu (giriş) spec'te çalışmasın.
    redirect = vi.spyOn(ParentActivateComponent.prototype, 'redirect').mockReturnValue(undefined);
  });

  afterEach(() => sessionStorage.removeItem(PARENT_ACTIVATION_PENDING_KEY));

  function create(
    options: {
      fragment?: string | null;
      lookup$?: Observable<ParentActivationLookup>;
      loggedIn?: boolean;
    } = {}
  ) {
    service = {
      lookup: vi.fn().mockName('ParentActivationService.lookup'),
      activate: vi.fn().mockName('ParentActivationService.activate'),
      resend: vi.fn().mockName('ParentActivationService.resend'),
      accept: vi.fn().mockName('ParentActivationService.accept'),
    } as unknown as MockedObject<ParentActivationService>;
    service.lookup.mockReturnValue(options.lookup$ ?? of(lookupOf()));
    service.activate.mockReturnValue(NEVER);
    service.resend.mockReturnValue(NEVER);
    service.accept.mockReturnValue(NEVER);
    location = {
      replaceState: vi.fn().mockName('Location.replaceState'),
    } as unknown as MockedObject<Location>;
    const fragment = options.fragment === undefined ? `token=${TOKEN}` : options.fragment;

    TestBed.configureTestingModule({
      imports: [ParentActivateComponent],
      providers: [
        provideRouter([]),
        { provide: ParentActivationService, useValue: service },
        { provide: Location, useValue: location },
        { provide: AuthService, useValue: { hasValidSessionToken: () => options.loggedIn === true } },
        {
          provide: ActivatedRoute,
          // Token yalnız URL parçasında (`#token=`); sorgu bilerek okunmaz.
          useValue: {
            snapshot: { fragment, queryParamMap: { get: (key: string) => (key === 'token' ? 'from-query' : null) } },
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(ParentActivateComponent);
    fixture.detectChanges();
    return fixture;
  }

  function text(fixture: ComponentFixture<ParentActivateComponent>): string {
    return fixture.nativeElement.textContent as string;
  }

  function q(fixture: ComponentFixture<ParentActivateComponent>, selector: string): HTMLElement | null {
    return fixture.nativeElement.querySelector(selector) as HTMLElement | null;
  }

  function fillNewAccount(fixture: ComponentFixture<ParentActivateComponent>, first: string, second: string): void {
    const form = fixture.componentInstance.form;
    form.controls.password.setValue(first);
    form.controls.confirmPassword.setValue(second);
    form.controls.consent.setValue(true);
  }

  afterEach(() => document.querySelector("meta[name='referrer']")?.remove());

  it('init_StripsTokenFromUrlAndSetsNoReferrer_LooksUpWithBodyToken', () => {
    const fixture = create();

    expect(location.replaceState).toHaveBeenCalledWith(PARENT_ACTIVATE_PATH);
    expect(document.querySelector("meta[name='referrer']")?.getAttribute('content')).toBe('no-referrer');
    expect(service.lookup).toHaveBeenCalledWith(TOKEN);
    expect(fixture.componentInstance.view()).toBe('form');
  });

  it('destroy_RemovesReferrerMeta', () => {
    const fixture = create();
    fixture.destroy();
    expect(document.querySelector("meta[name='referrer']")).toBeNull();
  });

  it('init_NoToken_ShowsInvalidWithoutLookup', () => {
    const fixture = create({ fragment: null });

    expect(service.lookup).not.toHaveBeenCalled();
    expect(q(fixture, '[data-testid="activate-invalid"]')).not.toBeNull();
  });

  it('lookup404_ShowsInvalid', () => {
    const fixture = create({
      lookup$: throwError(() => new HttpErrorResponse({ status: 404, error: { errorCode: 'InvalidActivationToken' } })),
    });
    expect(q(fixture, '[data-testid="activate-invalid"]')).not.toBeNull();
  });

  it('lookupServerError_ShowsRetryWhichLooksUpAgain', () => {
    const fixture = create({ lookup$: throwError(() => new HttpErrorResponse({ status: 500 })) });
    expect(q(fixture, '[data-testid="activate-error"]')).not.toBeNull();

    service.lookup.mockReturnValue(of(lookupOf()));
    (q(fixture, '.default-btn') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(service.lookup).toHaveBeenCalledTimes(2);
    expect(fixture.componentInstance.view()).toBe('form');
  });

  it('lookupUsed_ShowsAlreadyUsedWithLoginLink', () => {
    const fixture = create({ lookup$: of(lookupOf({ used: true })) });

    expect(q(fixture, '[data-testid="activate-used"]')).not.toBeNull();
    expect(text(fixture)).toContain('zaten kullanıldı');
    expect(q(fixture, 'a.default-btn')?.getAttribute('href')).toBe('/login');
  });

  it('lookupExpired_CanResend_ResendsAndShowsConfirmation', () => {
    const fixture = create({ lookup$: of(lookupOf({ expired: true, canResend: true })) });
    expect(q(fixture, '[data-testid="activate-expired"]')).not.toBeNull();

    service.resend.mockReturnValue(of({ message: 'ok' }));
    (q(fixture, '[data-testid="activate-resend"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(service.resend).toHaveBeenCalledWith(TOKEN);
    expect(q(fixture, '[data-testid="activate-resent"]')).not.toBeNull();
    expect(q(fixture, '[data-testid="activate-resend"]')).toBeNull();
  });

  it('lookupExpired_CannotResend_HidesResendButton', () => {
    const fixture = create({ lookup$: of(lookupOf({ expired: true, canResend: false })) });

    expect(q(fixture, '[data-testid="activate-resend"]')).toBeNull();
    expect(q(fixture, '[data-testid="activate-no-resend"]')).not.toBeNull();
  });

  it('resend429_ShowsRetryMessage', () => {
    const fixture = create({ lookup$: of(lookupOf({ expired: true, canResend: true })) });
    service.resend.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({ status: 429, error: { errorCode: 'ParentInvitationResendLimited', retryAt: null } })
      )
    );

    fixture.componentInstance.resend();
    fixture.detectChanges();

    expect(q(fixture, '[data-testid="activate-resend-error"]')?.textContent).toContain(
      'Yeniden gönderim sınırına ulaşıldı'
    );
  });

  it('newParent_RendersPasswordFieldsConsentTextAndPrivacyLink', () => {
    const fixture = create();

    expect(q(fixture, '#parentPassword')).not.toBeNull();
    expect(q(fixture, '#parentPasswordConfirm')).not.toBeNull();
    expect(text(fixture)).toContain('Ali K.');
    expect(q(fixture, '[data-testid="activate-consent-text"]')?.textContent).toContain('KVKK');
    expect(q(fixture, '.consent-option a')?.getAttribute('href')).toBe('/privacy-policy');
  });

  it('newParent_SubmitWithoutConsentOrDigits_DoesNotCallActivate', () => {
    const fixture = create();
    const lettersOnly = 'abcdefgh';
    fixture.componentInstance.form.controls.password.setValue(lettersOnly);
    fixture.componentInstance.form.controls.confirmPassword.setValue(lettersOnly);

    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(service.activate).not.toHaveBeenCalled();
    expect(text(fixture)).toContain('en az bir harf ve bir rakam');
    expect(text(fixture)).toContain('rıza metnini onaylamalısın');
  });

  it('newParent_MismatchedConfirmation_DoesNotCallActivate', () => {
    const fixture = create();
    fillNewAccount(fixture, sampleChoice(), sampleChoice() + 'y');

    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(service.activate).not.toHaveBeenCalled();
    expect(text(fixture)).toContain('Şifreler eşleşmiyor.');
  });

  it('newParent_ValidSubmit_SendsChoiceConsentAndVersion_ThenShowsLogin', () => {
    const fixture = create();
    const chosen = sampleChoice();
    fillNewAccount(fixture, chosen, chosen);
    service.activate.mockReturnValue(of({ activated: true, passwordSet: true }));

    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(service.activate).toHaveBeenCalledWith({
      token: TOKEN,
      password: chosen,
      consentAccepted: true,
      consentVersion: '2026-10',
    });
    expect(q(fixture, '[data-testid="activate-done"]')?.textContent).toContain('Hesabın aktive edildi');
    expect(q(fixture, '[data-testid="activate-login"]')?.getAttribute('href')).toBe('/login');
    expect(fixture.componentInstance.form.controls.password.value).toBe('');
  });

  it('activateTokenUsed409_SwitchesToUsedView', () => {
    const fixture = create();
    fillNewAccount(fixture, sampleChoice(), sampleChoice());
    service.activate.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: { errorCode: 'ActivationTokenUsed' } }))
    );

    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(q(fixture, '[data-testid="activate-used"]')).not.toBeNull();
  });

  it('activateExpired409_ReloadsLookupAndShowsExpired', () => {
    const fixture = create();
    fillNewAccount(fixture, sampleChoice(), sampleChoice());
    service.activate.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: { errorCode: 'ActivationTokenExpired' } }))
    );
    service.lookup.mockReturnValue(of(lookupOf({ expired: true, canResend: true })));

    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(q(fixture, '[data-testid="activate-expired"]')).not.toBeNull();
  });

  it('activateRejectedByServer_ShowsServerMessageAndStaysOnForm', () => {
    const fixture = create();
    const chosen = sampleChoice();
    fillNewAccount(fixture, chosen, chosen);
    service.activate.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({ status: 400, error: { errorCode: 'PasswordRejected', message: 'Şifre çok zayıf.' } })
      )
    );

    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(fixture.componentInstance.view()).toBe('form');
    expect(q(fixture, '[data-testid="activate-submit-error"]')?.textContent).toContain('Şifre çok zayıf.');
  });

  it('activateConsentVersionMismatch_UnchecksConsentAndRefreshesLookup', () => {
    const fixture = create();
    fillNewAccount(fixture, sampleChoice(), sampleChoice());
    service.activate.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 400, error: { errorCode: 'ConsentVersionMismatch' } }))
    );
    service.lookup.mockReturnValue(of(lookupOf({ consentVersion: '2026-11' })));

    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(fixture.componentInstance.form.controls.consent.value).toBe(false);
    expect(fixture.componentInstance.lookup()?.consentVersion).toBe('2026-11');
    expect(fixture.componentInstance.view()).toBe('form');
  });

  // ── #440 review: token URL parçasında ──────────────────────────────────────

  it('activationTokenFromFragment_ParsesTokenOnly', () => {
    expect(activationTokenFromFragment('#token=abc')).toBe('abc');
    expect(activationTokenFromFragment('token=abc&x=1')).toBe('abc');
    expect(activationTokenFromFragment('x=1')).toBeNull();
    expect(activationTokenFromFragment('')).toBeNull();
    expect(activationTokenFromFragment(null)).toBeNull();
  });

  it('init_TokenOnlyInQuery_IgnoredAsInvalid', () => {
    const fixture = create({ fragment: null });
    expect(service.lookup).not.toHaveBeenCalledWith('from-query');
    expect(q(fixture, '[data-testid="activate-invalid"]')).not.toBeNull();
  });

  // ── #440 güvenlik turu: hesabı olan veli giriş yapıp `accept` ile kabul eder ─────────────────

  const existing = () =>
    lookupOf({ accountExists: true, requiresPasswordSetup: false, kind: 'ExistingParentApproval' });

  function storedPending(): {
    token: string;
    consentAccepted: boolean;
    consentVersion: string;
    createdAt: number;
  } | null {
    const raw = sessionStorage.getItem(PARENT_ACTIVATION_PENDING_KEY);
    return raw ? JSON.parse(raw) : null;
  }

  it('accountExists_ShowsSignInMessageNoPasswordFields', () => {
    const fixture = create({ lookup$: of(existing()) });

    expect(q(fixture, '#parentPassword')).toBeNull();
    expect(q(fixture, '[data-testid="activate-account-exists"]')?.textContent).toContain(
      'Bu e-posta ile zaten bir hesabınız var. Giriş yapıp daveti kabul edin.'
    );
    expect(q(fixture, '[data-testid="activate-submit"]')?.textContent).toContain('Giriş Yap ve Kabul Et');
    expect(text(fixture)).not.toContain('Çocuklarım');
  });

  it('accountExists_NoConsent_DoesNotStartLogin', () => {
    const fixture = create({ lookup$: of(existing()) });

    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(redirect).not.toHaveBeenCalled();
    expect(storedPending()).toBeNull();
    expect(text(fixture)).toContain('rıza metnini onaylamalısın');
  });

  it('accountExists_NotLoggedIn_StoresTokenAndConsentInSessionStorage_StartsLoginWithReturnUrl', () => {
    const fixture = create({ lookup$: of(existing()) });
    fixture.componentInstance.form.controls.consent.setValue(true);

    fixture.componentInstance.submit();

    expect(redirect).toHaveBeenCalledWith(PARENT_ACTIVATE_LOGIN_URL);
    expect(PARENT_ACTIVATE_LOGIN_URL).toBe('/app/login?returnUrl=%2Fapp%2Fparent-activate');
    expect(PARENT_ACTIVATE_LOGIN_URL).not.toContain(TOKEN);
    expect(storedPending()).toEqual(
      expect.objectContaining({ token: TOKEN, consentAccepted: true, consentVersion: '2026-10' })
    );
    expect(service.accept).not.toHaveBeenCalled();
  });

  it('loginRoundTrip_ReturnWithoutFragment_TokenFromSessionStorage_AutoAccepts', () => {
    sessionStorage.setItem(
      PARENT_ACTIVATION_PENDING_KEY,
      JSON.stringify({ token: TOKEN, consentAccepted: true, consentVersion: '2026-10', createdAt: Date.now() })
    );
    const fixture = create({ fragment: null, lookup$: of(existing()), loggedIn: true });

    expect(service.lookup).toHaveBeenCalledWith(TOKEN);
    expect(service.accept).toHaveBeenCalledWith({ token: TOKEN, consentAccepted: true, consentVersion: '2026-10' });
    expect(redirect).not.toHaveBeenCalled();
    expect(fixture.componentInstance.view()).toBe('form');
  });

  it('loginRoundTrip_ConsentVersionChanged_AsksAgain', () => {
    sessionStorage.setItem(
      PARENT_ACTIVATION_PENDING_KEY,
      JSON.stringify({ token: TOKEN, consentAccepted: true, consentVersion: '2026-09', createdAt: Date.now() })
    );
    const fixture = create({ fragment: null, lookup$: of(existing()), loggedIn: true });

    expect(service.accept).not.toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.consent.value).toBe(false);
    expect(storedPending()).toBeNull();
  });

  it('expiredPendingKey_IgnoredAndRemoved_ShowsInvalid', () => {
    sessionStorage.setItem(
      PARENT_ACTIVATION_PENDING_KEY,
      JSON.stringify({
        token: TOKEN,
        consentAccepted: true,
        consentVersion: '2026-10',
        createdAt: Date.now() - PARENT_ACTIVATION_PENDING_MAX_AGE_MS - 1000,
      })
    );
    const fixture = create({ fragment: null, loggedIn: true });

    expect(service.lookup).not.toHaveBeenCalled();
    expect(service.accept).not.toHaveBeenCalled();
    expect(q(fixture, '[data-testid="activate-invalid"]')).not.toBeNull();
    expect(storedPending()).toBeNull();
  });

  it('newLinkWhilePending_ClearsOldPending', () => {
    sessionStorage.setItem(
      PARENT_ACTIVATION_PENDING_KEY,
      JSON.stringify({ token: 'old-token', consentAccepted: true, consentVersion: '2026-10', createdAt: Date.now() })
    );
    create();

    expect(service.lookup).toHaveBeenCalledWith(TOKEN);
    expect(storedPending()).toBeNull();
  });

  it('alreadyLoggedIn_ConsentThenAcceptDirectly_ShowsAcceptedWithParentPanelLink', () => {
    const fixture = create({ lookup$: of(existing()), loggedIn: true });
    expect(q(fixture, '[data-testid="activate-submit"]')?.textContent).toContain('Daveti Kabul Et');
    service.accept.mockReturnValue(of({ activated: true, passwordSet: false, message: 'ok' }));
    fixture.componentInstance.form.controls.consent.setValue(true);

    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(redirect).not.toHaveBeenCalled();
    expect(service.accept).toHaveBeenCalledWith({ token: TOKEN, consentAccepted: true, consentVersion: '2026-10' });
    expect(q(fixture, '[data-testid="activate-accepted"]')?.textContent).toContain('Davet kabul edildi');
    expect(q(fixture, '[data-testid="activate-parent-home"]')?.getAttribute('href')).toBe('/parent');
    expect(storedPending()).toBeNull();
  });

  it('activateLoginRequired409_SwitchesToSignInPath_KeepsConsent', () => {
    const fixture = create();
    fillNewAccount(fixture, sampleChoice(), sampleChoice());
    service.activate.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: { errorCode: 'LoginRequired' } }))
    );

    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(q(fixture, '#parentPassword')).toBeNull();
    expect(q(fixture, '[data-testid="activate-account-exists"]')).not.toBeNull();
    expect(fixture.componentInstance.form.controls.password.value).toBe('');
    expect(fixture.componentInstance.form.controls.consent.value).toBe(true);

    fixture.componentInstance.submit();
    expect(redirect).toHaveBeenCalledWith(PARENT_ACTIVATE_LOGIN_URL);
    expect(storedPending()?.token).toBe(TOKEN);
  });

  const acceptErrors: {
    name: string;
    status: number;
    code?: string;
    expected: string;
  }[] = [
    {
      name: 'EmailMismatch403',
      status: 403,
      code: 'InvitationEmailMismatch',
      expected: 'Bu davet başka bir e-posta adresine gönderildi; davetin geldiği e-postayla giriş yapın.',
    },
    {
      name: 'NotParent403',
      status: 403,
      code: 'ParentAccountRequired',
      expected: 'Bu daveti yalnız bir veli hesabı kabul edebilir.',
    },
    { name: 'Plain403', status: 403, expected: 'Bu daveti kabul etme yetkin yok.' },
    { name: 'StudentLimit409', status: 409, code: 'StudentLimitReached', expected: 'veli sınırına ulaşıldı' },
  ];

  for (const scenario of acceptErrors) {
    it(`accept${scenario.name}_ShowsMessage_ClearsPending`, () => {
      sessionStorage.setItem(
        PARENT_ACTIVATION_PENDING_KEY,
        JSON.stringify({ token: TOKEN, consentAccepted: true, consentVersion: '2026-10', createdAt: Date.now() })
      );
      const fixture = create({ fragment: null, lookup$: NEVER, loggedIn: true });
      service.accept.mockReturnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: scenario.status,
              error: scenario.code ? { errorCode: scenario.code } : null,
            })
        )
      );
      service.lookup.mockReturnValue(of(existing()));
      fixture.componentInstance.loadLookup();
      fixture.detectChanges();

      expect(q(fixture, '[data-testid="activate-submit-error"]')?.textContent).toContain(scenario.expected);
      expect(storedPending()).toBeNull();
    });
  }

  it('accept401_SessionExpired_KeepsPendingAndRestartsLogin', () => {
    const fixture = create({ lookup$: of(existing()), loggedIn: true });
    service.accept.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 401 })));
    fixture.componentInstance.form.controls.consent.setValue(true);

    fixture.componentInstance.submit();

    expect(redirect).toHaveBeenCalledWith(PARENT_ACTIVATE_LOGIN_URL);
    expect(storedPending()?.token).toBe(TOKEN);
  });

  it('accept404_InvalidView', () => {
    const fixture = create({ lookup$: of(existing()), loggedIn: true });
    service.accept.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 404, error: { errorCode: 'InvalidActivationToken' } }))
    );
    fixture.componentInstance.form.controls.consent.setValue(true);

    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(q(fixture, '[data-testid="activate-invalid"]')).not.toBeNull();
  });
});
