import { HttpErrorResponse } from '@angular/common/http';
import { Observable, catchError, map, of, throwError } from 'rxjs';

import type { BookingSlotDialogChild } from '../components/booking-slot-dialog/booking-slot-dialog.component';
import type { LinkedChild } from '../../models/parent-link.model';

/**
 * Issue #439 — veli doğrudan seçim: öğretmen arama/profil sayfaları `/my-children/:studentId/tutors[/:id]` altında "çocuk adına"
 * modda çalışır. Rota parametresinin adı.
 */
export const BOOKING_CHILD_ROUTE_PARAM = 'studentId';

/** Veli modunda arama sayfasının rotası: `/my-children/<studentId>/tutors`. */
export function childTutorSearchCommands(studentId: number): (string | number)[] {
  return ['/my-children', studentId, 'tutors'];
}

/** Veli modunda tekil öğretmen profilinin rotası: `/my-children/<studentId>/tutors/<teacherId>`. */
export function childTutorProfileCommands(studentId: number, teacherId: number): (string | number)[] {
  return [...childTutorSearchCommands(studentId), teacherId];
}

/** Rota parametresini pozitif tamsayıya çevirir; yoksa/geçersizse null (öğrenci modu). */
export function parseBookingChildParam(value: string | null | undefined): number | null {
  if (!value || !/^\d+$/.test(value)) return null;
  const id = Number(value);
  return Number.isSafeInteger(id) && id > 0 ? id : null;
}

/**
 * Velinin bağlı çocukları arasından bu `studentId`'li Active çocuğu bulur (talep yalnız Active bağlantıda açılabilir; sunucu da
 * bağlı olmayan çocukta 404 döner). Bulunamazsa null — sayfa "bu çocuk için talep açılamaz" durumunu gösterir.
 */
export function findBookingChild(
  children: readonly LinkedChild[] | null | undefined,
  studentId: number,
  fallbackName: string
): BookingSlotDialogChild | null {
  const child = (children ?? []).find((c) => c.status === 'Active' && c.studentId === studentId);
  return child ? { studentId, name: child.studentName?.trim() || fallbackName } : null;
}

/**
 * `getMyChildren()` akışını çocuk bağlamına çevirir. 404 "çocuk yok" sayılır (null); diğer hatalar çağırana gider (tekrar dene).
 */
export function resolveBookingChild(
  children$: Observable<LinkedChild[]>,
  studentId: number,
  fallbackName: string
): Observable<BookingSlotDialogChild | null> {
  return children$.pipe(
    map((children) => findBookingChild(children, studentId, fallbackName)),
    catchError((err: HttpErrorResponse) => (err?.status === 404 ? of(null) : throwError(() => err)))
  );
}
