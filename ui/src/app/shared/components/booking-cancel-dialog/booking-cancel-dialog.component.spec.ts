import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import { BookingCancelDialogComponent, BookingCancelDialogData } from './booking-cancel-dialog.component';
import { translocoTestingModule } from '../../testing/transloco-testing';
import rootTr from '../../../../../public/i18n/tr.json';

const URL = '/api/exam/booking/requests/9/cancel';

describe('BookingCancelDialogComponent (issue #315)', () => {
  let fixture: ComponentFixture<BookingCancelDialogComponent>;
  let http: HttpTestingController;
  let dialogRef: jasmine.SpyObj<MatDialogRef<BookingCancelDialogComponent>>;
  const el = () => fixture.nativeElement as HTMLElement;
  const q = <T extends HTMLElement = HTMLElement>(id: string) => el().querySelector<T>(`[data-testid="${id}"]`);

  function create(data: Partial<BookingCancelDialogData> = {}): void {
    dialogRef = jasmine.createSpyObj('MatDialogRef', ['close']);
    TestBed.configureTestingModule({
      imports: [BookingCancelDialogComponent, NoopAnimationsModule, translocoTestingModule()],
      providers: [
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        {
          provide: MAT_DIALOG_DATA,
          useValue: { bookingId: 9, mode: 'cancel', counterpart: 'Ayşe Hoca', when: '20 Ekim · 11:00 – 12:00', ...data },
        },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(BookingCancelDialogComponent);
    fixture.detectChanges();
  }

  function type(value: string): void {
    const textarea = q<HTMLTextAreaElement>('cancel-reason')!;
    textarea.value = value;
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('cancelMode_ShowsIntroAndSendsTrimmedReason_ClosesWithResult', () => {
    create();
    expect(q('cancel-intro')?.textContent).toContain('Ayşe Hoca');
    expect(el().textContent).toContain(rootTr.shared.bookingCancelDialog.titleCancel);

    type('  Hastayım  ');
    q<HTMLButtonElement>('cancel-submit')!.click();

    const req = http.expectOne(URL);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ reason: 'Hastayım' });
    const result = { success: true, message: 'Randevu iptal edildi.' };
    req.flush(result);

    expect(dialogRef.close).toHaveBeenCalledWith(jasmine.objectContaining(result));
  });

  it('emptyReason_SendsNull', () => {
    create({ mode: 'withdraw' });
    expect(el().textContent).toContain(rootTr.shared.bookingCancelDialog.submitWithdraw);

    q<HTMLButtonElement>('cancel-submit')!.click();

    http.expectOne(URL).flush({ success: true });
    expect(dialogRef.close).toHaveBeenCalled();
  });

  it('tooLong_DisablesSubmit', () => {
    create();
    type('x'.repeat(501));

    expect(q<HTMLButtonElement>('cancel-submit')!.disabled).toBeTrue();
    expect(q('cancel-reason-too-long')).not.toBeNull();
    expect(q('cancel-reason-counter')?.textContent?.trim()).toBe('501/500');
  });

  it('serverError_ShowsServerMessage_StaysOpen', () => {
    create();
    q<HTMLButtonElement>('cancel-submit')!.click();

    http
      .expectOne(URL)
      .flush(
        { success: false, message: 'İptal süresi doldu.', errorCode: 'CancellationWindowClosed' },
        { status: 409, statusText: 'Conflict' }
      );
    fixture.detectChanges();

    expect(q('cancel-error')?.textContent).toContain('İptal süresi doldu.');
    expect(dialogRef.close).not.toHaveBeenCalled();
    expect(q<HTMLButtonElement>('cancel-submit')!.disabled).toBeFalse();
  });
});
