import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { of, throwError } from 'rxjs';

import { AuthService, UserProfile } from './auth.service';
import { LocalePreferenceService } from './locale-preference.service';
import { LocaleService } from './locale.service';

function profileFixture(preferredLocale: string): UserProfile {
  return {
    email: 'ogrenci@example.com',
    avatar: '',
    fullName: 'Test Kullanıcı',
    id: 1,
    keycloakId: 'sub-1',
    profileId: 2,
    role: 'Student',
    preferredLocale,
  };
}

describe('LocalePreferenceService', () => {
  let service: LocalePreferenceService;
  let httpMock: HttpTestingController;
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let localeServiceSpy: jasmine.SpyObj<LocaleService>;
  /** Çağrı sırasını (PUT → refresh → setLocale) doğrulamak için ortak kayıt. */
  let calls: string[];

  beforeEach(() => {
    calls = [];
    localStorage.removeItem('user');

    authServiceSpy = jasmine.createSpyObj<AuthService>('AuthService', ['hasToken', 'refresh']);
    localeServiceSpy = jasmine.createSpyObj<LocaleService>('LocaleService', ['setLocale']);

    authServiceSpy.refresh.and.callFake(() => {
      calls.push('refresh');
      return of(profileFixture('en'));
    });
    localeServiceSpy.setLocale.and.callFake(() => {
      calls.push('setLocale');
    });

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: authServiceSpy },
        { provide: LocaleService, useValue: localeServiceSpy },
      ],
    });

    service = TestBed.inject(LocalePreferenceService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.removeItem('user');
  });

  it('persistPreference_AnonymousVisitor_AppliesLocaleWithoutServerCall', () => {
    authServiceSpy.hasToken.and.returnValue(false);

    let completed = false;
    service.persistPreference('en').subscribe({ complete: () => (completed = true) });

    expect(completed).toBeTrue();
    expect(localeServiceSpy.setLocale).toHaveBeenCalledOnceWith('en');
    expect(authServiceSpy.refresh).not.toHaveBeenCalled();
    httpMock.expectNone('/api/auth/me/locale');
  });

  it('persistPreference_AuthenticatedUser_PutsThenRefreshesThenAppliesLocale', () => {
    authServiceSpy.hasToken.and.returnValue(true);

    service.persistPreference('en').subscribe();

    const request = httpMock.expectOne('/api/auth/me/locale');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ preferredLocale: 'en' });

    calls.push('put');
    request.flush(profileFixture('en'));

    expect(calls).toEqual(['put', 'refresh', 'setLocale']);
    expect(localeServiceSpy.setLocale).toHaveBeenCalledOnceWith('en');
  });

  it('persistPreference_AuthenticatedUser_StoresRefreshedProfileInLocalStorage', () => {
    authServiceSpy.hasToken.and.returnValue(true);

    service.persistPreference('en').subscribe();
    httpMock.expectOne('/api/auth/me/locale').flush(profileFixture('tr'));

    // refresh() yanıtı PUT yanıtının önüne geçer (exam API'nin tazelenmiş profili).
    expect(JSON.parse(localStorage.getItem('user') ?? '{}').preferredLocale).toBe('en');
  });

  it('persistPreference_PutFails_StillAppliesLocaleLocally', () => {
    authServiceSpy.hasToken.and.returnValue(true);
    spyOn(console, 'warn');

    let completed = false;
    service.persistPreference('en').subscribe({ complete: () => (completed = true) });
    httpMock.expectOne('/api/auth/me/locale').flush('nope', { status: 400, statusText: 'Bad Request' });

    expect(completed).toBeTrue();
    expect(localeServiceSpy.setLocale).toHaveBeenCalledOnceWith('en');
    expect(console.warn).toHaveBeenCalled();
    expect(localStorage.getItem('user')).toBeNull();
  });

  it('persistPreference_RefreshFails_KeepsPutResultAndAppliesLocale', () => {
    authServiceSpy.hasToken.and.returnValue(true);
    authServiceSpy.refresh.and.returnValue(throwError(() => new Error('refresh down')));
    spyOn(console, 'warn');

    service.persistPreference('en').subscribe();
    httpMock.expectOne('/api/auth/me/locale').flush(profileFixture('en'));

    expect(JSON.parse(localStorage.getItem('user') ?? '{}').preferredLocale).toBe('en');
    expect(localeServiceSpy.setLocale).toHaveBeenCalledOnceWith('en');
  });

  it('persistPreference_NotSubscribed_DoesNothing', () => {
    authServiceSpy.hasToken.and.returnValue(true);

    service.persistPreference('en');

    httpMock.expectNone('/api/auth/me/locale');
    expect(localeServiceSpy.setLocale).not.toHaveBeenCalled();
  });
});
