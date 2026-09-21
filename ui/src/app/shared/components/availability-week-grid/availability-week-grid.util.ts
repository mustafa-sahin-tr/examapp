import { AvailabilitySlot } from '../../../models/booking.model';
import { formatSlotDay, formatSlotRange, isPastSlot } from '../../utils/booking-format.util';

/** Slot durumunun grid'deki görsel sınıfı (liste chip'leriyle aynı adlar). */
export type SlotStatusClass = 'is-free' | 'is-pending' | 'is-approved';

/** FullCalendar `extendedProps` içinde taşınan, şablonda okunan alanlar. */
export interface AvailabilityGridEventProps {
  slotId: number;
  statusClass: SlotStatusClass;
  /** Teacher-availability scope'una göreli çeviri anahtarı (`status.free` ...). */
  statusKey: string;
  studentName: string | null;
  past: boolean;
  /** "14:00 – 15:00" — listedeki `date` pipe'ıyla aynı yerel saat. */
  timeRange: string;
  /** "20 Eylül 2026 Pazar" */
  dayLabel: string;
}

/** FullCalendar `EventInput` ile uyumlu, kütüphaneden bağımsız olay şekli. */
export interface AvailabilityGridEvent {
  id: string;
  start: Date;
  end: Date;
  classNames: string[];
  extendedProps: AvailabilityGridEventProps;
}

/**
 * Slotun durumunu türetir: boş → `is-free`; onaylı randevu → `is-approved`; diğer dolu → `is-pending`.
 * Kural, teacher-availability listesindeki chip mantığıyla birebir aynıdır.
 */
export function slotStatus(slot: AvailabilitySlot): { statusClass: SlotStatusClass; statusKey: string } {
  if (!slot.isBooked) {
    return { statusClass: 'is-free', statusKey: 'status.free' };
  }
  return slot.bookingStatus === 'Approved'
    ? { statusClass: 'is-approved', statusKey: 'status.approved' }
    : { statusClass: 'is-pending', statusKey: 'status.pending' };
}

/**
 * `AvailabilitySlot[]` → grid olayları. Saatler `startUtc`/`endUtc`'den (yerel saate çevrilerek)
 * okunur; geçersiz veya bitişi başlangıçtan sonra olmayan slotlar atlanır.
 */
export function toGridEvents(slots: readonly AvailabilitySlot[]): AvailabilityGridEvent[] {
  const events: AvailabilityGridEvent[] = [];

  for (const slot of slots) {
    const start = new Date(slot.startUtc);
    const end = new Date(slot.endUtc);
    if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime()) || end.getTime() <= start.getTime()) {
      continue;
    }

    const { statusClass, statusKey } = slotStatus(slot);
    const past = isPastSlot(slot.startUtc);

    events.push({
      id: String(slot.id),
      start,
      end,
      classNames: past ? [statusClass, 'is-past'] : [statusClass],
      extendedProps: {
        slotId: slot.id,
        statusClass,
        statusKey,
        studentName: slot.isBooked && slot.studentName ? slot.studentName : null,
        past,
        timeRange: formatSlotRange(slot.startUtc, slot.endUtc),
        dayLabel: formatSlotDay(slot.startUtc),
      },
    });
  }

  return events;
}
