import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AvailabilitySlotListResult,
  AvailabilitySlotResult,
  BookingListResult,
  BookingResponseBase,
  BookingResult,
  CreateAvailabilitySlotRequest,
  CreateBookingRequest,
  VideoSessionResult,
} from '../models/booking.model';

/** Liste uçlarının varsayılan sayfa boyutu — backend skip/take bekliyor. */
const DEFAULT_TAKE = 100;

/**
 * Ders planlama / randevu uçları (issue #96). Tüm çağrılar gateway üzerinden
 * (`/api/exam/booking/...`); rol kontrolü backend'de (`Teacher` / `Student`).
 * Hata gövdeleri `ResponseBaseDto` tabanlıdır — `extractError` ile okunabilir mesaja çevrilir.
 */
@Injectable({ providedIn: 'root' })
export class BookingService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/exam/booking';

  // ---------------- Müsaitlik slotları (öğretmen) ----------------

  createSlot(body: CreateAvailabilitySlotRequest): Observable<AvailabilitySlotResult> {
    return this.http.post<AvailabilitySlotResult>(`${this.baseUrl}/slots`, body);
  }

  getMySlots(skip = 0, take = DEFAULT_TAKE): Observable<AvailabilitySlotListResult> {
    return this.http.get<AvailabilitySlotListResult>(`${this.baseUrl}/slots/mine`, {
      params: this.page(skip, take),
    });
  }

  deleteSlot(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/slots/${id}`);
  }

  // ---------------- Müsaitlik slotları (öğrenci görünümü) ----------------

  /** Bir öğretmenin gelecekteki, randevu alınmamış aralıkları. */
  getTeacherSlots(teacherId: number, skip = 0, take = DEFAULT_TAKE): Observable<AvailabilitySlotListResult> {
    return this.http.get<AvailabilitySlotListResult>(`${this.baseUrl}/teachers/${teacherId}/slots`, {
      params: this.page(skip, take),
    });
  }

  // ---------------- Randevu talepleri ----------------

  createBooking(body: CreateBookingRequest): Observable<BookingResult> {
    return this.http.post<BookingResult>(`${this.baseUrl}/requests`, body);
  }

  getTeacherRequests(skip = 0, take = DEFAULT_TAKE): Observable<BookingListResult> {
    return this.http.get<BookingListResult>(`${this.baseUrl}/requests/teacher`, {
      params: this.page(skip, take),
    });
  }

  getStudentRequests(skip = 0, take = DEFAULT_TAKE): Observable<BookingListResult> {
    return this.http.get<BookingListResult>(`${this.baseUrl}/requests/student`, {
      params: this.page(skip, take),
    });
  }

  approveBooking(id: number): Observable<BookingResult> {
    return this.http.post<BookingResult>(`${this.baseUrl}/requests/${id}/approve`, null);
  }

  /** Gerekçe opsiyonel; backend en fazla 500 karakter kabul eder. */
  rejectBooking(id: number, rejectionReason?: string | null): Observable<BookingResult> {
    return this.http.post<BookingResult>(`${this.baseUrl}/requests/${id}/reject`, {
      rejectionReason: rejectionReason?.trim() || null,
    });
  }

  // ---------------- Video görüşme (issue #97) ----------------

  /**
   * Onaylı bir randevu için görüşme odası oturumu üretir/alır (Teacher veya Student).
   * 409 = randevu onaylı değil ya da katılım penceresi dışında; mesaj backend'den Türkçe gelir.
   */
  getVideoSession(bookingId: number): Observable<VideoSessionResult> {
    return this.http.post<VideoSessionResult>(`${this.baseUrl}/requests/${bookingId}/video-session`, null);
  }

  /**
   * `HttpErrorResponse` gövdesindeki `ResponseBaseDto` mesajını çıkarır.
   * 409 (slot başkasına ayrılmış) için mesaj yoksa anlamlı bir varsayılan verir.
   */
  extractError(err: HttpErrorResponse, fallback: string): string {
    const body = err.error as BookingResponseBase | null;
    if (body?.message) {
      return body.message;
    }
    if (err.status === 409) {
      return 'Bu zaman aralığı için zaten bir randevu var.';
    }
    if (err.status === 403) {
      return 'Bu işlem için yetkiniz yok.';
    }
    if (err.status === 404) {
      return 'Kayıt bulunamadı.';
    }
    return fallback;
  }

  private page(skip: number, take: number): HttpParams {
    return new HttpParams().set('skip', String(skip)).set('take', String(take));
  }
}
