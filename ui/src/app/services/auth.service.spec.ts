import type { Mock } from "vitest";
import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient, withXhr } from '@angular/common/http';
import { Router, provideRouter } from '@angular/router';
import { AuthService, UserProfile } from './auth.service';
import { Teacher } from '../models/teacher';
import { teacherAccountSuspendedOf } from '../models/teacher-approval.model';
import { ParentStatusStore } from './parent-status.store';

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
    beforeEach(() => {
        vi.useFakeTimers();
    });
    afterEach(() => {
        vi.useRealTimers();
    });
    let service: AuthService;
    let httpMock: HttpTestingController;
    let routerSpy: Mock;

    beforeEach(() => {
        TestBed.configureTestingModule({
            imports: [HttpClientTestingModule],
            providers: [AuthService],
        });

        service = TestBed.inject(AuthService);
        httpMock = TestBed.inject(HttpTestingController);
        routerSpy = vi.spyOn(service, 'goLogin').mockReturnValue(undefined);

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
            const token = base64urlEncode(JSON.stringify(header)) +
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

        it('logout_HttpTimeout_ClearsLocalStorageAndCallsGoLogin', async () => {
            localStorage.setItem('auth_token', 'token-123');
            localStorage.setItem('user', JSON.stringify({ keycloakId: 'user-123' }));

            service.logout();

            expect(localStorage.getItem('auth_token')).toBeNull();

            const req = httpMock.expectOne('/api/exam/auth/logout');
            expect(req.request.headers.get('Authorization')).toBe('Bearer token-123');

            // Timeout işlemini tetikle (2000ms)
            await vi.advanceTimersByTimeAsync(2000);

            // goLogin çağrılması beklenir
            expect(routerSpy).toHaveBeenCalledTimes(1);
        });
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

    describe('refreshToken', () => {
        it('refreshToken_SuccessfulResponse_ReturnsAccessToken', async () => {
            service.refreshToken().subscribe({
                next: (token) => {
                    expect(token).toBe('example.jwt.token');
                    ;
                },
                error: () => expect.fail('should succeed'),
            });

            const req = httpMock.expectOne('/api/auth/refresh-token');
            const payload = { accessToken: 'example.jwt.token' }; // example test data
            req.flush(payload);
        });

        it('refreshToken_EmptyAccessToken_ThrowsErrorAndClearsStorage', async () => {
            localStorage.setItem('auth_token', 'example.old.token');

            service.refreshToken().subscribe({
                next: () => expect.fail('should error'),
                error: () => {
                    expect(localStorage.getItem('auth_token')).toBeNull();
                    expect(routerSpy).toHaveBeenCalled();
                    ;
                },
            });

            const req = httpMock.expectOne('/api/auth/refresh-token');
            const payload = { accessToken: '' }; // example empty token
            req.flush(payload);
        });

        it('refreshToken_HttpError_ThrowsErrorAndClearsStorage', async () => {
            localStorage.setItem('auth_token', 'example.old.token');

            service.refreshToken().subscribe({
                next: () => expect.fail('should error'),
                error: () => {
                    expect(localStorage.getItem('auth_token')).toBeNull();
                    expect(routerSpy).toHaveBeenCalled();
                    ;
                },
            });

            const req = httpMock.expectOne('/api/auth/refresh-token');
            req.error(new ProgressEvent('error'), { status: 401 });
        });

        it('refreshToken_TwoConcurrentCalls_OnlyMakesOneHttpRequest', async () => {
            let successCount = 0;

            service.refreshToken().subscribe({
                next: (token) => {
                    successCount++;
                    expect(token).toBe('example.shared.token');
                    if (successCount === 2) {
                        ;
                    }
                },
                error: () => expect.fail('should succeed'),
            });

            service.refreshToken().subscribe({
                next: (token) => {
                    successCount++;
                    expect(token).toBe('example.shared.token');
                    if (successCount === 2) {
                        ;
                    }
                },
                error: () => expect.fail('should succeed'),
            });

            // Only one request should be made (single-flight pattern)
            const requests = httpMock.match('/api/auth/refresh-token');
            expect(requests.length).toBe(1);
            const payload = { accessToken: 'example.shared.token' }; // example test data
            requests[0].flush(payload);
        });

        it('refreshToken_TimeoutAfter10Seconds_ThrowsErrorAndClearsStorage', async () => {
            localStorage.setItem('auth_token', 'example.old.token');

            service.refreshToken().subscribe({
                next: () => expect.fail('should error'),
                error: () => {
                    expect(localStorage.getItem('auth_token')).toBeNull();
                    expect(routerSpy).toHaveBeenCalled();
                },
            });

            const req = httpMock.expectOne('/api/auth/refresh-token');

            // Trigger timeout after 10 seconds
            await vi.advanceTimersByTimeAsync(10000);

            expect(localStorage.getItem('auth_token')).toBeNull();
        });

        it('refreshToken_AfterError_NewCallMakesNewRequest', async () => {
            let requestCount = 0;

            // First call fails
            service.refreshToken().subscribe({
                next: () => expect.fail('should error'),
                error: () => {
                    // Second call (cache should be cleared)
                    service.refreshToken().subscribe({
                        next: (token) => {
                            expect(token).toBe('example.new.token');
                            expect(requestCount).toBe(2);
                            ;
                        },
                        error: () => expect.fail('should succeed on second call'),
                    });

                    const secondReq = httpMock.expectOne('/api/auth/refresh-token');
                    requestCount++;
                    const payload = { accessToken: 'example.new.token' }; // example test data
                    secondReq.flush(payload);
                },
            });

            const firstReq = httpMock.expectOne('/api/auth/refresh-token');
            requestCount++;
            firstReq.error(new ProgressEvent('error'), { status: 401 });
        });
    });
});

