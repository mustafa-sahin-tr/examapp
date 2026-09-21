import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { EMPTY, Observable, defer, expand, map, toArray } from 'rxjs';
import {
  AvailabilitySlot,
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

/** `slots/mine` için backend `take` üst sınırı (issue #175: haftalık grid tüm slotları ister). */
const SLOTS_PAGE_SIZE = 200;
/** Sayfalama döngüsünün emniyet sınırı (25 x 200 = 5000 slot); backend skip'i yok saysa bile sonsuz döngü olmaz. */
const SLOTS_MAX_PAGES = 25;

/**
 * Ders planlama / randevu uçları (issue #96). Tüm çağrılar gateway üzerinden
 * (`/api/exam/booking/...`); rol kontrolü backend'de (`Teacher` / `Student`).
 * Hata gövdeleri `ResponseBaseDto` tabanlıdır — `extractError` ile okunabilir mesaja çevrilir.
 */
@Injectable({ providedIn: 'root' })
export class BookingService {
  private readonly http = inject(HttpClient);
  private readonly transloco = inject(TranslocoService);
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

  /**
   * Öğretmenin tüm slotlarını sayfa sayfa (`take=200`) çekip tek sonuçta birleştirir (issue #175, #208).
   * Boş sayfa gelene kadar devam eder; `skip` o ana kadar dönen toplam kayıt sayısıdır — böylece backend
   * `take` üst sınırını düşürse bile veri eksik kalmaz (bedeli: sonda fazladan bir boş sayfa isteği).
   * `SLOTS_MAX_PAGES` emniyet sınırıdır; sınıra dolu sayfayla ulaşılırsa `console.warn` verilir.
   * `id` ile tekilleştirir. Bir sayfa hata verirse akış `error` ile biter, kısmi sonuç dönmez
   * (tüketici mevcut hata yolunu kullanır). Sonuç bayrakları (`success`, `message`) ilk sayfadan gelir.
   */
  getAllMySlots(): Observable<AvailabilitySlotListResult> {
    return defer(() => {
      // Abonelik başına birikimli sayaç: bir sonraki sayfanın `skip` değeri.
      let fetched = 0;
      return this.getMySlots(0, SLOTS_PAGE_SIZE).pipe(
        expand((page, index) => {
          const count = page.items?.length ?? 0;
          if (count === 0) {
            return EMPTY;
          }
          if (index + 1 >= SLOTS_MAX_PAGES) {
            console.warn(`getAllMySlots: ${SLOTS_MAX_PAGES} sayfa sınırına ulaşıldı; slot listesi eksik olabilir.`);
            return EMPTY;
          }
          fetched += count;
          return this.getMySlots(fetched, SLOTS_PAGE_SIZE);
        }),
        toArray(),
        map((pages): AvailabilitySlotListResult => {
          const byId = new Map<number, AvailabilitySlot>();
          for (const page of pages) {
            for (const slot of page.items ?? []) {
              if (!byId.has(slot.id)) {
                byId.set(slot.id, slot);
              }
            }
          }
          return { ...pages[0], items: [...byId.values()] };
        })
      );
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
      return this.t('common.errors.bookingConflict');
    }
    if (err.status === 403) {
      return this.t('common.errors.forbidden');
    }
    if (err.status === 404) {
      return this.t('common.errors.notFound');
    }
    return fallback;
  }

  private t(key: string): string {
    return this.transloco.translate<string>(key) ?? '';
  }

  private page(skip: number, take: number): HttpParams {
    return new HttpParams().set('skip', String(skip)).set('take', String(take));
  }
}
