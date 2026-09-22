/**
 * Ders planlama / randevu modelleri (issue #96).
 * Backend karşılığı: api/ExamApp.Api/Models/Dtos/Bookings/BookingDtos.cs
 * Uçlar gateway üzerinden: /api/exam/booking/...
 */

/** Backend ResponseBaseDto — hata gövdelerinde bayraklar HTTP koduyla birlikte gelir. */
export interface BookingResponseBase {
  success: boolean;
  message?: string | null;
  notFound?: boolean;
  forbidden?: boolean;
  conflict?: boolean;
}

/** "Pending" | "Approved" (slot üzerindeki aktif randevunun durumu) */
export type ActiveBookingStatus = 'Pending' | 'Approved';

export type BookingStatus = ActiveBookingStatus | 'Rejected';

/** Öğretmenin müsaitlik aralığı. `bookingStatus`/`studentName` yalnızca öğretmenin kendi listesinde dolu. */
export interface AvailabilitySlot {
  id: number;
  teacherId: number;
  /** "2026-09-20" */
  date: string;
  /** "14:00:00" */
  startTime: string;
  endTime: string;
  createdAt: string;
  /** ISO datetime (UTC) — tarih hesabı için hazır. */
  startUtc: string;
  endUtc: string;
  isBooked: boolean;
  bookingId?: number | null;
  bookingStatus?: ActiveBookingStatus | null;
  studentName?: string | null;
  /**
   * Slot bir tekrarlayan kuraldan üretildiyse kuralın kimliği (issue #178); tekil slotta null.
   * Yalnızca öğretmenin kendi listesinde (`slots/mine`) dolu; öğrenci ucunda her zaman null.
   */
  recurringAvailabilityRuleId?: number | null;
}

// ---------------- Tekrarlayan haftalık müsaitlik (issue #178 / #179) ----------------

/** Haftanın günü — backend `System.DayOfWeek`, JSON'da sayı: 0=Pazar .. 6=Cumartesi (JS `getUTCDay()` ile aynı). */
export type RuleDayOfWeek = 0 | 1 | 2 | 3 | 4 | 5 | 6;

/** Tekrarlayan kural görünümü (`RecurringAvailabilityRuleDto`). Saatler tekil slot gibi UTC duvar saatidir. */
export interface RecurringAvailabilityRule {
  id: number;
  teacherId: number;
  dayOfWeek: RuleDayOfWeek;
  /** "14:00:00" */
  startTime: string;
  endTime: string;
  /** Geçerli ilk gün (dahil), "2026-09-28". */
  effectiveFrom: string;
  /** Geçerli son gün (dahil); null = süresiz. */
  effectiveUntil?: string | null;
  isActive: boolean;
  createdAt: string;
}

/**
 * POST /booking/recurring-rules gövdesi (`CreateRecurringAvailabilityRuleDto`).
 * Saatler "HH:mm:00" (dakika hassasiyeti), gün/tarih tıklanan anın UTC bileşenleridir (bkz. `toRecurringRuleRequest`).
 */
export interface CreateRecurringRuleRequest {
  dayOfWeek: RuleDayOfWeek;
  startTime: string;
  endTime: string;
  /** "YYYY-MM-DD"; UTC bugün .. bugün+90. */
  effectiveFrom: string;
  /** "YYYY-MM-DD" ya da null (süresiz); verilirse effectiveFrom'dan sonra ve en fazla 1 yıl ileride. */
  effectiveUntil: string | null;
}

/** Kural oluşturma sonucu: kural + ufuk içinde üretilen slotlar + tekil slotla çakıştığı için atlanan tarihler. */
export interface RecurringRuleResult extends BookingResponseBase {
  objectId?: number | null;
  rule?: RecurringAvailabilityRule | null;
  generatedSlotIds: number[];
  /** "2026-10-05" — hata değil; öğretmen o haftaları elle düzenler. */
  skippedDates: string[];
}

/** DELETE /booking/recurring-rules/{id} ("tüm seri") sonucu: randevusuz gelecek slotlar silinir, randevulular korunur. */
export interface RecurringRuleDeleteResult extends BookingResponseBase {
  objectId?: number | null;
  deletedSlotIds: number[];
  preservedSlotIds: number[];
  /** `preservedSlotIds.length` — "K randevulu aralık korundu" mesajı için. */
  preservedBookedCount: number;
}

/** Randevu talebi. Aynı şekil hem öğretmen hem öğrenci listelerinde kullanılır. */
export interface Booking {
  id: number;
  teacherId: number;
  teacherName?: string | null;
  studentId: number;
  studentName?: string | null;
  availabilitySlotId: number;
  date: string;
  startTime: string;
  endTime: string;
  startUtc: string;
  endUtc: string;
  status: BookingStatus;
  createdAt: string;
  decisionAt?: string | null;
  rejectionReason?: string | null;
}

/** POST /booking/slots gövdesi. Saatler "HH:mm:ss" formatında gönderilir. */
export interface CreateAvailabilitySlotRequest {
  date: string;
  startTime: string;
  endTime: string;
}

/** POST /booking/requests gövdesi. */
export interface CreateBookingRequest {
  availabilitySlotId: number;
}

/** POST /booking/requests/{id}/reject gövdesi. */
export interface RejectBookingRequest {
  rejectionReason?: string | null;
}

export interface AvailabilitySlotResult extends BookingResponseBase {
  slot?: AvailabilitySlot | null;
}

export interface AvailabilitySlotListResult extends BookingResponseBase {
  items: AvailabilitySlot[];
}

export interface BookingResult extends BookingResponseBase {
  booking?: Booking | null;
}

export interface BookingListResult extends BookingResponseBase {
  items: Booking[];
}

// ---------------- Video görüşme (issue #97) ----------------

/**
 * Bir görüşme odasına katılmak için gereken her şey.
 * Backend karşılığı: api/ExamApp.Api/Models/Dtos/Video/VideoSessionDtos.cs (`VideoSessionDto`).
 * Sağlayıcıdan bağımsızdır; şimdilik `provider` her zaman "Jitsi".
 */
export interface VideoSession {
  provider: string;
  /** Deterministik oda adı, ör. "booking-12-a1b2c3d4e5f6". */
  roomName: string;
  /** Şemasız host, ör. "localhost:8000" — Jitsi IFrame API bunu ister. */
  domain: string;
  /** Şemalı taban adres, ör. "http://localhost:8000". */
  baseUrl: string;
  /** Token dahil, doğrudan tarayıcıda açılabilen tam katılım adresi. */
  joinUrl: string;
  /** Odaya giriş için üretilmiş kısa ömürlü JWT. */
  token: string;
  /** Token geçerlilik sonu (ISO, UTC). */
  expiresAt: string;
  /** Çağıran bu odada moderatör mü (öğretmen). */
  isModerator: boolean;
}

/** POST /booking/requests/{id}/video-session yanıtı. */
export interface VideoSessionResult extends BookingResponseBase {
  objectId?: number | null;
  session?: VideoSession | null;
}
