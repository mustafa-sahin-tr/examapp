import type { MockedObject } from "vitest";
import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of, throwError } from 'rxjs';

import { LIVE_STATUS_MAX_FAILURES, LIVE_STATUS_POLL_MS, LessonVideoComponent } from './lesson-video.component';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../models/locale';
import { AuthService, UserProfile } from '../../services/auth.service';
import { BookingService } from '../../services/booking.service';
import { JitsiScriptLoaderService } from '../../services/jitsi-script-loader.service';
import lessonVideoTr from '../../../../public/i18n/lesson-video/tr.json';

/** Gerçek sözlük yüklenir; anahtar bozulursa test kırılır (issue #183). */
const translocoTesting = TranslocoTestingModule.forRoot({
    langs: { 'lesson-video/tr': lessonVideoTr },
    translocoConfig: {
        availableLangs: [...SUPPORTED_LOCALE_CODES],
        defaultLang: DEFAULT_LOCALE,
        scopes: { keepCasing: true },
    },
    preloadLangs: true,
});

/** Issue #298: öğretmen askıdayken video-session 409 `TeacherUnavailable`. */
describe('LessonVideoComponent (issue #298 teacher unavailable)', () => {
    let fixture: ComponentFixture<LessonVideoComponent>;
    let bookingService: MockedObject<BookingService>;

    const conflict = (body: unknown) => new HttpErrorResponse({ status: 409, error: body });

    async function render(error: HttpErrorResponse): Promise<HTMLElement> {
        bookingService.getVideoSession.mockReturnValue(throwError(() => error));
        fixture = TestBed.createComponent(LessonVideoComponent);
        fixture.detectChanges();
        await fixture.whenStable();
        fixture.detectChanges();
        return fixture.nativeElement as HTMLElement;
    }

    function buttonLabels(host: HTMLElement): string[] {
        return Array.from(host.querySelectorAll('.lvid__state-actions button')).map((b) => b.textContent?.trim() ?? '');
    }

    beforeEach(async () => {
        bookingService = {
            getVideoSession: vi.fn().mockName("BookingService.getVideoSession"),
            extractError: vi.fn().mockName("BookingService.extractError")
        } as unknown as MockedObject<BookingService>;
        bookingService.extractError.mockImplementation((err: HttpErrorResponse, fallback: string) => {
            const message = (err.error as {
                message?: string;
            } | null)?.message;
            return message || fallback;
        });

        await TestBed.configureTestingModule({
            imports: [LessonVideoComponent, translocoTesting],
            providers: [
                provideRouter([]),
                { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ bookingId: '42' }) } } },
                { provide: BookingService, useValue: bookingService },
                { provide: JitsiScriptLoaderService, useValue: { load: () => Promise.reject(new Error('unused')) } },
                { provide: AuthService, useValue: { getUserRole: () => 'Student' } },
            ],
        }).compileComponents();
    });

    it('teacherUnavailable_WithServerMessage_ShowsServerMessageWithoutRetry', async () => {
        const host = await render(conflict({ success: false, conflict: true, errorCode: 'TeacherUnavailable', message: 'Sunucu mesajı' }));

        const alert = host.querySelector('[role="alert"]');
        expect(alert?.textContent).toContain('Sunucu mesajı');
        expect(alert?.querySelector('mat-icon')?.textContent?.trim()).toBe('event_busy');
        expect(buttonLabels(host)).toEqual([lessonVideoTr.goBack]);
        expect(bookingService.getVideoSession).toHaveBeenCalledTimes(1);
    });

    it('teacherUnavailable_WithoutMessage_FallsBackToLocalTranslation', async () => {
        const host = await render(conflict({ success: false, conflict: true, errorCode: 'TeacherUnavailable', message: '  ' }));

        expect(host.querySelector('[role="alert"]')?.textContent).toContain(lessonVideoTr.error.teacherUnavailable);
        expect(buttonLabels(host)).toEqual([lessonVideoTr.goBack]);
        expect(bookingService.getVideoSession).toHaveBeenCalledTimes(1);
    });

    it('otherConflict_KeepsRetryButton', async () => {
        const host = await render(conflict({ success: false, conflict: true, message: 'Katılım penceresi dışında' }));

        const alert = host.querySelector('[role="alert"]');
        expect(alert?.textContent).toContain('Katılım penceresi dışında');
        expect(alert?.querySelector('mat-icon')?.textContent?.trim()).toBe('videocam_off');
        expect(buttonLabels(host)).toEqual([lessonVideoTr.retry, lessonVideoTr.goBack]);
    });

    it('teacherUnavailableCodeOnNon409_TreatedAsGenericError', async () => {
        const host = await render(new HttpErrorResponse({ status: 403, error: { success: false, errorCode: 'TeacherUnavailable', message: 'Yasak' } }));

        expect(buttonLabels(host)).toEqual([lessonVideoTr.retry, lessonVideoTr.goBack]);
    });
});

