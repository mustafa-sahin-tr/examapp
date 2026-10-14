import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { ActivatedRoute } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { BehaviorSubject, Observable, Subject, isObservable, of, throwError } from 'rxjs';

import { ParentBookingRequestsComponent } from './parent-booking-requests.component';
import { BookingService } from '../../../services/booking.service';
import { LocaleService } from '../../../services/locale.service';
import { BookingResult, ParentBookingRequest, ParentBookingRequestList } from '../../../models/booking.model';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import parentDashboardTr from '../../../../../public/i18n/parent-dashboard/tr.json';
import parentDashboardEn from '../../../../../public/i18n/parent-dashboard/en.json';

/** Issue #439: veli panelindeki "Onay bekleyen ders talepleri" kartı. */
describe('ParentBookingRequestsComponent (issue #439)', () => {
  let service: jasmine.SpyObj<BookingService>;
  let snackBar: jasmine.SpyObj<MatSnackBar>;
  let fixture: ComponentFixture<ParentBookingRequestsComponent>;
  /** Issue #439 review: rota fragment akışı (panel açıkken bildirim tıklaması). */
  let fragment$: BehaviorSubject<string | null>;

  function request(overrides: Partial<ParentBookingRequest> = {}): ParentBookingRequest {
    return {
      id: 7,
      studentId: 21,
      studentName: 'Ayşe Kaya',
      teacherId: 4,
      teacherName: 'Elif Hoca',
      availabilitySlotId: 55,
      date: '2026-10-20',
      startTime: '11:00:00',
      endTime: '12:00:00',
      startUtc: '2026-10-20T11:00:00Z',
      endUtc: '2026-10-20T12:00:00Z',
      createdAt: '2026-10-14T08:00:00Z',
      expiresAt: '2026-10-21T08:00:00Z',
      ...overrides,
    };
  }

  const ok: BookingResult = { success: true, message: '' };

  function conflict(errorCode: string, message = ''): HttpErrorResponse {
    return new HttpErrorResponse({ status: 409, error: { message, errorCode } });
  }

  async function setup(
    response: ParentBookingRequestList | Observable<ParentBookingRequestList> = { items: [request()] },
    lang = 'tr',
    initialFragment: string | null = null
  ): Promise<HTMLElement> {
    fragment$ = new BehaviorSubject<string | null>(initialFragment);
    service = jasmine.createSpyObj<BookingService>('BookingService', [
      'getParentBookingRequests',
      'parentApproveBooking',
      'parentRejectBooking',
      'extractError',
    ]);
    service.getParentBookingRequests.and.returnValue(isObservable(response) ? response : of(response));
    service.extractError.and.callFake((err: HttpErrorResponse, fallback: string) =>
      (err.error as { message?: string } | null)?.message || fallback
    );
    snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);

    TestBed.configureTestingModule({
      imports: [
        ParentBookingRequestsComponent,
        NoopAnimationsModule,
        translocoTestingModule({
          langs: { 'parent-dashboard/tr': parentDashboardTr, 'parent-dashboard/en': parentDashboardEn },
        }),
      ],
      providers: [
        { provide: BookingService, useValue: service },
        { provide: ActivatedRoute, useValue: { snapshot: { fragment: initialFragment }, fragment: fragment$ } },
        { provide: LocaleService, useValue: { localeDefinition: signal({ angularLocale: lang === 'tr' ? 'tr-TR' : 'en-US' }) } },
      ],
    });
    TestBed.overrideProvider(MatSnackBar, { useValue: snackBar });
    TestBed.inject(TranslocoService).setActiveLang(lang);
    fixture = TestBed.createComponent(ParentBookingRequestsComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const q = (el: HTMLElement, sel: string) => el.querySelector(sel) as HTMLElement | null;
  const text = (el: HTMLElement, sel: string) => q(el, sel)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const click = (el: HTMLElement, sel: string) => {
    q(el, sel)!.click();
    fixture.detectChanges();
  };

  it('Rows_ShowChildTeacherTimeAndExpiry', async () => {
    const el = await setup();

    expect(el.querySelectorAll('[data-test="booking-request-row"]').length).toBe(1);
    const row = text(el, '[data-test="booking-request-row"]');
    expect(row).toContain('Ayşe Kaya');
    expect(row).toContain('Elif Hoca');
    expect(text(el, '[data-test="booking-request-expires"]')).toContain('Son karar:');
    expect(text(el, '#pbr-title')).toBe('Onay bekleyen ders talepleri');
  });

  it('Empty_ShowsShortText', async () => {
    const el = await setup({ items: [] });

    expect(text(el, '[data-test="booking-requests-empty"]')).toBe('Onay bekleyen ders talebi yok.');
    expect(q(el, '[data-test="booking-request-row"]')).toBeNull();
  });

  it('Loading_ShowsSpinner', async () => {
    const el = await setup(new Subject<ParentBookingRequestList>());

    expect(q(el, '[data-test="booking-requests-loading"]')).not.toBeNull();
  });

  it('LoadError_ShowsMessage_RetryReloads', async () => {
    const el = await setup(throwError(() => new HttpErrorResponse({ status: 500 })));

    expect(text(el, '[data-test="booking-requests-error"]')).toContain('Ders talepleri yüklenemedi.');
    service.getParentBookingRequests.and.returnValue(of({ items: [request()] }));
    click(el, '[data-test="booking-requests-error"] button');

    expect(service.getParentBookingRequests).toHaveBeenCalledTimes(2);
    expect(el.querySelectorAll('[data-test="booking-request-row"]').length).toBe(1);
  });

  it('Approve_Success_RemovesRowAndToasts', async () => {
    const el = await setup({ items: [request(), request({ id: 8, studentName: 'Mert' })] });
    service.parentApproveBooking.and.returnValue(of(ok));

    click(el, '[data-test="booking-request-approve"]');

    expect(service.parentApproveBooking).toHaveBeenCalledOnceWith(7);
    expect(el.querySelectorAll('[data-test="booking-request-row"]').length).toBe(1);
    expect(snackBar.open).toHaveBeenCalledWith('Talep onaylandı ve öğretmene gönderildi.', 'Tamam', jasmine.any(Object));
  });

  it('Reject_WithNote_SendsTrimmedNoteAndRemovesRow', async () => {
    const el = await setup();
    service.parentRejectBooking.and.returnValue(of(ok));

    click(el, '[data-test="booking-request-reject"]');
    const textarea = q(el, '[data-test="booking-request-note"]') as HTMLTextAreaElement;
    expect(textarea.maxLength).toBe(500);
    textarea.value = '  Bu hafta sınav var  ';
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    click(el, '[data-test="booking-request-confirm-reject"]');

    expect(service.parentRejectBooking).toHaveBeenCalledOnceWith(7, 'Bu hafta sınav var');
    expect(q(el, '[data-test="booking-request-row"]')).toBeNull();
    expect(snackBar.open).toHaveBeenCalledWith('Talep reddedildi.', 'Tamam', jasmine.any(Object));
  });

  it('Reject_WithoutNote_SendsNull', async () => {
    const el = await setup();
    service.parentRejectBooking.and.returnValue(of(ok));

    click(el, '[data-test="booking-request-reject"]');
    click(el, '[data-test="booking-request-confirm-reject"]');

    expect(service.parentRejectBooking).toHaveBeenCalledOnceWith(7, null);
  });

  it('Approve_AlreadyDecided_ShowsNoticeAndReloads', async () => {
    const el = await setup();
    service.parentApproveBooking.and.returnValue(throwError(() => conflict('AlreadyDecided')));
    service.getParentBookingRequests.and.returnValue(of({ items: [] }));

    click(el, '[data-test="booking-request-approve"]');

    expect(service.getParentBookingRequests).toHaveBeenCalledTimes(2);
    expect(text(el, '[data-test="booking-requests-notice"]')).toContain('artık karar beklemiyor');
    expect(q(el, '[data-test="booking-request-row"]')).toBeNull();
    expect(snackBar.open).not.toHaveBeenCalled();
  });

  it('Reject_Expired_UsesServerMessageAndReloads', async () => {
    const el = await setup();
    service.parentRejectBooking.and.returnValue(
      throwError(() => conflict('BookingRequestExpired', 'Talebin süresi doldu.'))
    );

    click(el, '[data-test="booking-request-reject"]');
    click(el, '[data-test="booking-request-confirm-reject"]');

    expect(service.getParentBookingRequests).toHaveBeenCalledTimes(2);
    expect(text(el, '[data-test="booking-requests-notice"]')).toBe('Talebin süresi doldu.');
  });

  it('Approve_OtherError_KeepsRowAndShowsNotice', async () => {
    const el = await setup();
    service.parentApproveBooking.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

    click(el, '[data-test="booking-request-approve"]');

    expect(service.getParentBookingRequests).toHaveBeenCalledTimes(1);
    expect(text(el, '[data-test="booking-requests-notice"]')).toBe('İşlem tamamlanamadı.');
    expect(el.querySelectorAll('[data-test="booking-request-row"]').length).toBe(1);
  });

  it('WhileDeciding_ButtonsDisabled', async () => {
    const el = await setup();
    service.parentApproveBooking.and.returnValue(new Subject<BookingResult>());

    click(el, '[data-test="booking-request-approve"]');

    expect((q(el, '[data-test="booking-request-approve"]') as HTMLButtonElement).disabled).toBeTrue();
    expect((q(el, '[data-test="booking-request-reject"]') as HTMLButtonElement).disabled).toBeTrue();
  });

  it('FragmentArrivesWhileOpen_ReloadsAndScrollsToCard; otherFragment_Ignored', async () => {
    const el = await setup({ items: [] });
    document.body.appendChild(el);
    const scroll = spyOn(HTMLElement.prototype, 'scrollIntoView');
    service.getParentBookingRequests.and.returnValue(of({ items: [request()] }));

    fragment$.next('other');
    expect(service.getParentBookingRequests).toHaveBeenCalledTimes(1);

    fragment$.next('booking-requests');
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve));

    expect(service.getParentBookingRequests).toHaveBeenCalledTimes(2);
    expect(el.querySelectorAll('[data-test="booking-request-row"]').length).toBe(1);
    expect(scroll).toHaveBeenCalledTimes(1);
    el.remove();
  });

  it('InitialFragment_LoadsOnceAndScrollsOnce', async () => {
    const scroll = spyOn(HTMLElement.prototype, 'scrollIntoView');
    const el = await setup({ items: [request()] }, 'tr', 'booking-requests');
    await new Promise((resolve) => setTimeout(resolve));

    // BehaviorSubject'in ilk değeri (snapshot ile aynı) ikinci yükleme tetiklemez.
    expect(service.getParentBookingRequests).toHaveBeenCalledTimes(1);
    expect(el.querySelector('#booking-requests')).not.toBeNull();
    expect(scroll).toHaveBeenCalledTimes(1);
  });

  it('English_RendersEnglishTitle', async () => {
    const el = await setup({ items: [] }, 'en');

    expect(text(el, '#pbr-title')).toBe('Lesson requests awaiting approval');
  });
});
