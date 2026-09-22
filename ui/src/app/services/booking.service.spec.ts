import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';

import { BookingService } from './booking.service';
import {
  CreateAvailabilitySlotRequest,
  CreateBookingRequest,
  CreateRecurringRuleRequest,
  AvailabilitySlot,
  Booking,
  RecurringAvailabilityRule,
  VideoSession,
} from '../models/booking.model';
import { translocoTestingModule } from '../shared/testing/transloco-testing';

describe('BookingService', () => {
  let service: BookingService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [translocoTestingModule(), HttpClientTestingModule],
      providers: [BookingService],
    });

    service = TestBed.inject(BookingService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  describe('createSlot', () => {
    it('createSlot_ValidRequest_SendsPostWithCorrectPayload', () => {
      const req: CreateAvailabilitySlotRequest = {
        date: '2026-09-20',
        startTime: '14:00:00',
        endTime: '15:00:00',
      };

      service.createSlot(req).subscribe();

      const httpReq = httpMock.expectOne(`${service['baseUrl']}/slots`);
      expect(httpReq.request.method).toBe('POST');
      expect(httpReq.request.body).toEqual(req);
      httpReq.flush({ success: true, slot: { id: 1, teacherId: 10 } as AvailabilitySlot });
    });

    it('createSlot_ApiReturnsSuccess_IncludesSlotInResult', (done) => {
      const req: CreateAvailabilitySlotRequest = {
        date: '2026-09-20',
        startTime: '14:00:00',
        endTime: '15:00:00',
      };

      const mockSlot: AvailabilitySlot = {
        id: 1,
        teacherId: 10,
        date: '2026-09-20',
        startTime: '14:00:00',
        endTime: '15:00:00',
        createdAt: new Date().toISOString(),
        startUtc: new Date().toISOString(),
        endUtc: new Date().toISOString(),
        isBooked: false,
      };

      service.createSlot(req).subscribe((result) => {
        expect(result.success).toBeTrue();
        expect(result.slot).toEqual(mockSlot);
        done();
      });

      const httpReq = httpMock.expectOne(`${service['baseUrl']}/slots`);
      httpReq.flush({ success: true, slot: mockSlot });
    });

    it('createSlot_ApiReturns409Conflict_ErrorPropagates', (done) => {
      const req: CreateAvailabilitySlotRequest = {
        date: '2026-09-20',
        startTime: '14:00:00',
        endTime: '15:00:00',
      };

      service.createSlot(req).subscribe({
        next: () => fail('hata beklenirken next yayildi'),
        error: (error: HttpErrorResponse) => {
          expect(error.status).toBe(409);
          expect(error.error.conflict).toBeTrue();
          expect(error.error.message).toBe('Time slot overlaps');
          done();
        },
      });

      const httpReq = httpMock.expectOne(`${service['baseUrl']}/slots`);
      httpReq.flush(
        { success: false, conflict: true, message: 'Time slot overlaps' },
        { status: 409, statusText: 'Conflict' }
      );
    });
  });

  describe('deleteSlot', () => {
    it('deleteSlot_ValidId_SendsDeleteRequest', () => {
      const slotId = 1;

      service.deleteSlot(slotId).subscribe();

      const httpReq = httpMock.expectOne(`${service['baseUrl']}/slots/${slotId}`);
      expect(httpReq.request.method).toBe('DELETE');
      httpReq.flush(null);
    });

    it('deleteSlot_ApiReturns403Forbidden_ErrorPropagates', (done) => {
      const slotId = 1;

      service.deleteSlot(slotId).subscribe({
        next: () => fail('hata beklenirken next yayildi'),
        error: (error: HttpErrorResponse) => {
          expect(error.status).toBe(403);
          done();
        },
      });

      const httpReq = httpMock.expectOne(`${service['baseUrl']}/slots/${slotId}`);
      httpReq.flush(
        { success: false, forbidden: true, message: 'Not your slot' },
        { status: 403, statusText: 'Forbidden' }
      );
    });
  });

  describe('recurring rules (issue #178/#179)', () => {
    const ruleRequest: CreateRecurringRuleRequest = {
      dayOfWeek: 5,
      startTime: '11:00:00',
      endTime: '12:30:00',
      effectiveFrom: '2026-09-25',
      effectiveUntil: null,
    };
    const rule: RecurringAvailabilityRule = {
      id: 5,
      teacherId: 10,
      dayOfWeek: 5,
      startTime: '11:00:00',
      endTime: '12:30:00',
      effectiveFrom: '2026-09-25',
      effectiveUntil: null,
      isActive: true,
      createdAt: '2026-09-22T09:00:00Z',
    };

    it('createRecurringRule_SendsPostWithBodyAsIs_AndReturnsRuleGeneratedAndSkipped', (done) => {
      service.createRecurringRule(ruleRequest).subscribe((result) => {
        expect(result.success).toBeTrue();
        expect(result.rule).toEqual(rule);
        expect(result.generatedSlotIds).toEqual([101, 102, 103]);
        expect(result.skippedDates).toEqual(['2026-10-09']);
        done();
      });

      const httpReq = httpMock.expectOne(`${service['baseUrl']}/recurring-rules`);
      expect(httpReq.request.method).toBe('POST');
      // Gövde dönüştürülmeden gider: gün/saat/tarih UTC bileşenleri istemcide zaten hesaplanmıştır.
      expect(httpReq.request.body).toEqual(ruleRequest);
      httpReq.flush(
        { success: true, objectId: 5, rule, generatedSlotIds: [101, 102, 103], skippedDates: ['2026-10-09'] },
        { status: 201, statusText: 'Created' }
      );
    });

    it('createRecurringRule_EffectiveUntilGiven_SendsDateString', () => {
      service.createRecurringRule({ ...ruleRequest, effectiveUntil: '2026-12-25' }).subscribe();

      const httpReq = httpMock.expectOne(`${service['baseUrl']}/recurring-rules`);
      expect(httpReq.request.body.effectiveUntil).toBe('2026-12-25');
      httpReq.flush({ success: true, rule, generatedSlotIds: [], skippedDates: [] });
    });

    it('createRecurringRule_ApiReturns409OverlappingRule_ErrorPropagates', (done) => {
      service.createRecurringRule(ruleRequest).subscribe({
        next: () => fail('hata beklenirken next yayildi'),
        error: (error: HttpErrorResponse) => {
          expect(error.status).toBe(409);
          expect(error.error.conflict).toBeTrue();
          done();
        },
      });

      httpMock
        .expectOne(`${service['baseUrl']}/recurring-rules`)
        .flush({ success: false, conflict: true, message: 'Overlapping rule' }, { status: 409, statusText: 'Conflict' });
    });

    it('createRecurringRule_ApiReturns400Validation_ErrorPropagatesWithMessage', (done) => {
      service.createRecurringRule(ruleRequest).subscribe({
        next: () => fail('hata beklenirken next yayildi'),
        error: (error: HttpErrorResponse) => {
          expect(error.status).toBe(400);
          expect(error.error.message).toBe('Rule must be at least 30 minutes');
          done();
        },
      });

      httpMock
        .expectOne(`${service['baseUrl']}/recurring-rules`)
        .flush({ success: false, message: 'Rule must be at least 30 minutes' }, { status: 400, statusText: 'Bad Request' });
    });

    it('deleteRecurringRule_SendsDelete_AndReturnsDeletedPreservedCounts', (done) => {
      service.deleteRecurringRule(5).subscribe((result) => {
        expect(result.success).toBeTrue();
        expect(result.deletedSlotIds).toEqual([101, 103]);
        expect(result.preservedSlotIds).toEqual([102]);
        expect(result.preservedBookedCount).toBe(1);
        done();
      });

      const httpReq = httpMock.expectOne(`${service['baseUrl']}/recurring-rules/5`);
      expect(httpReq.request.method).toBe('DELETE');
      httpReq.flush({
        success: true,
        objectId: 5,
        deletedSlotIds: [101, 103],
        preservedSlotIds: [102],
        preservedBookedCount: 1,
      });
    });

    it('deleteRecurringRule_ApiReturns404_ErrorPropagates', (done) => {
      service.deleteRecurringRule(99).subscribe({
        next: () => fail('hata beklenirken next yayildi'),
        error: (error: HttpErrorResponse) => {
          expect(error.status).toBe(404);
          expect(error.error.notFound).toBeTrue();
          done();
        },
      });

      httpMock
        .expectOne(`${service['baseUrl']}/recurring-rules/99`)
        .flush({ success: false, notFound: true, message: 'Rule not found' }, { status: 404, statusText: 'Not Found' });
    });
  });

  describe('getMySlots', () => {
    it('getMySlots_NoParams_UseDefaultSkipAndTake', () => {
      service.getMySlots().subscribe();

      const httpReq = httpMock.expectOne(
        (req) =>
          req.url === `${service['baseUrl']}/slots/mine` &&
          req.params.get('skip') === '0' &&
          req.params.get('take') === '100'
      );
      expect(httpReq.request.method).toBe('GET');
      httpReq.flush({ success: true, items: [] });
    });

    it('getMySlots_WithParams_IncludesSkipAndTake', () => {
      service.getMySlots(10, 50).subscribe();

      const httpReq = httpMock.expectOne(
        (req) =>
          req.url === `${service['baseUrl']}/slots/mine` &&
          req.params.get('skip') === '10' &&
          req.params.get('take') === '50'
      );
      expect(httpReq.request.method).toBe('GET');
      httpReq.flush({ success: true, items: [] });
    });
  });

  describe('getTeacherSlots', () => {
    it('getTeacherSlots_ValidTeacherId_FetchesOpenSlots', (done) => {
      const teacherId = 10;

      const mockSlot: AvailabilitySlot = {
        id: 1,
        teacherId,
        date: '2026-09-20',
        startTime: '14:00:00',
        endTime: '15:00:00',
        createdAt: new Date().toISOString(),
        startUtc: new Date().toISOString(),
        endUtc: new Date().toISOString(),
        isBooked: false,
      };

      service.getTeacherSlots(teacherId).subscribe((result) => {
        expect(result.success).toBeTrue();
        expect(result.items.length).toBeGreaterThan(0);
        expect(result.items[0]).toEqual(mockSlot);
        done();
      });

      const httpReq = httpMock.expectOne(
        (req) => req.url === `${service['baseUrl']}/teachers/${teacherId}/slots`
      );
      expect(httpReq.request.method).toBe('GET');
      httpReq.flush({ success: true, items: [mockSlot] });
    });

    it('getTeacherSlots_ApiReturns404_ErrorPropagates', (done) => {
      const teacherId = 999;

      service.getTeacherSlots(teacherId).subscribe({
        next: () => fail('hata beklenirken next yayildi'),
        error: (error: HttpErrorResponse) => {
          expect(error.status).toBe(404);
          expect(error.error.notFound).toBeTrue();
          expect(error.error.message).toBe('Teacher not found');
          done();
        },
      });

      const httpReq = httpMock.expectOne(
        (req) => req.url === `${service['baseUrl']}/teachers/${teacherId}/slots`
      );
      httpReq.flush(
        { success: false, notFound: true, message: 'Teacher not found' },
        { status: 404, statusText: 'Not Found' }
      );
    });
  });

  describe('createBooking', () => {
    it('createBooking_ValidSlotId_CreatesBookingRequest', () => {
      const req: CreateBookingRequest = { availabilitySlotId: 1 };

      service.createBooking(req).subscribe();

      const httpReq = httpMock.expectOne(`${service['baseUrl']}/requests`);
      expect(httpReq.request.method).toBe('POST');
      expect(httpReq.request.body).toEqual(req);
      httpReq.flush({ success: true });
    });

    it('createBooking_ApiReturnsSuccess_IncludesBookingInResult', (done) => {
      const req: CreateBookingRequest = { availabilitySlotId: 1 };
      const mockBooking: Booking = {
        id: 1,
        teacherId: 10,
        studentId: 20,
        availabilitySlotId: 1,
        date: '2026-09-20',
        startTime: '14:00:00',
        endTime: '15:00:00',
        startUtc: new Date().toISOString(),
        endUtc: new Date().toISOString(),
        status: 'Pending',
        createdAt: new Date().toISOString(),
      };

      service.createBooking(req).subscribe((result) => {
        expect(result.success).toBeTrue();
        expect(result.booking).toEqual(mockBooking);
        done();
      });

      const httpReq = httpMock.expectOne(`${service['baseUrl']}/requests`);
      httpReq.flush({ success: true, booking: mockBooking });
    });

    it('createBooking_ApiReturns409Conflict_ErrorPropagates', (done) => {
      const req: CreateBookingRequest = { availabilitySlotId: 1 };

      service.createBooking(req).subscribe({
        next: () => fail('hata beklenirken next yayildi'),
        error: (error: HttpErrorResponse) => {
          expect(error.status).toBe(409);
          expect(error.error.conflict).toBeTrue();
          expect(error.error.message).toBe('Slot already booked');
          done();
        },
      });

      const httpReq = httpMock.expectOne(`${service['baseUrl']}/requests`);
      httpReq.flush(
        { success: false, conflict: true, message: 'Slot already booked' },
        { status: 409, statusText: 'Conflict' }
      );
    });
  });

  describe('getTeacherRequests', () => {
    it('getTeacherRequests_NoParams_UseDefaults', () => {
      service.getTeacherRequests().subscribe();

      const httpReq = httpMock.expectOne(
        (req) =>
          req.url === `${service['baseUrl']}/requests/teacher` &&
          req.params.get('skip') === '0' &&
          req.params.get('take') === '100'
      );
      expect(httpReq.request.method).toBe('GET');
      httpReq.flush({ success: true, items: [] });
    });
  });

  describe('getStudentRequests', () => {
    it('getStudentRequests_NoParams_UseDefaults', () => {
      service.getStudentRequests().subscribe();

      const httpReq = httpMock.expectOne(
        (req) =>
          req.url === `${service['baseUrl']}/requests/student` &&
          req.params.get('skip') === '0' &&
          req.params.get('take') === '100'
      );
      expect(httpReq.request.method).toBe('GET');
      httpReq.flush({ success: true, items: [] });
    });
  });

  describe('approveBooking', () => {
    it('approveBooking_ValidId_SendsPostWithNull', () => {
      const bookingId = 1;

      service.approveBooking(bookingId).subscribe();

      const httpReq = httpMock.expectOne(`${service['baseUrl']}/requests/${bookingId}/approve`);
      expect(httpReq.request.method).toBe('POST');
      expect(httpReq.request.body).toBeNull();
      httpReq.flush({ success: true });
    });

    it('approveBooking_ApiReturns403_FailsWithForbidden', (done) => {
      const bookingId = 1;

      service.approveBooking(bookingId).subscribe({
        next: () => fail('hata beklenirken next yayildi'),
        error: (error: HttpErrorResponse) => {
          expect(error.status).toBe(403);
          done();
        },
      });

      const httpReq = httpMock.expectOne(`${service['baseUrl']}/requests/${bookingId}/approve`);
      httpReq.flush(
        { success: false, forbidden: true, message: 'Not your booking' },
        { status: 403, statusText: 'Forbidden' }
      );
    });
  });

  describe('rejectBooking', () => {
    it('rejectBooking_WithReason_SendsRequestWithReason', () => {
      const bookingId = 1;
      const reason = 'Time conflict';

      service.rejectBooking(bookingId, reason).subscribe();

      const httpReq = httpMock.expectOne(`${service['baseUrl']}/requests/${bookingId}/reject`);
      expect(httpReq.request.method).toBe('POST');
      expect(httpReq.request.body).toEqual({ rejectionReason: reason });
      httpReq.flush({ success: true });
    });

    it('rejectBooking_WithoutReason_SendsNullReason', () => {
      const bookingId = 1;

      service.rejectBooking(bookingId).subscribe();

      const httpReq = httpMock.expectOne(`${service['baseUrl']}/requests/${bookingId}/reject`);
      expect(httpReq.request.body).toEqual({ rejectionReason: null });
      httpReq.flush({ success: true });
    });

    it('rejectBooking_WithWhitespaceReason_TrimsAndSends', () => {
      const bookingId = 1;

      service.rejectBooking(bookingId, '  reason  ').subscribe();

      const httpReq = httpMock.expectOne(`${service['baseUrl']}/requests/${bookingId}/reject`);
      expect(httpReq.request.body).toEqual({ rejectionReason: 'reason' });
      httpReq.flush({ success: true });
    });
  });

  describe('getVideoSession', () => {
    it('getVideoSession_ValidId_SendsPostWithNull', () => {
      const bookingId = 12;

      service.getVideoSession(bookingId).subscribe();

      const httpReq = httpMock.expectOne(`${service['baseUrl']}/requests/${bookingId}/video-session`);
      expect(httpReq.request.method).toBe('POST');
      expect(httpReq.request.body).toBeNull();
      httpReq.flush({ success: true });
    });

    it('getVideoSession_ApiReturnsSuccess_IncludesSessionInResult', (done) => {
      const bookingId = 12;
      const mockSession: VideoSession = {
        provider: 'Jitsi',
        roomName: 'booking-12-a1b2c3d4e5f6',
        domain: 'localhost:8000',
        baseUrl: 'http://localhost:8000',
        joinUrl: 'http://localhost:8000/booking-12-a1b2c3d4e5f6?jwt=token',
        token: 'token',
        expiresAt: new Date().toISOString(),
        isModerator: true,
      };

      service.getVideoSession(bookingId).subscribe((result) => {
        expect(result.success).toBeTrue();
        expect(result.session).toEqual(mockSession);
        done();
      });

      httpMock
        .expectOne(`${service['baseUrl']}/requests/${bookingId}/video-session`)
        .flush({ success: true, objectId: bookingId, session: mockSession });
    });

    it('getVideoSession_ApiReturns409OutsideJoinWindow_ErrorCarriesMessage', (done) => {
      const bookingId = 12;

      service.getVideoSession(bookingId).subscribe({
        next: () => fail('hata beklenirken next yayildi'),
        error: (error: HttpErrorResponse) => {
          expect(error.status).toBe(409);
          expect(service.extractError(error, 'fallback')).toContain('katılabilirsiniz');
          done();
        },
      });

      httpMock.expectOne(`${service['baseUrl']}/requests/${bookingId}/video-session`).flush(
        {
          success: false,
          conflict: true,
          message: 'Görüşmeye ders saatinden en erken 15 dakika önce katılabilirsiniz.',
        },
        { status: 409, statusText: 'Conflict' }
      );
    });
  });

  describe('extractError', () => {
    it('extractError_WithMessageInBody_ReturnsMessage', () => {
      const error = new HttpErrorResponse({
        status: 400,
        error: { success: false, message: 'Invalid input' },
      });

      const result = service.extractError(error, 'fallback');

      expect(result).toBe('Invalid input');
    });

    it('extractError_409WithoutMessage_ReturnsFallback409Message', () => {
      const error = new HttpErrorResponse({
        status: 409,
        error: { success: false },
      });

      const result = service.extractError(error, 'fallback');

      expect(result).toContain('zaman aralığı');
    });

    it('extractError_403WithoutMessage_ReturnsFallback403Message', () => {
      const error = new HttpErrorResponse({
        status: 403,
        error: { success: false },
      });

      const result = service.extractError(error, 'fallback');

      expect(result).toContain('yetkiniz');
    });

    it('extractError_404WithoutMessage_ReturnsFallback404Message', () => {
      const error = new HttpErrorResponse({
        status: 404,
        error: { success: false },
      });

      const result = service.extractError(error, 'fallback');

      expect(result).toContain('bulunamadı');
    });

    it('extractError_NoMessage_ReturnsProvidedFallback', () => {
      const error = new HttpErrorResponse({
        status: 500,
        error: { success: false },
      });

      const result = service.extractError(error, 'custom fallback');

      expect(result).toBe('custom fallback');
    });
  });

  describe('getAllMySlots', () => {
    it('getAllMySlots_SinglePageLessThan200_MakesEmptyPageRequest', (done) => {
      const mockSlots: AvailabilitySlot[] = [
        {
          id: 1,
          teacherId: 10,
          date: '2026-09-20',
          startTime: '14:00:00',
          endTime: '15:00:00',
          createdAt: '2026-09-15T10:00:00Z',
          startUtc: '2026-09-20T12:00:00Z',
          endUtc: '2026-09-20T13:00:00Z',
          isBooked: false,
        },
      ];

      service.getAllMySlots().subscribe((result) => {
        expect(result.items).toEqual(mockSlots);
        expect(result.success).toBeTrue();
        done();
      });

      const httpReq1 = httpMock.expectOne(
        (req) =>
          req.url === `${service['baseUrl']}/slots/mine` &&
          req.params.get('skip') === '0' &&
          req.params.get('take') === '200'
      );
      httpReq1.flush({ success: true, items: mockSlots });

      // New behavior: service makes a second request with cumulative skip to confirm end of list
      const httpReq2 = httpMock.expectOne(
        (req) =>
          req.url === `${service['baseUrl']}/slots/mine` &&
          req.params.get('skip') === '1' &&
          req.params.get('take') === '200'
      );
      httpReq2.flush({ success: true, items: [] });
    });

    it('getAllMySlots_ExactlyTwoHundredSlots_FetchesThirdEmpty', (done) => {
      const mockSlots1: AvailabilitySlot[] = Array.from({ length: 200 }, (_, i) => ({
        id: i + 1,
        teacherId: 10,
        date: '2026-09-20',
        startTime: '14:00:00',
        endTime: '15:00:00',
        createdAt: '2026-09-15T10:00:00Z',
        startUtc: '2026-09-20T12:00:00Z',
        endUtc: '2026-09-20T13:00:00Z',
        isBooked: false,
      }));

      const mockSlots2: AvailabilitySlot[] = [
        {
          id: 201,
          teacherId: 10,
          date: '2026-09-20',
          startTime: '14:00:00',
          endTime: '15:00:00',
          createdAt: '2026-09-15T10:00:00Z',
          startUtc: '2026-09-20T12:00:00Z',
          endUtc: '2026-09-20T13:00:00Z',
          isBooked: false,
        },
      ];

      service.getAllMySlots().subscribe((result) => {
        expect(result.items.length).toBe(201);
        expect(result.success).toBeTrue();
        done();
      });

      const httpReq1 = httpMock.expectOne(
        (req) =>
          req.url === `${service['baseUrl']}/slots/mine` &&
          req.params.get('skip') === '0' &&
          req.params.get('take') === '200'
      );
      httpReq1.flush({ success: true, items: mockSlots1 });

      const httpReq2 = httpMock.expectOne(
        (req) =>
          req.url === `${service['baseUrl']}/slots/mine` &&
          req.params.get('skip') === '200' &&
          req.params.get('take') === '200'
      );
      httpReq2.flush({ success: true, items: mockSlots2 });

      // Page 2 had 1 item (< 200), so service fetches page 3 with cumulative skip
      const httpReq3 = httpMock.expectOne(
        (req) =>
          req.url === `${service['baseUrl']}/slots/mine` &&
          req.params.get('skip') === '201' &&
          req.params.get('take') === '200'
      );
      httpReq3.flush({ success: true, items: [] });
    });

    it('getAllMySlots_ThreePages_AccumulativeSkipWithEmptyEnd', (done) => {
      const makePage = (startId: number, count: number): AvailabilitySlot[] =>
        Array.from({ length: count }, (_, i) => ({
          id: startId + i,
          teacherId: 10,
          date: '2026-09-20',
          startTime: '14:00:00',
          endTime: '15:00:00',
          createdAt: '2026-09-15T10:00:00Z',
          startUtc: '2026-09-20T12:00:00Z',
          endUtc: '2026-09-20T13:00:00Z',
          isBooked: false,
        }));

      const page1 = makePage(1, 200);
      const page2 = makePage(201, 200);
      const page3 = makePage(401, 50);

      service.getAllMySlots().subscribe((result) => {
        expect(result.items.length).toBe(450);
        expect(result.success).toBeTrue();
        done();
      });

      const httpReq1 = httpMock.expectOne(
        (req) =>
          req.url === `${service['baseUrl']}/slots/mine` &&
          req.params.get('skip') === '0' &&
          req.params.get('take') === '200'
      );
      httpReq1.flush({ success: true, items: page1 });

      const httpReq2 = httpMock.expectOne(
        (req) =>
          req.url === `${service['baseUrl']}/slots/mine` &&
          req.params.get('skip') === '200' &&
          req.params.get('take') === '200'
      );
      httpReq2.flush({ success: true, items: page2 });

      const httpReq3 = httpMock.expectOne(
        (req) =>
          req.url === `${service['baseUrl']}/slots/mine` &&
          req.params.get('skip') === '400' &&
          req.params.get('take') === '200'
      );
      httpReq3.flush({ success: true, items: page3 });

      // Page 3 had 50 items (< 200), so service fetches page 4 with cumulative skip
      const httpReq4 = httpMock.expectOne(
        (req) =>
          req.url === `${service['baseUrl']}/slots/mine` &&
          req.params.get('skip') === '450' &&
          req.params.get('take') === '200'
      );
      httpReq4.flush({ success: true, items: [] });
    });

    it('getAllMySlots_DuplicateIds_DeduplicatesByIdKeepingFirst', (done) => {
      const slot1: AvailabilitySlot = {
        id: 1,
        teacherId: 10,
        date: '2026-09-20',
        startTime: '14:00:00',
        endTime: '15:00:00',
        createdAt: '2026-09-15T10:00:00Z',
        startUtc: '2026-09-20T12:00:00Z',
        endUtc: '2026-09-20T13:00:00Z',
        isBooked: false,
      };

      const slot1Duplicate: AvailabilitySlot = {
        id: 1,
        teacherId: 10,
        date: '2026-09-20',
        startTime: '15:00:00',
        endTime: '16:00:00',
        createdAt: '2026-09-15T10:00:00Z',
        startUtc: '2026-09-20T13:00:00Z',
        endUtc: '2026-09-20T14:00:00Z',
        isBooked: true,
      };

      const page1 = Array(200).fill(null).map((_, i) => ({ ...slot1, id: i + 1 }));
      const page2 = [slot1Duplicate]; // Same id as first item in page1

      service.getAllMySlots().subscribe((result) => {
        const slot1Item = result.items.find((s) => s.id === 1);
        expect(slot1Item?.startTime).toBe('14:00:00');
        expect(result.items.length).toBe(200);
        done();
      });

      const httpReq1 = httpMock.expectOne(
        (req) =>
          req.url === `${service['baseUrl']}/slots/mine` &&
          req.params.get('skip') === '0'
      );
      httpReq1.flush({ success: true, items: page1 });

      const httpReq2 = httpMock.expectOne(
        (req) =>
          req.url === `${service['baseUrl']}/slots/mine` &&
          req.params.get('skip') === '200'
      );
      httpReq2.flush({ success: true, items: page2 });

      // Page 2 had 1 item (< 200), so service fetches page 3 with cumulative skip
      const httpReq3 = httpMock.expectOne(
        (req) =>
          req.url === `${service['baseUrl']}/slots/mine` &&
          req.params.get('skip') === '201'
      );
      httpReq3.flush({ success: true, items: [] });
    });

    it('getAllMySlots_MaxPagesReached_WarnsAndStops', (done) => {
      const makePage = (pageNum: number): AvailabilitySlot[] =>
        Array.from({ length: 200 }, (_, i) => ({
          id: pageNum * 200 + i + 1,
          teacherId: 10,
          date: '2026-09-20',
          startTime: '14:00:00',
          endTime: '15:00:00',
          createdAt: '2026-09-15T10:00:00Z',
          startUtc: '2026-09-20T12:00:00Z',
          endUtc: '2026-09-20T13:00:00Z',
          isBooked: false,
        }));

      spyOn(console, 'warn');
      service.getAllMySlots().subscribe((result) => {
        expect(result.items.length).toBe(25 * 200);
        expect(console.warn).toHaveBeenCalledWith(
          jasmine.stringContaining('25 sayfa sınırına ulaşıldı')
        );
        // Verify no 26th request is made
        httpMock.expectNone((req) =>
          req.url === `${service['baseUrl']}/slots/mine` &&
          req.params.get('skip') === String(25 * 200)
        );
        done();
      });

      for (let i = 0; i < 25; i++) {
        const httpReq = httpMock.expectOne(
          (req) =>
            req.url === `${service['baseUrl']}/slots/mine` &&
            req.params.get('skip') === String(i * 200)
        );
        httpReq.flush({ success: true, items: makePage(i) });
      }
    });

    it('getAllMySlots_BelowMaxPages_DoesNotWarn', (done) => {
      const makePage = (pageNum: number): AvailabilitySlot[] =>
        Array.from({ length: 200 }, (_, i) => ({
          id: pageNum * 200 + i + 1,
          teacherId: 10,
          date: '2026-09-20',
          startTime: '14:00:00',
          endTime: '15:00:00',
          createdAt: '2026-09-15T10:00:00Z',
          startUtc: '2026-09-20T12:00:00Z',
          endUtc: '2026-09-20T13:00:00Z',
          isBooked: false,
        }));

      spyOn(console, 'warn');
      service.getAllMySlots().subscribe((result) => {
        expect(result.items.length).toBe(3 * 200);
        expect(console.warn).not.toHaveBeenCalled();
        done();
      });

      for (let i = 0; i < 3; i++) {
        const httpReq = httpMock.expectOne(
          (req) =>
            req.url === `${service['baseUrl']}/slots/mine` &&
            req.params.get('skip') === String(i * 200)
        );
        httpReq.flush({ success: true, items: makePage(i) });
      }

      // Page 3 is full, so one more request to check end
      const finalReq = httpMock.expectOne(
        (req) =>
          req.url === `${service['baseUrl']}/slots/mine` &&
          req.params.get('skip') === String(3 * 200)
      );
      finalReq.flush({ success: true, items: [] });
    });

    it('getAllMySlots_BackendReturnsSmallPages_AdaptsWithAccumulativeSkip', (done) => {
      const page1 = Array.from({ length: 50 }, (_, i) => ({
        id: i + 1,
        teacherId: 10,
        date: '2026-09-20',
        startTime: '14:00:00',
        endTime: '15:00:00',
        createdAt: '2026-09-15T10:00:00Z',
        startUtc: '2026-09-20T12:00:00Z',
        endUtc: '2026-09-20T13:00:00Z',
        isBooked: false,
      }));
      const page2 = Array.from({ length: 50 }, (_, i) => ({
        id: i + 51,
        teacherId: 10,
        date: '2026-09-20',
        startTime: '14:00:00',
        endTime: '15:00:00',
        createdAt: '2026-09-15T10:00:00Z',
        startUtc: '2026-09-20T12:00:00Z',
        endUtc: '2026-09-20T13:00:00Z',
        isBooked: false,
      }));

      service.getAllMySlots().subscribe((result) => {
        expect(result.items.length).toBe(100);
        expect(result.items[0].id).toBe(1);
        expect(result.items[99].id).toBe(100);
        done();
      });

      const httpReq1 = httpMock.expectOne(
        (req) =>
          req.url === `${service['baseUrl']}/slots/mine` &&
          req.params.get('skip') === '0'
      );
      httpReq1.flush({ success: true, items: page1 });

      const httpReq2 = httpMock.expectOne(
        (req) =>
          req.url === `${service['baseUrl']}/slots/mine` &&
          req.params.get('skip') === '50'
      );
      httpReq2.flush({ success: true, items: page2 });

      const httpReq3 = httpMock.expectOne(
        (req) =>
          req.url === `${service['baseUrl']}/slots/mine` &&
          req.params.get('skip') === '100'
      );
      httpReq3.flush({ success: true, items: [] });
    });

    it('getAllMySlots_TwentyFourFullPagesThenEmpty_NoWarnAndTwentyFiveRequests', (done) => {
      const makePage = (pageNum: number): AvailabilitySlot[] =>
        Array.from({ length: 200 }, (_, i) => ({
          id: pageNum * 200 + i + 1,
          teacherId: 10,
          date: '2026-09-20',
          startTime: '14:00:00',
          endTime: '15:00:00',
          createdAt: '2026-09-15T10:00:00Z',
          startUtc: '2026-09-20T12:00:00Z',
          endUtc: '2026-09-20T13:00:00Z',
          isBooked: false,
        }));

      spyOn(console, 'warn');
      service.getAllMySlots().subscribe((result) => {
        expect(result.items.length).toBe(24 * 200);
        expect(console.warn).not.toHaveBeenCalled();
        done();
      });

      // 25. (son izinli) sayfa boş: veri kesilmediği için uyarı verilmez.
      for (let i = 0; i < 25; i++) {
        const httpReq = httpMock.expectOne(
          (req) => req.url === `${service['baseUrl']}/slots/mine` && req.params.get('skip') === String(i * 200)
        );
        httpReq.flush({ success: true, items: i < 24 ? makePage(i) : [] });
      }
    });

    it('getAllMySlots_SubscribedTwice_SecondSubscriptionRestartsFromSkipZero', () => {
      const slot: AvailabilitySlot = {
        id: 1,
        teacherId: 10,
        date: '2026-09-20',
        startTime: '14:00:00',
        endTime: '15:00:00',
        createdAt: '2026-09-15T10:00:00Z',
        startUtc: '2026-09-20T12:00:00Z',
        endUtc: '2026-09-20T13:00:00Z',
        isBooked: false,
      };
      const all$ = service.getAllMySlots();
      const results: number[] = [];

      for (let run = 0; run < 2; run++) {
        all$.subscribe((result) => results.push(result.items.length));

        // Sayaç abonelik başına sıfırlanmalı: ikinci abonelik de skip=0, ardından skip=1 ister.
        httpMock
          .expectOne((req) => req.url === `${service['baseUrl']}/slots/mine` && req.params.get('skip') === '0')
          .flush({ success: true, items: [slot] });
        httpMock
          .expectOne((req) => req.url === `${service['baseUrl']}/slots/mine` && req.params.get('skip') === '1')
          .flush({ success: true, items: [] });
      }

      expect(results).toEqual([1, 1]);
    });

    it('getAllMySlots_MiddlePageError_PropagatesErrorStopsStream', (done) => {
      const page1 = Array.from({ length: 200 }, (_, i) => ({
        id: i + 1,
        teacherId: 10,
        date: '2026-09-20',
        startTime: '14:00:00',
        endTime: '15:00:00',
        createdAt: '2026-09-15T10:00:00Z',
        startUtc: '2026-09-20T12:00:00Z',
        endUtc: '2026-09-20T13:00:00Z',
        isBooked: false,
      }));

      service.getAllMySlots().subscribe({
        next: () => fail('hata beklenirken next yayildi'),
        error: (err: HttpErrorResponse) => {
          expect(err.status).toBe(500);
          done();
        },
      });

      const httpReq1 = httpMock.expectOne(
        (req) =>
          req.url === `${service['baseUrl']}/slots/mine` &&
          req.params.get('skip') === '0'
      );
      httpReq1.flush({ success: true, items: page1 });

      const httpReq2 = httpMock.expectOne(
        (req) =>
          req.url === `${service['baseUrl']}/slots/mine` &&
          req.params.get('skip') === '200'
      );
      httpReq2.flush('Server error', { status: 500, statusText: 'Internal Server Error' });
    });

    it('getAllMySlots_PageError_PropagatesError', (done) => {
      service.getAllMySlots().subscribe({
        next: () => fail('hata beklenirken next yayildi'),
        error: (error: HttpErrorResponse) => {
          expect(error.status).toBe(500);
          done();
        },
      });

      const httpReq = httpMock.expectOne((req) => req.url === `${service['baseUrl']}/slots/mine`);
      httpReq.flush('Server error', { status: 500, statusText: 'Internal Server Error' });
    });

    it('getAllMySlots_FirstPageError_DoesNotMakeSecondRequest', (done) => {
      service.getAllMySlots().subscribe({
        error: () => {
          httpMock.verify();
          done();
        },
      });

      const httpReq = httpMock.expectOne((req) => req.url === `${service['baseUrl']}/slots/mine`);
      httpReq.flush('Error', { status: 400, statusText: 'Bad Request' });
    });

    it('getAllMySlots_EmptyResponse_ReturnsEmpty', (done) => {
      service.getAllMySlots().subscribe((result) => {
        expect(result.items).toEqual([]);
        done();
      });

      const httpReq = httpMock.expectOne((req) => req.url === `${service['baseUrl']}/slots/mine`);
      httpReq.flush({ success: true, items: [] });
    });

    it('getAllMySlots_NullItems_TreatsAsEmpty', (done) => {
      service.getAllMySlots().subscribe((result) => {
        expect(result.items).toEqual([]);
        done();
      });

      const httpReq = httpMock.expectOne((req) => req.url === `${service['baseUrl']}/slots/mine`);
      httpReq.flush({ success: true, items: null });
    });

    it('getAllMySlots_PreservesFlagsFromFirstPage', (done) => {
      const slot: AvailabilitySlot = {
        id: 1,
        teacherId: 10,
        date: '2026-09-20',
        startTime: '14:00:00',
        endTime: '15:00:00',
        createdAt: '2026-09-15T10:00:00Z',
        startUtc: '2026-09-20T12:00:00Z',
        endUtc: '2026-09-20T13:00:00Z',
        isBooked: false,
      };

      service.getAllMySlots().subscribe((result) => {
        expect(result.success).toBeTrue();
        expect(result.message).toBe('Custom message from first page');
        done();
      });

      const httpReq1 = httpMock.expectOne(
        (req) =>
          req.url === `${service['baseUrl']}/slots/mine` &&
          req.params.get('skip') === '0'
      );
      httpReq1.flush({
        success: true,
        message: 'Custom message from first page',
        items: [slot],
      });

      const httpReq2 = httpMock.expectOne(
        (req) =>
          req.url === `${service['baseUrl']}/slots/mine` &&
          req.params.get('skip') === '1'
      );
      httpReq2.flush({ success: true, items: [] });
    });
  });
});