/** Issue #418: "Dersten ayrıl" — `/booking-requests` yalnız bağımsız (ya da bayrağı bilinmeyen) öğretmene. */
describe('LessonVideoComponent leave target (issue #418)', () => {
    function teacher(isIndependentTutor: boolean | undefined): UserProfile {
        return {
            email: '',
            avatar: '',
            fullName: '',
            id: 1,
            keycloakId: 'k',
            profileId: 1,
            role: 'Teacher',
            teacher: { id: 1, userId: 1, schoolName: '', schoolId: 7, isIndependentTutor },
        } as UserProfile;
    }

    async function leaveAs(role: string, user: UserProfile | null): Promise<string> {
        const bookingService = {
            getVideoSession: vi.fn().mockName("BookingService.getVideoSession"),
            extractError: vi.fn().mockName("BookingService.extractError")
        };
        bookingService.getVideoSession.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
        bookingService.extractError.mockReturnValue('x');
        await TestBed.configureTestingModule({
            imports: [LessonVideoComponent, translocoTesting],
            providers: [
                provideRouter([]),
                { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ bookingId: '42' }) } } },
                { provide: BookingService, useValue: bookingService },
                { provide: JitsiScriptLoaderService, useValue: { load: () => Promise.reject(new Error('unused')) } },
                { provide: AuthService, useValue: { getUserRole: () => role, user: signal(user) } },
            ],
        }).compileComponents();
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
        const fixture = TestBed.createComponent(LessonVideoComponent);
        fixture.detectChanges();
        await fixture.whenStable();
        fixture.componentInstance['leave']();
        return vi.mocked(navigate).mock.lastCall![0] as string;
    }

    it('NotIndependentTeacher_LeavesToMyCalendar', async () => {
        expect(await leaveAs('Teacher', teacher(false))).toBe('/my-calendar');
    });

    it('IndependentTeacher_LeavesToBookingRequests', async () => {
        expect(await leaveAs('Teacher', teacher(true))).toBe('/booking-requests');
    });

    it('UnknownFlagTeacher_LeavesToBookingRequests', async () => {
        expect(await leaveAs('Teacher', null)).toBe('/booking-requests');
    });

    it('Student_LeavesToMyBookings', async () => {
        expect(await leaveAs('Student', null)).toBe('/my-bookings');
    });
});

