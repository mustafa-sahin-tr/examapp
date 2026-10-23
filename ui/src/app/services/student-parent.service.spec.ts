import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ParentStatusStore } from './parent-status.store';
import { StudentParentService } from './student-parent.service';
import { parentStatusOf } from '../shared/testing/parent-status-testing';

/** Issue #440 / #441: öğrenci veli uçları ve store güncellemesi. */
describe('StudentParentService', () => {
    let service: StudentParentService;
    let store: ParentStatusStore;
    let http: HttpTestingController;

    beforeEach(() => {
        TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr()), provideHttpClientTesting()] });
        service = TestBed.inject(StudentParentService);
        store = TestBed.inject(ParentStatusStore);
        http = TestBed.inject(HttpTestingController);
    });

    afterEach(() => http.verify());

    it('reload_GetsStatusThroughGateway_WritesStore_SharesConcurrentCalls', () => {
        service.reload().subscribe();
        service.reload().subscribe();

        const req = http.expectOne('/api/exam/student-parent/status');
        expect(req.request.method).toBe('GET');
        req.flush(parentStatusOf({ mode: 'Grace', daysLeft: 10 }));

        expect(store.mode()).toBe('Grace');
    });

    it('ensureLoaded_AlreadyLoaded_NoRequest', () => {
        store.set(parentStatusOf({ mode: 'Ok' }));
        let result: unknown;
        service.ensureLoaded().subscribe((s) => (result = s));

        http.expectNone('/api/exam/student-parent/status');
        expect(result).toEqual(expect.objectContaining({ mode: 'Ok' }));
    });

    it('ensureLoaded_Error_ResolvesNull', () => {
        let result: unknown = 'unset';
        service.ensureLoaded().subscribe((s) => (result = s));

        http.expectOne('/api/exam/student-parent/status').flush({}, { status: 500, statusText: 'Server Error' });

        expect(result).toBeNull();
        expect(store.loaded()).toBe(false);
    });

    it('startInvitation_PostsContact_StoresReturnedStatus', () => {
        const payload = { parentFullName: 'Ayşe Yılmaz', parentEmail: 'ayse@example.com', parentPhone: '05321234567' };
        service.startInvitation(payload).subscribe();

        const req = http.expectOne('/api/exam/student-parent/invitation');
        expect(req.request.method).toBe('POST');
        expect(req.request.body).toEqual(payload);
        req.flush({ message: 'ok', status: parentStatusOf({ mode: 'AwaitingActivation' }) });

        expect(store.activationRestricted()).toBe(true);
    });

    it('resend_PostsResend_StoresReturnedStatus', () => {
        service.resend().subscribe();

        const req = http.expectOne('/api/exam/student-parent/invitation/resend');
        expect(req.request.method).toBe('POST');
        req.flush({ message: 'ok', status: parentStatusOf({ mode: 'AwaitingActivation' }) });

        expect(store.mode()).toBe('AwaitingActivation');
    });
});