/** Issue #287: öğretmen hesabı onay durumu (computed + profil yenileme + 403 TeacherNotApproved işleme). */
describe('AuthService — teacher account approval (issue #287)', () => {
    let service: AuthService;
    let httpMock: HttpTestingController;
    let navigateByUrl: Mock;
    let router: Router;

    function tokenWithRoles(roles: string[]): string {
        const payload = { sub: 'kc-1', exp: Math.floor(Date.now() / 1000) + 3600, realm_access: { roles } };
        return `${base64urlEncode(JSON.stringify({ alg: 'RS256', typ: 'JWT' }))}.${base64urlEncode(JSON.stringify(payload))}.${base64urlEncode('sig')}`;
    }

    function profile(teacher?: Partial<Teacher>): UserProfile {
        return {
            email: 't@x.com',
            avatar: '',
            fullName: 'T',
            id: 1,
            keycloakId: 'kc-1',
            profileId: 1,
            role: 'Teacher',
            teacher: teacher ? ({ id: 5, userId: 1, schoolName: '', ...teacher } as Teacher) : undefined,
        };
    }

    function setup(roles: string[], cached: UserProfile | null): void {
        localStorage.clear();
        localStorage.setItem('auth_token', tokenWithRoles(roles));
        if (cached)
            localStorage.setItem('user', JSON.stringify(cached));
        TestBed.configureTestingModule({
            providers: [provideHttpClient(withXhr()), provideHttpClientTesting(), provideRouter([])],
        });
        service = TestBed.inject(AuthService);
        httpMock = TestBed.inject(HttpTestingController);
        router = TestBed.inject(Router);
        navigateByUrl = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    }

    afterEach(() => {
        httpMock.verify();
        localStorage.clear();
    });

    it('isUnapprovedTeacher_TeacherWithApprovedFalse_True', () => {
        setup(['Teacher'], profile({ teacherAccountApproved: false, teacherApplicationStatus: 'Pending' }));
        expect(service.isUnapprovedTeacher()).toBe(true);
        expect(service.teacherAccountApproved()).toBe(false);
    });

    it('isUnapprovedTeacher_TeacherApprovedWithLaterPendingApplication_False (school → independent switch)', () => {
        setup(['Teacher'], profile({ teacherAccountApproved: true, teacherApplicationStatus: 'Pending' }));
        expect(service.isUnapprovedTeacher()).toBe(false);
    });

    it('isUnapprovedTeacher_TeacherWithoutApprovalField_FalseUntilKnown', () => {
        setup(['Teacher'], profile({}));
        expect(service.isUnapprovedTeacher()).toBe(false);
    });

    it('isUnapprovedTeacher_StudentOrAdmin_AlwaysFalse', () => {
        setup(['Student', 'Admin'], profile({ teacherAccountApproved: false }));
        expect(service.isUnapprovedTeacher()).toBe(false);
    });

    for (const adminRole of ['Admin', 'SuperAdmin']) {
        it(`isUnapprovedTeacher_TeacherAnd${adminRole}_ExemptLikeBackend`, () => {
            setup(['Teacher', adminRole], profile({ teacherAccountApproved: false }));
            expect(service.isTeacherApprovalExempt()).toBe(true);
            expect(service.isUnapprovedTeacher()).toBe(false);

            service.handleTeacherNotApproved();
            expect(navigateByUrl).not.toHaveBeenCalled();
            httpMock.expectNone('/api/exam/auth/refresh');
        });
    }

    it('refreshProfile_ConcurrentCalls_SingleRequest_UpdatesUserSignal', () => {
        setup(['Teacher'], profile({}));
        const results: (UserProfile | null)[] = [];
        service.refreshProfile().subscribe((p) => results.push(p));
        service.refreshProfile().subscribe((p) => results.push(p));

        const req = httpMock.expectOne('/api/exam/auth/refresh');
        req.flush(profile({ teacherAccountApproved: false, teacherApplicationStatus: 'Rejected', rejectionReason: 'Eksik' }));

        expect(results.length).toBe(2);
        expect(service.user()?.teacher?.rejectionReason).toBe('Eksik');
        expect(service.isUnapprovedTeacher()).toBe(true);
    });

    it('refreshProfile_StudentParentStatus_GoesToStoreNotLocalStorage (issue #440/#441)', () => {
        setup(['Student'], null);
        const status = { mode: 'AwaitingActivation', requiresParent: true, graceDays: 30, isFinalWarning: false };

        service.refreshProfile().subscribe();
        httpMock.expectOne('/api/exam/auth/refresh').flush({
            ...profile(undefined),
            role: 'Student',
            student: { id: 3, userId: 1, studentNumber: '1', schoolName: '', grade: { id: 1, name: '8' }, parentStatus: status },
        });

        const store = TestBed.inject(ParentStatusStore);
        expect(store.activationRestricted()).toBe(true);
        expect(service.user()?.student?.parentStatus).toBeUndefined();
        expect(JSON.parse(localStorage.getItem('user') ?? '{}').student?.parentStatus).toBeUndefined();

        vi.spyOn(service, 'goLogin').mockReturnValue(undefined);
        service.logout();
        expect(store.loaded()).toBe(false);
        httpMock.expectOne('/api/exam/auth/logout').flush({});
    });

    it('refreshProfile_SuspendedTeacher_PersistsFlagToSignalAndLocalStorage_AndCountsAsUnapproved (issue #289)', () => {
        setup(['Teacher'], profile({ teacherAccountApproved: true, teacherApplicationStatus: 'Approved' }));
        expect(service.isUnapprovedTeacher()).toBe(false);

        service.refreshProfile().subscribe();
        httpMock.expectOne('/api/exam/auth/refresh').flush(profile({ teacherAccountApproved: false, teacherAccountSuspended: true, teacherApplicationStatus: 'Approved' }));

        expect(teacherAccountSuspendedOf(service.user())).toBe(true);
        expect(JSON.parse(localStorage.getItem('user') ?? '{}').teacher?.teacherAccountSuspended).toBe(true);
        expect(service.isUnapprovedTeacher()).toBe(true);
    });

    it('refreshProfile_Unsuspended_ClearsSuspendedFlagAndReopensTeacherFeatures (issue #289)', () => {
        setup(['Teacher'], profile({ teacherAccountApproved: false, teacherAccountSuspended: true }));
        expect(teacherAccountSuspendedOf(service.user())).toBe(true);

        service.refreshProfile().subscribe();
        httpMock.expectOne('/api/exam/auth/refresh').flush(profile({ teacherAccountApproved: true, teacherAccountSuspended: false, teacherApplicationStatus: 'Approved' }));

        expect(teacherAccountSuspendedOf(service.user())).toBe(false);
        expect(service.isUnapprovedTeacher()).toBe(false);
    });

    it('teacherAccountSuspendedOf_MissingField_False (login/exchange profile)', () => {
        // afterEach httpMock.verify() çağırır — rastgele sırada ilk koşan test olsa da TestBed kurulu olmalı.
        setup(['Teacher'], null);
        expect(teacherAccountSuspendedOf(profile({ teacherAccountApproved: false }))).toBe(false);
        expect(teacherAccountSuspendedOf(profile())).toBe(false);
        expect(teacherAccountSuspendedOf(null)).toBe(false);
    });

    it('handleTeacherNotApproved_NavigatesOnceAndRefreshesProfile_EvenForBurstOf403s', () => {
        setup(['Teacher'], profile({}));

        service.handleTeacherNotApproved();
        service.handleTeacherNotApproved();
        service.handleTeacherNotApproved();

        expect(service.isUnapprovedTeacher(), 'menu closes immediately').toBe(true);
        expect(navigateByUrl).toHaveBeenCalledTimes(1);
        expect(navigateByUrl).toHaveBeenCalledWith('/teacher-approval-pending');
        httpMock.expectOne('/api/exam/auth/refresh').flush(profile({ teacherAccountApproved: false }));
        expect(service.isUnapprovedTeacher()).toBe(true);
    });

    it('handleTeacherNotApproved_AlreadyOnPendingPage_DoesNotNavigate', () => {
        setup(['Teacher'], profile({}));
        vi.spyOn(router, 'url', 'get').mockReturnValue('/teacher-approval-pending?x=1');

        service.handleTeacherNotApproved();

        expect(navigateByUrl).not.toHaveBeenCalled();
        httpMock.expectOne('/api/exam/auth/refresh').flush(profile({ teacherAccountApproved: false }));
    });

    it('handleTeacherNotApproved_RefreshSaysApproved_ClearsServerFlag', () => {
        setup(['Teacher'], profile({}));

        service.handleTeacherNotApproved();
        httpMock.expectOne('/api/exam/auth/refresh').flush(profile({ teacherAccountApproved: true }));

        expect(service.isUnapprovedTeacher()).toBe(false);
    });

    it('handleTeacherNotApproved_NonTeacher_Ignored', () => {
        setup(['Student'], null);

        service.handleTeacherNotApproved();

        expect(navigateByUrl).not.toHaveBeenCalled();
        httpMock.expectNone('/api/exam/auth/refresh');
    });
});