/** Issue #320: açık görüşme periyodik yoklanır; askı / iptal / yetki kaybında Jitsi kapanır. */
describe('LessonVideoComponent live status poll (issue #320)', () => {
    beforeEach(() => {
        vi.useFakeTimers();
    });
    afterEach(() => {
        vi.useRealTimers();
    });
    let fixture: ComponentFixture<LessonVideoComponent>;
    let bookingService: MockedObject<BookingService>;

    const session = {
        provider: 'Jitsi',
        roomName: 'room-42',
        domain: 'localhost:8000',
        baseUrl: 'http://localhost:8000',
        joinUrl: 'http://localhost:8000/room-42?jwt=x',
        token: 'x',
        expiresAt: '2030-01-01T00:00:00Z',
        isModerator: false,
    };

    beforeEach(async () => {
        bookingService = {
            getVideoSession: vi.fn().mockName("BookingService.getVideoSession"),
            getLiveSessionStatus: vi.fn().mockName("BookingService.getLiveSessionStatus"),
            extractError: vi.fn().mockName("BookingService.extractError")
        } as unknown as MockedObject<BookingService>;
        bookingService.getVideoSession.mockReturnValue(of({ success: true, session }));
        bookingService.extractError.mockImplementation((_: HttpErrorResponse, fallback: string) => fallback);

        await TestBed.configureTestingModule({
            imports: [LessonVideoComponent, translocoTesting],
            providers: [
                provideRouter([]),
                { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ bookingId: '42' }) } } },
                { provide: BookingService, useValue: bookingService },
                { provide: JitsiScriptLoaderService, useValue: { load: () => Promise.reject(new Error('unused')) } },
                { provide: AuthService, useValue: { getUserRole: () => 'Student' } },
            ],
        }).compileComponents();
    });

    async function open(): Promise<HTMLElement> {
        fixture = TestBed.createComponent(LessonVideoComponent);
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);
        fixture.detectChanges();
        return fixture.nativeElement as HTMLElement;
    }

    async function poll(host: HTMLElement): Promise<void> {
        await vi.advanceTimersByTimeAsync(LIVE_STATUS_POLL_MS);
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);
        fixture.detectChanges();
        host.querySelector('iframe'); // render tamam
    }

    function buttonLabels(host: HTMLElement): string[] {
        return Array.from(host.querySelectorAll('.lvid__state-actions button')).map((b) => b.textContent?.trim() ?? '');
    }

    it('TeacherSuspendedDuringCall_ClosesRoomAndShowsTeacherUnavailable', async () => {
        bookingService.getLiveSessionStatus.mockReturnValue(of({ success: true, active: false, errorCode: 'TeacherUnavailable' }));
        const host = await open();
        expect(host.querySelector('iframe')).not.toBeNull();

        await poll(host);

        expect(bookingService.getLiveSessionStatus).toHaveBeenCalledTimes(1);

        expect(bookingService.getLiveSessionStatus).toHaveBeenCalledWith(42);
        expect(host.querySelector('iframe')).toBeNull();
        expect(host.querySelector('[role="alert"]')?.textContent).toContain(lessonVideoTr.error.teacherUnavailable);
        expect(buttonLabels(host)).toEqual([lessonVideoTr.goBack]);

        // Yoklama durdu.
        await vi.advanceTimersByTimeAsync(LIVE_STATUS_POLL_MS * 3);
        expect(bookingService.getLiveSessionStatus).toHaveBeenCalledTimes(1);
    });

    it('BookingCancelledDuringCall_ClosesRoomWithCancelledMessage', async () => {
        bookingService.getLiveSessionStatus.mockReturnValue(of({ success: true, active: false, errorCode: 'BookingNotApproved' }));
        const host = await open();

        await poll(host);

        expect(host.querySelector('iframe')).toBeNull();
        expect(host.querySelector('[role="alert"]')?.textContent).toContain(lessonVideoTr.error.bookingCancelled);
        expect(buttonLabels(host)).toEqual([lessonVideoTr.goBack]);
    });

    for (const status of [403, 404]) {
        it(`AccessLost${status}_ClosesRoomAndStopsPolling`, async () => {
            bookingService.getLiveSessionStatus.mockReturnValue(throwError(() => new HttpErrorResponse({ status })));
            const host = await open();

            await poll(host);

            expect(host.querySelector('iframe')).toBeNull();
            expect(host.querySelector('[role="alert"]')?.textContent).toContain(lessonVideoTr.error.sessionEnded);
            expect(buttonLabels(host)).toEqual([lessonVideoTr.goBack]);
            await vi.advanceTimersByTimeAsync(LIVE_STATUS_POLL_MS * 3);
            expect(bookingService.getLiveSessionStatus).toHaveBeenCalledTimes(1);
        });
    }

    it('WindowClosedDuringCall_ShowsWindowClosedMessage', async () => {
        bookingService.getLiveSessionStatus.mockReturnValue(of({ success: true, active: false, errorCode: 'WindowClosed' }));
        const host = await open();

        await poll(host);

        expect(host.querySelector('iframe')).toBeNull();
        expect(host.querySelector('[role="alert"]')?.textContent).toContain(lessonVideoTr.error.windowClosed);
    });

    it('RepeatedTransientFailures_CloseRoomButKeepRetry', async () => {
        bookingService.getLiveSessionStatus.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 502 })));
        const host = await open();

        for (let i = 1; i < LIVE_STATUS_MAX_FAILURES; i++) {
            await poll(host);
            expect(host.querySelector('iframe'), `hata ${i}`).not.toBeNull();
        }
        await poll(host);

        expect(host.querySelector('iframe')).toBeNull();
        expect(host.querySelector('[role="alert"]')?.textContent).toContain(lessonVideoTr.error.statusUnverified);
        expect(buttonLabels(host)).toContain(lessonVideoTr.retry);
    });

    it('Unauthorized401_ClosesRoomButKeepsRetry', async () => {
        bookingService.getLiveSessionStatus.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 401 })));
        const host = await open();

        await poll(host);

        expect(host.querySelector('iframe')).toBeNull();
        expect(buttonLabels(host)).toContain(lessonVideoTr.retry);
    });

    it('ActiveOrTransientError_KeepsRoomAndKeepsPolling', async () => {
        let calls = 0;
        bookingService.getLiveSessionStatus.mockImplementation(() => ++calls === 1 ? throwError(() => new HttpErrorResponse({ status: 502 })) : of({ success: true, active: true }));
        const host = await open();

        await poll(host);
        await poll(host);

        expect(bookingService.getLiveSessionStatus).toHaveBeenCalledTimes(2);
        expect(host.querySelector('iframe')).not.toBeNull();
        fixture.destroy();
    });
});
