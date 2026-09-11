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
