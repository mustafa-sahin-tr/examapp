import { AvailabilitySlot } from '../../../models/booking.model';
import { endsOnNextLocalDay, formatSlotDay, formatSlotRange, isPastSlot } from '../../utils/booking-format.util';

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
  /** Slotu üreten tekrarlayan kuralın kimliği (issue #179); tekil slotta null. Olay `is-recurring` sınıfı alır. */
  ruleId: number | null;
  /** "14:00 – 15:00" — listedeki `date` pipe'ıyla aynı yerel saat. */
  timeRange: string;
  /**
   * Bitiş ertesi YEREL günde (issue #300, ör. 23:30 – 00:30): şablon aralığa "(+1 gün)" ekler. FullCalendar böyle
   * bir olayı iki gün sütununa böler; ikinci parça (`isStart` false) devam parçası olarak çizilir.
   */
  endsNextDay: boolean;
  /** "20 Eylül 2026 Pazar" */
  dayLabel: string;
  /**
   * Issue #439: slotu yalnız veli onayı bekleyen talep tutuyor. Öğretmen böyle slotu silebilir (talep `Expired` olur); silme
   * çubuğu uyarı gösterir.
   */
  awaitingParent: boolean;
}

/**
 * Silinebilir slot: boş ya da yalnız veli onayı bekleyen talebi tutan (issue #439). Onaylı/bekleyen (öğretmene düşmüş)
 * randevulu slot silinemez.
 */
export function isDeletableSlot(p: Pick<AvailabilityGridEventProps, 'statusClass' | 'awaitingParent'>): boolean {
  return p.statusClass === 'is-free' || p.awaitingParent;
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
 * Issue #439: veli onayı bekleyen talep (`AwaitingParent`) slotu tutar ama öğretmene henüz düşmemiştir — dolu (`is-pending`)
 * görünür, etiketi "Veli onayı bekleniyor"; öğrenci adı gelmez.
 * Kural, teacher-availability listesindeki chip mantığıyla birebir aynıdır.
 */
export function slotStatus(slot: AvailabilitySlot): { statusClass: SlotStatusClass; statusKey: string } {
  if (!slot.isBooked) {
    return { statusClass: 'is-free', statusKey: 'status.free' };
  }
  if (slot.bookingStatus === 'Approved') {
    return { statusClass: 'is-approved', statusKey: 'status.approved' };
  }
  if (slot.bookingStatus === 'AwaitingParent') {
    return { statusClass: 'is-pending', statusKey: 'status.awaitingParent' };
  }
  return { statusClass: 'is-pending', statusKey: 'status.pending' };
}

/**
 * `AvailabilitySlot[]` → grid olayları. Saatler `startUtc`/`endUtc`'den (yerel saate çevrilerek)
 * okunur; geçersiz veya bitişi başlangıçtan sonra olmayan slotlar atlanır.
 *
 * Bitiş YALNIZCA `endUtc`'den gelir: gün aşan slotta (issue #300) `endTime` başlangıçtan küçüktür ve `date + endTime`
 * yanlış (başlangıçtan önceki) bir an verir; backend `endUtc`'yi ertesi güne koyar.
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
    const ruleId = slot.recurringAvailabilityRuleId ?? null;
    const classNames: string[] = [statusClass];
    if (past) {
      classNames.push('is-past');
    }
    if (ruleId !== null) {
      classNames.push('is-recurring');
    }

    events.push({
      id: String(slot.id),
      start,
      end,
      classNames,
      extendedProps: {
        slotId: slot.id,
        statusClass,
        statusKey,
        studentName: slot.isBooked && slot.studentName ? slot.studentName : null,
        past,
        ruleId,
        timeRange: formatSlotRange(slot.startUtc, slot.endUtc),
        endsNextDay: endsOnNextLocalDay(start, end),
        dayLabel: formatSlotDay(slot.startUtc),
        awaitingParent: slot.isBooked && slot.bookingStatus === 'AwaitingParent',
      },
    });
  }

  return events;
}
