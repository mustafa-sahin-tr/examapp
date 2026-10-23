import { Booking, BookingParty } from '../../models/booking.model';

/**
 * Issue #315 — bir randevu satırında hangi iptal / erteleme / no-show aksiyonlarının görüneceği. Saf fonksiyon: `now` ve
 * görüntüleyen taraf dışarıdan gelir. Asıl kapı backend'dedir (sınır/pencere 409 döner); burası yalnız görünürlüktür.
 */
export interface BookingLifecycleView {
  /** Onaylı randevu, ders henüz başlamadı → "İptal et" görünür. */
  showCancel: boolean;
  /** İptal penceresi kapandı (`cancellationDeadlineUtc` geçti) → buton pasif + ipucu. */
  cancelWindowClosed: boolean;
  /** Öğrenci: `Pending`/`AwaitingParent` talep → "Talebi geri çek". */
  showWithdrawRequest: boolean;
  /** Sunucu yeni erteleme önerisine izin veriyor (`canProposeReschedule`). */
  showPropose: boolean;
  /** Bekleyen erteleme önerisi (yalnız onaylı randevuda); yoksa null. */
  proposal: { startUtc: string; endUtc: string; mine: boolean; canAccept: boolean } | null;
  /** Ders bitti, pencere açık, henüz bildirim yok → "Karşı taraf katılmadı". */
  showNoShow: boolean;
}

function time(iso: string | null | undefined): number {
  if (!iso) {
    return Number.NaN;
  }
  return new Date(iso).getTime();
}

export function bookingLifecycleView(booking: Booking, viewer: BookingParty, now: number): BookingLifecycleView {
  const approved = booking.status === 'Approved';
  const start = time(booking.startUtc);
  const end = time(booking.endUtc);
  const deadline = time(booking.cancellationDeadlineUtc);
  const noShowUntil = time(booking.noShowReportableUntilUtc);

  const showCancel = approved && !Number.isNaN(start) && now < start;
  const proposal =
    approved && booking.proposedSlotId != null && booking.proposedStartUtc && booking.proposedEndUtc
      ? {
          startUtc: booking.proposedStartUtc,
          endUtc: booking.proposedEndUtc,
          mine: booking.proposedByRole === viewer,
          // Bayat öneri (slot silindi / süre geçti) kabul edilemez; ret / geri çekme açık kalır.
          canAccept: booking.canRespondReschedule === true,
        }
      : null;

  return {
    showCancel,
    // Son an bilinmiyorsa (eski yanıt) pasif yapılmaz; sunucu yine 409 ile reddeder.
    cancelWindowClosed: showCancel && !Number.isNaN(deadline) && now > deadline,
    showWithdrawRequest:
      viewer === 'Student' && (booking.status === 'Pending' || booking.status === 'AwaitingParent'),
    showPropose: approved && booking.canProposeReschedule === true && proposal === null,
    proposal,
    showNoShow:
      approved &&
      !booking.noShowParty &&
      !Number.isNaN(end) &&
      !Number.isNaN(noShowUntil) &&
      now >= end &&
      now <= noShowUntil,
  };
}
