import { AvailabilitySlot } from '../../../models/booking.model';
import { toGridEvents, slotStatus, AvailabilityGridEvent } from './availability-week-grid.util';

describe('availability-week-grid.util', () => {
  describe('slotStatus', () => {
    it('slotStatus_FreeSlot_ReturnsFreeStatus', () => {
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

      const result = slotStatus(slot);

      expect(result.statusClass).toBe('is-free');
      expect(result.statusKey).toBe('status.free');
    });

    it('slotStatus_ApprovedBooking_ReturnsApprovedStatus', () => {
      const slot: AvailabilitySlot = {
        id: 1,
        teacherId: 10,
        date: '2026-09-20',
        startTime: '14:00:00',
        endTime: '15:00:00',
        createdAt: '2026-09-15T10:00:00Z',
        startUtc: '2026-09-20T12:00:00Z',
        endUtc: '2026-09-20T13:00:00Z',
        isBooked: true,
        bookingStatus: 'Approved',
      };

      const result = slotStatus(slot);

      expect(result.statusClass).toBe('is-approved');
      expect(result.statusKey).toBe('status.approved');
    });

    it('slotStatus_PendingBooking_ReturnsPendingStatus', () => {
      const slot: AvailabilitySlot = {
        id: 1,
        teacherId: 10,
        date: '2026-09-20',
        startTime: '14:00:00',
        endTime: '15:00:00',
        createdAt: '2026-09-15T10:00:00Z',
        startUtc: '2026-09-20T12:00:00Z',
        endUtc: '2026-09-20T13:00:00Z',
        isBooked: true,
        bookingStatus: 'Pending',
      };

      const result = slotStatus(slot);

      expect(result.statusClass).toBe('is-pending');
      expect(result.statusKey).toBe('status.pending');
    });
  });

  describe('toGridEvents', () => {
    it('toGridEvents_ValidSlot_CreatesEventWithCorrectStartEnd', () => {
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

      const events = toGridEvents([slot]);

      expect(events.length).toBe(1);
      expect(events[0].id).toBe('1');
      expect(events[0].start).toEqual(new Date(slot.startUtc));
      expect(events[0].end).toEqual(new Date(slot.endUtc));
    });

    it('toGridEvents_FreeSlot_IncludesCorrectClassName', () => {
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

      const events = toGridEvents([slot]);

      expect(events[0].classNames).toContain('is-free');
    });

    it('toGridEvents_PastSlot_IncludesPastClassName', () => {
      const now = new Date();
      const pastDate = new Date(now.getTime() - 60 * 60 * 1000); // 1 hour ago
      const slot: AvailabilitySlot = {
        id: 1,
        teacherId: 10,
        date: '2026-09-20',
        startTime: '14:00:00',
        endTime: '15:00:00',
        createdAt: '2026-09-15T10:00:00Z',
        startUtc: pastDate.toISOString(),
        endUtc: new Date(pastDate.getTime() + 60 * 60 * 1000).toISOString(),
        isBooked: false,
      };

      const events = toGridEvents([slot]);

      expect(events[0].classNames).toContain('is-past');
      expect(events[0].extendedProps.past).toBeTrue();
    });

    it('toGridEvents_InvalidStartDate_SkipsSlot', () => {
      const slot: AvailabilitySlot = {
        id: 1,
        teacherId: 10,
        date: '2026-09-20',
        startTime: '14:00:00',
        endTime: '15:00:00',
        createdAt: '2026-09-15T10:00:00Z',
        startUtc: 'invalid-date',
        endUtc: '2026-09-20T13:00:00Z',
        isBooked: false,
      };

      const events = toGridEvents([slot]);

      expect(events.length).toBe(0);
    });

    it('toGridEvents_EndBeforeStart_SkipsSlot', () => {
      const slot: AvailabilitySlot = {
        id: 1,
        teacherId: 10,
        date: '2026-09-20',
        startTime: '14:00:00',
        endTime: '15:00:00',
        createdAt: '2026-09-15T10:00:00Z',
        startUtc: '2026-09-20T13:00:00Z',
        endUtc: '2026-09-20T12:00:00Z',
        isBooked: false,
      };

      const events = toGridEvents([slot]);

      expect(events.length).toBe(0);
    });

    it('toGridEvents_EndEqualsStart_SkipsSlot', () => {
      const slot: AvailabilitySlot = {
        id: 1,
        teacherId: 10,
        date: '2026-09-20',
        startTime: '14:00:00',
        endTime: '15:00:00',
        createdAt: '2026-09-15T10:00:00Z',
        startUtc: '2026-09-20T12:00:00Z',
        endUtc: '2026-09-20T12:00:00Z',
        isBooked: false,
      };

      const events = toGridEvents([slot]);

      expect(events.length).toBe(0);
    });

    it('toGridEvents_MultipleSlots_IncludesAllValid', () => {
      const slots: AvailabilitySlot[] = [
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
        {
          id: 2,
          teacherId: 10,
          date: '2026-09-20',
          startTime: '16:00:00',
          endTime: '17:00:00',
          createdAt: '2026-09-15T10:00:00Z',
          startUtc: '2026-09-20T14:00:00Z',
          endUtc: '2026-09-20T15:00:00Z',
          isBooked: false,
        },
      ];

      const events = toGridEvents(slots);

      expect(events.length).toBe(2);
      expect(events[0].id).toBe('1');
      expect(events[1].id).toBe('2');
    });

    it('toGridEvents_BookedSlotWithStudentName_IncludesStudentName', () => {
      const slot: AvailabilitySlot = {
        id: 1,
        teacherId: 10,
        date: '2026-09-20',
        startTime: '14:00:00',
        endTime: '15:00:00',
        createdAt: '2026-09-15T10:00:00Z',
        startUtc: '2026-09-20T12:00:00Z',
        endUtc: '2026-09-20T13:00:00Z',
        isBooked: true,
        studentName: 'Ayşe Yılmaz',
        bookingStatus: 'Approved',
      };

      const events = toGridEvents([slot]);

      expect(events[0].extendedProps.studentName).toBe('Ayşe Yılmaz');
    });

    it('toGridEvents_BookedSlotWithoutStudentName_StudentNameIsNull', () => {
      const slot: AvailabilitySlot = {
        id: 1,
        teacherId: 10,
        date: '2026-09-20',
        startTime: '14:00:00',
        endTime: '15:00:00',
        createdAt: '2026-09-15T10:00:00Z',
        startUtc: '2026-09-20T12:00:00Z',
        endUtc: '2026-09-20T13:00:00Z',
        isBooked: true,
        bookingStatus: 'Approved',
      };

      const events = toGridEvents([slot]);

      expect(events[0].extendedProps.studentName).toBeNull();
    });

    it('toGridEvents_FreeSlotWithStudentNameField_StudentNameIsNull', () => {
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
        studentName: 'Ayşe Yılmaz',
      };

      const events = toGridEvents([slot]);

      expect(events[0].extendedProps.studentName).toBeNull();
    });

    it('toGridEvents_HtmlInStudentName_TreatedAsLiteral', () => {
      const slot: AvailabilitySlot = {
        id: 1,
        teacherId: 10,
        date: '2026-09-20',
        startTime: '14:00:00',
        endTime: '15:00:00',
        createdAt: '2026-09-15T10:00:00Z',
        startUtc: '2026-09-20T12:00:00Z',
        endUtc: '2026-09-20T13:00:00Z',
        isBooked: true,
        studentName: 'Ayşe <b>Bold</b>',
        bookingStatus: 'Approved',
      };

      const events = toGridEvents([slot]);

      expect(events[0].extendedProps.studentName).toBe('Ayşe <b>Bold</b>');
    });

    it('toGridEvents_Empty_ReturnsEmpty', () => {
      const events = toGridEvents([]);

      expect(events.length).toBe(0);
    });
  });
});
