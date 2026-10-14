import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { BookingSlotDialogComponent, BookingSlotDialogData, STUDENT_PARENTS_ROUTE } from './booking-slot-dialog.component';
import { AvailabilitySlot, BookingResult } from '../../../models/booking.model';
import { BookingService } from '../../../services/booking.service';
import { translocoTestingModule } from '../../testing/transloco-testing';

/** Issue #96 slot seçimi + issue #439: veli modu ve 403 ParentRequired. */
describe('BookingSlotDialogComponent (issue #439)', () => {
  let service: jasmine.SpyObj<BookingService>;
  let dialogRef: jasmine.SpyObj<MatDialogRef<BookingSlotDialogComponent>>;
  let fixture: ComponentFixture<BookingSlotDialogComponent>;

  const slot: AvailabilitySlot = {
    id: 55,
    teacherId: 4,
    date: '2026-10-20',
    startTime: '11:00:00',
    endTime: '12:00:00',
    createdAt: '2026-10-10T08:00:00Z',
    startUtc: '2026-10-20T11:00:00Z',
    endUtc: '2026-10-20T12:00:00Z',
    isBooked: false,
  };

  function setup(data: BookingSlotDialogData): HTMLElement {
    service = jasmine.createSpyObj<BookingService>('BookingService', [
      'getTeacherSlots',
      'createBooking',
      'createBookingForChild',
      'extractError',
    ]);
    service.getTeacherSlots.and.returnValue(of({ success: true, items: [slot] }));
    service.extractError.and.callFake((_err: HttpErrorResponse, fallback: string) => fallback);
    dialogRef = jasmine.createSpyObj<MatDialogRef<BookingSlotDialogComponent>>('MatDialogRef', ['close']);

    TestBed.configureTestingModule({
      imports: [BookingSlotDialogComponent, NoopAnimationsModule, translocoTestingModule()],
      providers: [
        provideRouter([]),
        { provide: BookingService, useValue: service },
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: MAT_DIALOG_DATA, useValue: data },
      ],
    });
    fixture = TestBed.createComponent(BookingSlotDialogComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function selectAndSubmit(el: HTMLElement): void {
    (el.querySelector('mat-radio-button input') as HTMLInputElement).click();
    fixture.detectChanges();
    const buttons = el.querySelectorAll('mat-dialog-actions button');
    (buttons[buttons.length - 1] as HTMLButtonElement).click();
    fixture.detectChanges();
  }

  const text = (el: HTMLElement, sel: string) => el.querySelector(sel)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

  it('Student_AwaitingParent_ClosesWithServerResult', () => {
    const el = setup({ teacherId: 4, teacherName: 'Elif Hoca' });
    const result: BookingResult = {
      success: true,
      message: '',
      booking: { status: 'AwaitingParent' } as BookingResult['booking'],
    };
    service.createBooking.and.returnValue(of(result));

    selectAndSubmit(el);

    expect(service.createBooking).toHaveBeenCalledOnceWith({ availabilitySlotId: 55 });
    expect(service.createBookingForChild).not.toHaveBeenCalled();
    expect(dialogRef.close).toHaveBeenCalledOnceWith(result);
    expect(el.querySelector('[data-test="for-child"]')).toBeNull();
  });

  it('Student_ParentRequired_ShowsMessageWithLinkToParentsPage', () => {
    const el = setup({ teacherId: 4, teacherName: 'Elif Hoca' });
    service.createBooking.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 403, error: { success: false, message: 'x', errorCode: 'ParentRequired' } }))
    );

    selectAndSubmit(el);

    const box = el.querySelector('[data-test="parent-required"]') as HTMLElement;
    expect(box).not.toBeNull();
    expect(box.textContent).toContain('Ders talebi için hesabına bağlı bir veli gerekli.');
    expect(box.querySelector('a')?.getAttribute('href')).toBe(STUDENT_PARENTS_ROUTE);
    expect(dialogRef.close).not.toHaveBeenCalled();
    // Slot listesi tazelenmez (yalnız 409/404'te).
    expect(service.getTeacherSlots).toHaveBeenCalledTimes(1);
  });

  it('Student_TooManyAwaitingParent_ShowsExplanation_NoSlotReload', () => {
    const el = setup({ teacherId: 4, teacherName: 'Elif Hoca' });
    service.createBooking.and.returnValue(
      throwError(
        () => new HttpErrorResponse({ status: 409, error: { success: false, message: 'x', errorCode: 'TooManyAwaitingParent' } })
      )
    );

    selectAndSubmit(el);

    expect(text(el, '.bsd__error')).toBe(
      'Velinin onayını bekleyen çok fazla talebin var. Velin mevcut taleplerine karar verince yenisini oluşturabilirsin.'
    );
    expect(service.getTeacherSlots).toHaveBeenCalledTimes(1);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('Student_Other403_ShowsGenericError', () => {
    const el = setup({ teacherId: 4, teacherName: 'Elif Hoca' });
    service.createBooking.and.returnValue(throwError(() => new HttpErrorResponse({ status: 403, error: null })));

    selectAndSubmit(el);

    expect(el.querySelector('[data-test="parent-required"]')).toBeNull();
    expect(text(el, '.bsd__error')).toBe('Randevu talebi oluşturulamadı.');
  });

  it('ChildMode_PostsForChild_ShowsChildAndChildSubmitLabel', () => {
    const el = setup({ teacherId: 4, teacherName: 'Elif Hoca', child: { studentId: 21, name: 'Ayşe' } });
    const result: BookingResult = { success: true, message: '', booking: { status: 'Pending' } as BookingResult['booking'] };
    service.createBookingForChild.and.returnValue(of(result));

    expect(text(el, '[data-test="for-child"]')).toContain('Ayşe adına talep');
    const buttons = el.querySelectorAll('mat-dialog-actions button');
    expect(buttons[buttons.length - 1].textContent?.trim()).toBe('Talebi Öğretmene Gönder');

    selectAndSubmit(el);

    expect(service.createBookingForChild).toHaveBeenCalledOnceWith(21, { availabilitySlotId: 55 });
    expect(service.createBooking).not.toHaveBeenCalled();
    expect(dialogRef.close).toHaveBeenCalledOnceWith(result);
  });

  it('ChildMode_Conflict_ReloadsSlots', () => {
    const el = setup({ teacherId: 4, teacherName: 'Elif Hoca', child: { studentId: 21, name: 'Ayşe' } });
    service.createBookingForChild.and.returnValue(throwError(() => new HttpErrorResponse({ status: 409, error: {} })));

    selectAndSubmit(el);

    expect(service.getTeacherSlots).toHaveBeenCalledTimes(2);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });
});
