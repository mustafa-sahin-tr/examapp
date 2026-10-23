import { Booking, BookingStatus } from '../../models/booking.model';
import { bookingLifecycleView } from './booking-lifecycle.util';

/** Issue #315 — satır aksiyonlarının görünürlüğü. */
describe('bookingLifecycleView (issue #315)', () => {
  const start = Date.UTC(2030, 9, 20, 11, 0, 0);
  const hour = 60 * 60 * 1000;

  function booking(status: BookingStatus, overrides: Partial<Booking> = {}): Booking {
    return {
      id: 1,
      teacherId: 4,
      studentId: 21,
      availabilitySlotId: 100,
      date: '2030-10-20',
      startTime: '11:00:00',
      endTime: '12:00:00',
      startUtc: new Date(start).toISOString(),
      endUtc: new Date(start + hour).toISOString(),
      status,
      createdAt: '2030-10-01T08:00:00Z',
      cancellationDeadlineUtc: status === 'Approved' ? new Date(start - 24 * hour).toISOString() : null,
      noShowReportableUntilUtc: status === 'Approved' ? new Date(start + hour + 7 * 24 * hour).toISOString() : null,
      canProposeReschedule: status === 'Approved',
      ...overrides,
    };
  }

  it('Approved_BeforeDeadline_CancelEnabledAndProposeShown', () => {
    const view = bookingLifecycleView(booking('Approved'), 'Student', start - 48 * hour);

    expect(view.showCancel).toBeTrue();
    expect(view.cancelWindowClosed).toBeFalse();
    expect(view.showPropose).toBeTrue();
    expect(view.showNoShow).toBeFalse();
    expect(view.showWithdrawRequest).toBeFalse();
  });

  it('Approved_AfterDeadline_CancelShownButClosed', () => {
    const view = bookingLifecycleView(booking('Approved', { canProposeReschedule: false }), 'Teacher', start - hour);

    expect(view.showCancel).toBeTrue();
    expect(view.cancelWindowClosed).toBeTrue();
    expect(view.showPropose).toBeFalse();
  });

  it('Approved_AfterEnd_NoShowWithinWindowOnly', () => {
    expect(bookingLifecycleView(booking('Approved'), 'Student', start + 2 * hour).showNoShow).toBeTrue();
    expect(bookingLifecycleView(booking('Approved'), 'Student', start + 2 * hour).showCancel).toBeFalse();
    expect(bookingLifecycleView(booking('Approved'), 'Student', start + 30 * 24 * hour).showNoShow).toBeFalse();
    expect(
      bookingLifecycleView(booking('Approved', { noShowParty: 'Teacher' }), 'Student', start + 2 * hour).showNoShow
    ).toBeFalse();
  });

  it('PendingProposal_MineVsTheirs_HidesPropose', () => {
    const proposed = booking('Approved', {
      proposedSlotId: 200,
      proposedStartUtc: '2030-10-22T11:00:00Z',
      proposedEndUtc: '2030-10-22T12:00:00Z',
      proposedByRole: 'Teacher',
      canProposeReschedule: false,
    });

    expect(bookingLifecycleView(proposed, 'Teacher', start - 48 * hour).proposal?.mine).toBeTrue();
    expect(bookingLifecycleView(proposed, 'Student', start - 48 * hour).proposal?.mine).toBeFalse();
    expect(bookingLifecycleView(proposed, 'Student', start - 48 * hour).showPropose).toBeFalse();
  });

  it('StudentPendingOrAwaitingParent_WithdrawOnly_TeacherNever', () => {
    for (const status of ['Pending', 'AwaitingParent'] as const) {
      const view = bookingLifecycleView(booking(status), 'Student', start - 48 * hour);
      expect(view.showWithdrawRequest).withContext(status).toBeTrue();
      expect(view.showCancel).withContext(status).toBeFalse();
      expect(bookingLifecycleView(booking(status), 'Teacher', start - 48 * hour).showWithdrawRequest)
        .withContext(status)
        .toBeFalse();
    }
  });

  it('ClosedStatuses_NoActions', () => {
    for (const status of ['Cancelled', 'Rejected', 'Expired', 'ParentRejected'] as const) {
      const view = bookingLifecycleView(booking(status, { canProposeReschedule: true }), 'Student', start - 48 * hour);
      expect([view.showCancel, view.showPropose, view.showNoShow, view.showWithdrawRequest, !!view.proposal])
        .withContext(status)
        .toEqual([false, false, false, false, false]);
    }
  });
});
