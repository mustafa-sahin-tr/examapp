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

/**
 * Slot üzerindeki aktif randevunun durumu. Issue #439: `AwaitingParent` (veli onayı bekleyen talep) slotu tutar; öğretmenin
 * kendi listesinde `isBooked=true`, `bookingId=null` ve öğrenci adı olmadan gelir (talep henüz öğretmene düşmedi).
 */
export type ActiveBookingStatus = 'Pending' | 'Approved' | 'AwaitingParent';

/**
 * Issue #439: `AwaitingParent` (veli onayı bekleniyor), `Expired` (veli 7 günde karar vermedi), `ParentRejected` (veli reddetti;
 * `rejectionReason` velinin notu).
 */
export type BookingStatus = ActiveBookingStatus | 'Rejected' | 'Expired' | 'ParentRejected';

/** Kapanmış (aktif olmayan) randevu durumları — öğrenci listesindeki "Aktif" filtresi bunları gizler. */
export const CLOSED_BOOKING_STATUSES: readonly BookingStatus[] = ['Rejected', 'Expired', 'ParentRejected'];

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
  /** Öğretmenin ret gerekçesi; `ParentRejected` iken velinin notu. */
  rejectionReason?: string | null;
  /** Issue #439: velinin onay anı (veli doğrudan talep açtıysa oluşturma anı); veliye tabi olmayan talepte null. */
  parentApprovedAt?: string | null;
  /** Issue #439: yalnız `AwaitingParent` iken dolu — bu andan sonra talep `Expired` olur. */
  expiresAt?: string | null;
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
  objectId?: number | null;
  booking?: Booking | null;
  /** Issue #439: makine okunur kod (bkz. `BOOKING_ERROR_CODES`). */
  errorCode?: string | null;
}

export interface BookingListResult extends BookingResponseBase {
  items: Booking[];
}

// ---------------- Veli onayı (issue #439) ----------------

/**
 * Velinin "Onay bekleyen ders talepleri" satırı (`ParentBookingRequestDto`). Velinin Active bağlı TÜM çocuklarının, hâlâ karar
 * verilebilir `AwaitingParent` talepleri; ders zamanına göre artan.
 */
export interface ParentBookingRequest {
  id: number;
  studentId: number;
  studentName: string | null;
  teacherId: number;
  teacherName: string | null;
  availabilitySlotId: number;
  date: string;
  startTime: string;
  endTime: string;
  startUtc: string;
  endUtc: string;
  createdAt: string;
  /** Bu andan sonra talep `Expired` olur (oluşturmadan 7 gün). */
  expiresAt: string;
}

/** GET /parent/booking-requests yanıtı. */
export interface ParentBookingRequestList {
  items: ParentBookingRequest[];
}

/** POST /parent/booking-requests/{id}/reject gövdesi — not opsiyonel, en fazla 500 karakter (öğrenciye bildirilir). */
export interface ParentRejectBookingRequest {
  note?: string | null;
}

/** Veli uçlarının hata gövdesi: `{ message, errorCode }`. */
export interface ParentBookingErrorBody {
  message?: string | null;
  errorCode?: string | null;
}

/** Veli panelindeki "Onay bekleyen ders talepleri" kartının fragment'ı (`/parent#booking-requests`, bildirim derin linki). */
export const PARENT_BOOKING_REQUESTS_FRAGMENT = 'booking-requests';

/** Velinin ret notu üst sınırı (backend `ParentRejectBookingDto.Note`). */
export const PARENT_REJECT_NOTE_MAX_LENGTH = 500;

/** Backend `BookingErrorCodes` (issue #439 ile gelenler + veli uçlarında kullanılanlar). */
export const BOOKING_ERROR_CODES = {
  /** 403 — veliye tabi öğrencinin Active velisi yok, talep açılamaz. */
  parentRequired: 'ParentRequired',
  /**
   * 409 (öğrenci talebi) — veli onayı bekleyen açık talep sınırı (3) dolu ya da aynı öğretmene zaten bekleyen bir talep var.
   */
  tooManyAwaitingParent: 'TooManyAwaitingParent',
  /** 409 — başka veli önce karar verdi / süre doldu / talep kapandı. */
  alreadyDecided: 'AlreadyDecided',
  /** 409 — talebin süresi doldu. */
  requestExpired: 'BookingRequestExpired',
  /** 409 (onay) — öğretmen askıda/onaysız. */
  teacherUnavailable: 'TeacherUnavailable',
  /** 409 (onay) — öğretmen müsaitlik aralığını sildi. */
  requestSlotDeleted: 'BookingRequestSlotDeleted',
  /** 404 — bağlı olmayan çocuk / talep yok. */
  notFound: 'NotFound',
} as const;

/** Hata gövdesindeki `errorCode` (güvenilmeyen veri); yoksa null. */
export function bookingErrorCode(body: unknown): string | null {
  if (!body || typeof body !== 'object') {
    return null;
  }
  const code = (body as Record<string, unknown>)['errorCode'];
  return typeof code === 'string' && code ? code : null;
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
  /**
   * Issue #298: makine okunur red kodu (`VideoSessionResultDto.ErrorCode`). Şimdilik yalnızca
   * `TeacherUnavailable` (409); diğer yanıtlarda alan gelmez.
   */
  errorCode?: string | null;
}

/** `TeacherAccessErrorCodes.TeacherUnavailable` — öğretmen askıda/onaysız, ders açılamaz (issue #298). */
export const TEACHER_UNAVAILABLE_ERROR_CODE = 'TeacherUnavailable';

/**
 * Hata gövdesi (güvenilmeyen veri) `errorCode: "TeacherUnavailable"` taşıyor mu. Yalnızca 409'da anlamlıdır;
 * başka durum kodlarında `false`.
 */
export function isTeacherUnavailableError(status: number, body: unknown): boolean {
  return (
    status === 409 &&
    !!body &&
    typeof body === 'object' &&
    (body as Record<string, unknown>)['errorCode'] === TEACHER_UNAVAILABLE_ERROR_CODE
  );
}
