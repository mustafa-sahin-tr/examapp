import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import {
  BookingRescheduleDialogComponent,
  BookingRescheduleSlotSource,
  selectableRescheduleSlots,
} from './booking-reschedule-dialog.component';
import { AvailabilitySlot, Booking } from '../../../models/booking.model';
import { translocoTestingModule } from '../../testing/transloco-testing';
import rootTr from '../../../../../public/i18n/tr.json';

const HOUR = 60 * 60 * 1000;
const FUTURE = Date.now() + 10 * 24 * HOUR;

function iso(ms: number): string {
  return new Date(ms).toISOString();
}

function slot(id: number, startMs: number, overrides: Partial<AvailabilitySlot> = {}): AvailabilitySlot {
  return {
    id,
    teacherId: 4,
    date: iso(startMs).slice(0, 10),
    startTime: '11:00:00',
    endTime: '12:00:00',
    createdAt: '2030-10-01T08:00:00Z',
    startUtc: iso(startMs),
    endUtc: iso(startMs + HOUR),
    isBooked: false,
    ...overrides,
  };
}

const BOOKING: Booking = {
  id: 9,
  teacherId: 4,
  teacherName: 'Ayşe Hoca',
  studentId: 21,
  availabilitySlotId: 100,
  date: iso(FUTURE).slice(0, 10),
  startTime: '11:00:00',
  endTime: '12:00:00',
  startUtc: iso(FUTURE),
  endUtc: iso(FUTURE + HOUR),
  status: 'Approved',
  createdAt: '2030-10-01T08:00:00Z',
  canProposeReschedule: true,
};

describe('selectableRescheduleSlots (issue #315)', () => {
  it('KeepsOnlyFreeFutureOtherSameTeacherSameDurationSlots_Sorted', () => {
    const now = Date.now();
    const result = selectableRescheduleSlots(
      [
        slot(5, FUTURE + 3 * 24 * HOUR),
        slot(100, FUTURE), // mevcut slot
        slot(6, now - HOUR), // geçmiş
        slot(7, FUTURE + 24 * HOUR, { isBooked: true }),
        slot(8, FUTURE + 24 * HOUR, { teacherId: 99 }),
        slot(9, FUTURE + 2 * 24 * HOUR, { endUtc: iso(FUTURE + 2 * 24 * HOUR + 2 * HOUR) }), // farklı süre
        slot(10, FUTURE + 24 * HOUR),
      ],
      BOOKING,
      now
    );

    expect(result.map((s) => s.id)).toEqual([10, 5]);
  });
});

describe('BookingRescheduleDialogComponent (issue #315)', () => {
  let fixture: ComponentFixture<BookingRescheduleDialogComponent>;
  let http: HttpTestingController;
  let dialogRef: jasmine.SpyObj<MatDialogRef<BookingRescheduleDialogComponent>>;
  const el = () => fixture.nativeElement as HTMLElement;
  const q = <T extends HTMLElement = HTMLElement>(id: string) => el().querySelector<T>(`[data-testid="${id}"]`);

  function create(source: BookingRescheduleSlotSource): void {
    dialogRef = jasmine.createSpyObj('MatDialogRef', ['close']);
    TestBed.configureTestingModule({
      imports: [BookingRescheduleDialogComponent, NoopAnimationsModule, translocoTestingModule()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { booking: BOOKING, source, counterpart: 'Ayşe Hoca' } },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(BookingRescheduleDialogComponent);
    fixture.detectChanges();
  }

  function pickSlot(id: number): void {
    const input = el().querySelector<HTMLInputElement>(`[data-slot-id="${id}"] input`)!;
    input.click();
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('studentSource_LoadsTeacherOpenSlots_ProposesSelected', () => {
    create('teacherOpenSlots');
    http
      .expectOne((r) => r.url === '/api/exam/booking/teachers/4/slots')
      .flush({ success: true, items: [slot(100, FUTURE), slot(10, FUTURE + 24 * HOUR)] });
    fixture.detectChanges();

    expect(el().querySelector('[data-slot-id="100"]')).toBeNull();
    expect(q<HTMLButtonElement>('reschedule-submit')!.disabled).toBeTrue();

    pickSlot(10);
    q<HTMLButtonElement>('reschedule-submit')!.click();

    const req = http.expectOne('/api/exam/booking/requests/9/reschedule');
    expect(req.request.body).toEqual({ availabilitySlotId: 10 });
    req.flush({ success: true, message: 'Erteleme önerildi.' });

    expect(dialogRef.close).toHaveBeenCalledWith(jasmine.objectContaining({ success: true }));
  });

  it('teacherSource_LoadsMySlots_EmptyState', () => {
    create('mySlots');
    // getAllMySlots sayfalar: ilk sayfa boş → döngü biter.
    http.expectOne((r) => r.url === '/api/exam/booking/slots/mine').flush({ success: true, items: [] });
    fixture.detectChanges();

    expect(q('reschedule-empty')?.textContent).toContain(rootTr.shared.bookingRescheduleDialog.emptyMine);
  });

  it('slotUnavailable_ShowsMessageAndReloads', () => {
    create('teacherOpenSlots');
    http
      .expectOne((r) => r.url === '/api/exam/booking/teachers/4/slots')
      .flush({ success: true, items: [slot(10, FUTURE + 24 * HOUR)] });
    fixture.detectChanges();
    pickSlot(10);
    q<HTMLButtonElement>('reschedule-submit')!.click();

    http
      .expectOne('/api/exam/booking/requests/9/reschedule')
      .flush(
        { success: false, message: 'Seçilen saat artık uygun değil.', errorCode: 'ProposedSlotUnavailable' },
        { status: 409, statusText: 'Conflict' }
      );
    http.expectOne((r) => r.url === '/api/exam/booking/teachers/4/slots').flush({ success: true, items: [] });
    fixture.detectChanges();

    expect(q('reschedule-submit-error')?.textContent).toContain('Seçilen saat artık uygun değil.');
    expect(dialogRef.close).not.toHaveBeenCalled();
  });
});
