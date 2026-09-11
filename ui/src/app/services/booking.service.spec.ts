import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';

import { BookingService } from './booking.service';
import {
  CreateAvailabilitySlotRequest,
  CreateBookingRequest,
  AvailabilitySlot,
  Booking,
} from '../models/booking.model';

describe('BookingService', () => {
  let service: BookingService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
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

    it('createSlot_ApiReturns409Conflict_ResultIncludesConflictFlag', (done) => {
      const req: CreateAvailabilitySlotRequest = {
        date: '2026-09-20',
        startTime: '14:00:00',
        endTime: '15:00:00',
      };

      service.createSlot(req).subscribe({
        next: (result) => {
          expect(result.success).toBeFalse();
          expect(result.conflict).toBeTrue();
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

    it('getTeacherSlots_ApiReturns404_NotFound', (done) => {
      const teacherId = 999;

      service.getTeacherSlots(teacherId).subscribe((result) => {
        expect(result.success).toBeFalse();
        expect(result.notFound).toBeTrue();
        done();
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

    it('createBooking_ApiReturns409Conflict_SlotAlreadyBooked', (done) => {
      const req: CreateBookingRequest = { availabilitySlotId: 1 };

      service.createBooking(req).subscribe((result) => {
        expect(result.success).toBeFalse();
        expect(result.conflict).toBeTrue();
        done();
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
});
