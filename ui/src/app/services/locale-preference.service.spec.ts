import type { MockedObject } from "vitest";
import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient, withXhr } from '@angular/common/http';
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
    let authServiceSpy: MockedObject<AuthService>;
    let localeServiceSpy: MockedObject<LocaleService>;
    /** Çağrı sırasını (PUT → refresh → setLocale) doğrulamak için ortak kayıt. */
    let calls: string[];

    beforeEach(() => {
        calls = [];
        localStorage.removeItem('user');

        authServiceSpy = {
            hasToken: vi.fn().mockName("AuthService.hasToken"),
            refresh: vi.fn().mockName("AuthService.refresh"),
            setUser: vi.fn().mockName("AuthService.setUser")
        } as unknown as MockedObject<AuthService>;
        localeServiceSpy = {
            setLocale: vi.fn().mockName("LocaleService.setLocale")
        } as unknown as MockedObject<LocaleService>;

        // Gerçek setUser gibi localStorage'a yazar; aşağıdaki testler depolanan profili okur (issue #191).
        authServiceSpy.setUser.mockImplementation((profile: UserProfile | null) => {
            if (profile) {
                localStorage.setItem('user', JSON.stringify(profile));
            }
            else {
                localStorage.removeItem('user');
            }
        });

        authServiceSpy.refresh.mockImplementation(() => {
            calls.push('refresh');
            return of(profileFixture('en'));
        });
        localeServiceSpy.setLocale.mockImplementation(() => {
            calls.push('setLocale');
        });

        TestBed.configureTestingModule({
            providers: [
                provideHttpClient(withXhr()),
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
        authServiceSpy.hasToken.mockReturnValue(false);

        let completed = false;
        service.persistPreference('en').subscribe({ complete: () => (completed = true) });

        expect(completed).toBe(true);
        expect(localeServiceSpy.setLocale).toHaveBeenCalledTimes(1);
        expect(localeServiceSpy.setLocale).toHaveBeenCalledWith('en');
        expect(authServiceSpy.refresh).not.toHaveBeenCalled();
        httpMock.expectNone('/api/auth/me/locale');
    });

    it('persistPreference_AuthenticatedUser_PutsThenRefreshesThenAppliesLocale', () => {
        authServiceSpy.hasToken.mockReturnValue(true);

        service.persistPreference('en').subscribe();

        const request = httpMock.expectOne('/api/auth/me/locale');
        expect(request.request.method).toBe('PUT');
        expect(request.request.body).toEqual({ preferredLocale: 'en' });

        calls.push('put');
        request.flush(profileFixture('en'));

        expect(calls).toEqual(['put', 'refresh', 'setLocale']);
        expect(localeServiceSpy.setLocale).toHaveBeenCalledTimes(1);
        expect(localeServiceSpy.setLocale).toHaveBeenCalledWith('en');
    });

    it('persistPreference_AuthenticatedUser_StoresRefreshedProfileInLocalStorage', () => {
        authServiceSpy.hasToken.mockReturnValue(true);

        service.persistPreference('en').subscribe();
        httpMock.expectOne('/api/auth/me/locale').flush(profileFixture('tr'));

        // refresh() yanıtı PUT yanıtının önüne geçer (exam API'nin tazelenmiş profili).
        expect(JSON.parse(localStorage.getItem('user') ?? '{}').preferredLocale).toBe('en');
    });

    it('persistPreference_PutFails_StillAppliesLocaleLocally', () => {
        authServiceSpy.hasToken.mockReturnValue(true);
        vi.spyOn(console, 'warn').mockReturnValue(undefined);

        let completed = false;
        service.persistPreference('en').subscribe({ complete: () => (completed = true) });
        httpMock.expectOne('/api/auth/me/locale').flush('nope', { status: 400, statusText: 'Bad Request' });

        expect(completed).toBe(true);
        expect(localeServiceSpy.setLocale).toHaveBeenCalledTimes(1);
        expect(localeServiceSpy.setLocale).toHaveBeenCalledWith('en');
        expect(console.warn).toHaveBeenCalled();
        expect(localStorage.getItem('user')).toBeNull();
    });

    it('persistPreference_RefreshFails_KeepsPutResultAndAppliesLocale', () => {
        authServiceSpy.hasToken.mockReturnValue(true);
        authServiceSpy.refresh.mockReturnValue(throwError(() => new Error('refresh down')));
        vi.spyOn(console, 'warn').mockReturnValue(undefined);

        service.persistPreference('en').subscribe();
        httpMock.expectOne('/api/auth/me/locale').flush(profileFixture('en'));

        expect(JSON.parse(localStorage.getItem('user') ?? '{}').preferredLocale).toBe('en');
        expect(localeServiceSpy.setLocale).toHaveBeenCalledTimes(1);
        expect(localeServiceSpy.setLocale).toHaveBeenCalledWith('en');
    });

    it('persistPreference_NotSubscribed_DoesNothing', () => {
        authServiceSpy.hasToken.mockReturnValue(true);

        service.persistPreference('en');

        httpMock.expectNone('/api/auth/me/locale');
        expect(localeServiceSpy.setLocale).not.toHaveBeenCalled();
    });
});
