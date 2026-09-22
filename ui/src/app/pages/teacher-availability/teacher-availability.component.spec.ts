import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { By } from '@angular/platform-browser';
import { Subject, of, throwError } from 'rxjs';

import { TeacherAvailabilityComponent } from './teacher-availability.component';
import { BookingService } from '../../services/booking.service';
import { AvailabilitySlot, AvailabilitySlotResult } from '../../models/booking.model';
import { AvailabilityWeekGridComponent } from '../../shared/components/availability-week-grid/availability-week-grid.component';
import { safeLocalHour, utcRequest } from '../../shared/testing/booking-time-testing';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import { HttpErrorResponse } from '@angular/common/http';
import taTr from '../../../../public/i18n/teacher-availability/tr.json';
import taEn from '../../../../public/i18n/teacher-availability/en.json';

// Scope sözlüğü gerçek dosyadan yüklenir; snackbar/çubuk metinleri ham anahtar yerine gerçek çeviriyle doğrulanır.
const translocoTesting = translocoTestingModule({
  langs: { 'teacher-availability/tr': taTr, 'teacher-availability/en': taEn },
});

describe('TeacherAvailabilityComponent', () => {
  let component: TeacherAvailabilityComponent;
  let fixture: ComponentFixture<TeacherAvailabilityComponent>;
  let bookingService: jasmine.SpyObj<BookingService>;
  let snackBar: jasmine.SpyObj<MatSnackBar>;

  const mockSlot: AvailabilitySlot = {
    id: 1,
    teacherId: 10,
    date: '2026-09-20',
    startTime: '14:00:00',
    endTime: '15:00:00',
    createdAt: '2026-09-15T10:00:00Z',
    startUtc: '2026-09-20T12:00:00Z',
    endUtc: '2026-09-20T13:00:00Z',
    isBooked: false,
  };

  beforeEach(async () => {
    bookingService = jasmine.createSpyObj<BookingService>('BookingService', [
      'getAllMySlots',
      'createSlot',
      'deleteSlot',
      'extractError',
    ]);
    bookingService.getAllMySlots.and.returnValue(of({ items: [mockSlot], success: true }));
    bookingService.createSlot.and.returnValue(of({ success: true, slot: mockSlot }));
    bookingService.deleteSlot.and.returnValue(of(undefined));
    bookingService.extractError.and.returnValue('Error message');

    snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);

    await TestBed.configureTestingModule({
      imports: [TeacherAvailabilityComponent, HttpClientTestingModule, translocoTesting, BrowserAnimationsModule],
      providers: [
        { provide: BookingService, useValue: bookingService },
        { provide: MatSnackBar, useValue: snackBar },
        provideRouter([]),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TeacherAvailabilityComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should call getAllMySlots on init', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    expect(bookingService.getAllMySlots).toHaveBeenCalled();
  }));

  it('should render page title', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const title = fixture.nativeElement.querySelector('h1');
    expect(title).toBeTruthy();
  }));

  it('should render grid component when no error', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const grid = fixture.nativeElement.querySelector('app-availability-week-grid');
    expect(grid).toBeTruthy();
  }));

  it('should pass slots from service to grid component', fakeAsync(() => {
    const slots = [
      mockSlot,
      {
        id: 2,
        teacherId: 10,
        date: '2026-09-21',
        startTime: '15:00:00',
        endTime: '16:00:00',
        createdAt: '2026-09-15T10:00:00Z',
        startUtc: '2026-09-21T13:00:00Z',
        endUtc: '2026-09-21T14:00:00Z',
        isBooked: false,
      },
    ];
    bookingService.getAllMySlots.and.returnValue(of({ items: slots, success: true }));

    fixture.detectChanges();
    tick();

    // Grid'in `slots` input'u servisten dönen diziyle aynı olmalı (yalnızca sayfanın kendi sinyali değil).
    const grid = fixture.debugElement.query(By.directive(AvailabilityWeekGridComponent));
    expect(grid).toBeTruthy();
    expect((grid.componentInstance as AvailabilityWeekGridComponent).slots()).toEqual(slots);
  }));

  it('should render grid when error occurs but previous data exists', fakeAsync(() => {
    bookingService.getAllMySlots.and.returnValue(of({ items: [mockSlot], success: true }));

    fixture.detectChanges();
    tick();

    expect(fixture.nativeElement.querySelector('app-availability-week-grid')).toBeTruthy();

    // Now simulate error on reload
    const errorResponse = new HttpErrorResponse({ status: 500, statusText: 'Server Error' });
    bookingService.getAllMySlots.and.returnValue(throwError(() => errorResponse));

    // Trigger load again
    component['load']();
    tick();
    fixture.detectChanges();

    // Hata gerçekten render edildi VE grid önceki veriyle yerinde kaldı.
    expect(fixture.nativeElement.querySelector('.avail__state--error')).toBeTruthy();
    const gridDe = fixture.debugElement.query(By.directive(AvailabilityWeekGridComponent));
    expect(gridDe).toBeTruthy();
    expect((gridDe.componentInstance as AvailabilityWeekGridComponent).slots()).toEqual([mockSlot]);
  }));

  it('should not render grid when initial load error and no previous data', fakeAsync(() => {
    const errorResponse = new HttpErrorResponse({ status: 500, statusText: 'Server Error' });
    bookingService.getAllMySlots.and.returnValue(throwError(() => errorResponse));
    bookingService.extractError.and.returnValue('Failed to load slots');

    fixture.detectChanges();
    tick();

    const grid = fixture.nativeElement.querySelector('app-availability-week-grid');
    expect(grid).toBeFalsy();
  }));

  it('should show error message when load fails', fakeAsync(() => {
    const errorResponse = new HttpErrorResponse({ status: 500, statusText: 'Server Error' });
    bookingService.getAllMySlots.and.returnValue(throwError(() => errorResponse));
    bookingService.extractError.and.returnValue('Failed to load slots');

    fixture.detectChanges();
    tick();

    const errorBlock = fixture.nativeElement.querySelector('.avail__state--error');
    expect(errorBlock).toBeTruthy();
  }));

  it('should show retry button in error state', fakeAsync(() => {
    const errorResponse = new HttpErrorResponse({ status: 500, statusText: 'Server Error' });
    bookingService.getAllMySlots.and.returnValue(throwError(() => errorResponse));
    bookingService.extractError.and.returnValue('Failed to load slots');

    fixture.detectChanges();
    tick();

    const errorBlock = fixture.nativeElement.querySelector('.avail__state--error');
    const retryButton = errorBlock?.querySelector('button');
    expect(retryButton).toBeTruthy();
  }));

  it('should show empty state when no slots', fakeAsync(() => {
    bookingService.getAllMySlots.and.returnValue(of({ items: [], success: true }));

    fixture.detectChanges();
    tick();

    const emptyIcon = fixture.nativeElement.querySelector('.avail__empty-icon');
    expect(emptyIcon).toBeTruthy();
  }));

  it('should render list items for each slot', fakeAsync(() => {
    const slots = [
      mockSlot,
      {
        id: 2,
        teacherId: 10,
        date: '2026-09-21',
        startTime: '14:00:00',
        endTime: '15:00:00',
        createdAt: '2026-09-15T10:00:00Z',
        startUtc: '2026-09-21T12:00:00Z',
        endUtc: '2026-09-21T13:00:00Z',
        isBooked: false,
      },
    ];
    bookingService.getAllMySlots.and.returnValue(of({ items: slots, success: true }));

    fixture.detectChanges();
    tick();

    const listItems = fixture.nativeElement.querySelectorAll('.avail__row');
    expect(listItems.length).toBe(2);
  }));

  it('liste salt okunur: satırlarda silme butonu yok (issue #177 silmeyi grid\'e taşıdı)', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    expect(fixture.nativeElement.querySelectorAll('.avail__row').length).toBe(1);
    expect(fixture.nativeElement.querySelector('.avail__row button')).toBeNull();
    expect(fixture.nativeElement.querySelector('.avail__locked')).toBeNull();
  }));

  it('should show locked icon for booked slots', fakeAsync(() => {
    const bookedSlot: AvailabilitySlot = {
      id: 1,
      teacherId: 10,
      date: '2026-09-20',
      startTime: '14:00:00',
      endTime: '15:00:00',
      createdAt: '2026-09-15T10:00:00Z',
      startUtc: '2026-09-20T12:00:00Z',
      endUtc: '2026-09-20T13:00:00Z',
      isBooked: true,
      bookingStatus: 'Approved',
    };
    bookingService.getAllMySlots.and.returnValue(of({ items: [bookedSlot], success: true }));

    fixture.detectChanges();
    tick();

    const lockedIcon = fixture.nativeElement.querySelector('.avail__locked');
    expect(lockedIcon).toBeTruthy();
  }));

  it('should display booking requests button', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const button = fixture.nativeElement.querySelector('a[routerLink="/booking-requests"]');
    expect(button).toBeTruthy();
  }));

  it('should display slot information in list', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const daySpan = fixture.nativeElement.querySelector('.avail__row-day');
    expect(daySpan?.textContent).toBeTruthy();
  }));

  it('should display time range in list', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const timeRange = fixture.nativeElement.querySelector('.avail__row-range');
    expect(timeRange?.textContent).toContain('–');
  }));

  it('should display status chip', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const chip = fixture.nativeElement.querySelector('.avail__chip');
    expect(chip).toBeTruthy();
  }));

  describe('grid üzerinden aralık silme (issue #177)', () => {
    // Görünen haftada, dilimden bağımsız saatte: Cuma H:00–(H+1):00 boş; Cuma (H+2)–(H+3) bekleyen randevulu.
    const friday = (hour: number) => {
      const now = new Date();
      const day = now.getDay();
      const monday = new Date(now);
      monday.setDate(now.getDate() - day + (day === 0 ? -6 : 1));
      monday.setHours(0, 0, 0, 0);
      const d = new Date(monday);
      d.setDate(monday.getDate() + 4);
      d.setHours(hour, 0, 0, 0);
      return d;
    };
    const H = safeLocalHour(friday(12));
    const slotAt = (id: number, startHour: number, isBooked = false): AvailabilitySlot => ({
      id,
      teacherId: 10,
      date: friday(startHour).toISOString().slice(0, 10),
      startTime: friday(startHour).toISOString().slice(11, 19),
      endTime: friday(startHour + 1).toISOString().slice(11, 19),
      createdAt: '2026-09-15T10:00:00Z',
      startUtc: friday(startHour).toISOString(),
      endUtc: friday(startHour + 1).toISOString(),
      isBooked,
      bookingStatus: isBooked ? 'Pending' : null,
    });
    const freeSlot = slotAt(11, H);
    const bookedSlot = slotAt(12, H + 2, true);

    function render(): void {
      fixture.detectChanges();
      tick();
      fixture.detectChanges();
    }

    function grid(): AvailabilityWeekGridComponent {
      return fixture.debugElement.query(By.directive(AvailabilityWeekGridComponent))
        .componentInstance as AvailabilityWeekGridComponent;
    }

    function confirmButton(): HTMLButtonElement | null {
      return fixture.nativeElement.querySelector('.awg__delete-confirm') as HTMLButtonElement | null;
    }

    function alertText(): string | null {
      const alert = fixture.nativeElement.querySelector('.awg__draft [role="alert"]') as HTMLElement | null;
      return alert ? (alert.textContent ?? '').trim() : null;
    }

    /** Sayfa yüklenir, grid'de boş slota (11) tıklanır → silme modu. */
    function selectFreeSlot(): void {
      bookingService.getAllMySlots.and.returnValue(of({ items: [freeSlot, bookedSlot], success: true }));
      render();
      grid()['onEventClick']('11');
      render();
    }

    it('slota tıklama grid\'i silme moduna alır; Sil → deleteSlot(id) bir kez çağrılır', fakeAsync(() => {
      selectFreeSlot();
      expect(confirmButton()).not.toBeNull();
      expect(grid().selectedSlotId()).toBe(11);

      confirmButton()!.click();
      render();

      expect(bookingService.deleteSlot).toHaveBeenCalledOnceWith(11);
    }));

    it('başarıda snackbar, slot listeden ve grid\'den düşer, seçim kapanır, yeniden yükleme yapılmaz', fakeAsync(() => {
      selectFreeSlot();
      expect(fixture.nativeElement.querySelectorAll('.avail__row').length).toBe(2);

      confirmButton()!.click();
      render();

      expect(snackBar.open).toHaveBeenCalledOnceWith(taTr.messages.deleted, taTr.messages.ok, { duration: 3000 });
      expect(component['slots']().map((s) => s.id)).toEqual([12]);
      expect(grid().slots().map((s) => s.id)).toEqual([12]);
      expect(fixture.nativeElement.querySelectorAll('.avail__row').length).toBe(1);
      expect(fixture.nativeElement.querySelectorAll('.fc-timegrid-event').length).toBe(1);
      expect(grid().selectedSlotId()).toBeNull();
      expect(confirmButton()).toBeNull();
      expect(alertText()).toBeNull();
      expect(bookingService.getAllMySlots).toHaveBeenCalledTimes(1);
    }));

    it('sunucu reddi (randevusu var) grid çubuğunda gösterilir; seçim ve slot korunur, snackbar yok', fakeAsync(() => {
      selectFreeSlot();
      const rejected = new HttpErrorResponse({
        status: 400,
        error: { success: false, message: 'Aktif randevusu olan aralık silinemez.' },
      });
      bookingService.deleteSlot.and.returnValue(throwError(() => rejected));
      bookingService.extractError.and.returnValue('Aktif randevusu olan aralık silinemez.');

      confirmButton()!.click();
      render();

      expect(bookingService.extractError).toHaveBeenCalledOnceWith(rejected, taTr.messages.deleteFailed);
      expect(alertText()).toBe('Aktif randevusu olan aralık silinemez.');
      expect(grid().selectedSlotId()).toBe(11);
      expect(component['slots']().length).toBe(2);
      expect(confirmButton()?.getAttribute('aria-disabled')).toBeNull();
      expect(snackBar.open).not.toHaveBeenCalled();
    }));

    it('409 çakışmada da hata grid\'e düşer ve seçim değişince temizlenir', fakeAsync(() => {
      selectFreeSlot();
      bookingService.deleteSlot.and.returnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
      confirmButton()!.click();
      render();
      expect(alertText()).toBe('Error message');

      // Vazgeç → seçim null → sayfa hatayı temizler.
      (fixture.nativeElement.querySelector('.awg__delete-cancel') as HTMLButtonElement).click();
      render();

      expect(alertText()).toBeNull();
      expect(grid().selectedSlotId()).toBeNull();
      expect(component['deleteError']()).toBeNull();
    }));

    it('seçili slot yeniden yüklemede listeden düşerse grid seçimi null yayar ve sayfa deleteError\'ı temizler', fakeAsync(() => {
      selectFreeSlot();
      bookingService.deleteSlot.and.returnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
      confirmButton()!.click();
      render();
      expect(alertText()).toBe('Error message');

      bookingService.getAllMySlots.and.returnValue(of({ items: [bookedSlot], success: true }));
      component['load']();
      render();

      expect(component['selectedSlotId']()).toBeNull();
      expect(component['deleteError']()).toBeNull();
      expect(alertText()).toBeNull();
      expect(confirmButton()).toBeNull();
    }));

    it('randevulu slota tıklama silme moduna girmez ve deleteSlot çağrılmaz', fakeAsync(() => {
      bookingService.getAllMySlots.and.returnValue(of({ items: [freeSlot, bookedSlot], success: true }));
      render();

      grid()['onEventClick']('12');
      render();

      expect(confirmButton()).toBeNull();
      expect(fixture.nativeElement.querySelector('.awg__locked-hint')?.textContent?.trim()).toBe(taTr.actions.lockedHint);
      expect(bookingService.deleteSlot).not.toHaveBeenCalled();
    }));

    it('silme sürerken butonlar aria-disabled olur ve ikinci gönderim yapılmaz', fakeAsync(() => {
      selectFreeSlot();
      const pending = new Subject<void>();
      bookingService.deleteSlot.and.returnValue(pending.asObservable());

      confirmButton()!.click();
      render();

      expect(confirmButton()?.getAttribute('aria-disabled')).toBe('true');
      expect(fixture.nativeElement.querySelector('.awg__delete-cancel')?.getAttribute('aria-disabled')).toBe('true');
      expect(grid().deleting()).toBeTrue();

      confirmButton()!.click();
      component['deleteSlot'](11);
      render();
      expect(bookingService.deleteSlot).toHaveBeenCalledTimes(1);

      pending.next();
      pending.complete();
      render();

      expect(confirmButton()).toBeNull();
      expect(grid().deleting()).toBeFalse();
      expect(component['slots']().map((s) => s.id)).toEqual([12]);
    }));
  });

  describe('grid üzerinden aralık oluşturma (issue #176)', () => {
    const createdSlot: AvailabilitySlot = {
      id: 7,
      teacherId: 10,
      date: '2026-09-25',
      startTime: '14:00:00',
      endTime: '15:00:00',
      createdAt: '2026-09-23T09:00:00Z',
      startUtc: '2026-09-25T14:00:00Z',
      endUtc: '2026-09-25T15:00:00Z',
      isBooked: false,
    };
    // Cum 25 Eylül 2026, yerel H:00–(H+1):00; H dilime göre UTC gün sınırından uzak seçilir.
    const H = safeLocalHour(new Date(2026, 8, 25, 12, 0));
    const draftStart = new Date(2026, 8, 25, H, 0);
    const draftEnd = new Date(2026, 8, 25, H + 1, 0);
    const expectedRequest = utcRequest(draftStart, draftEnd);

    function render(): void {
      fixture.detectChanges();
      tick();
      fixture.detectChanges();
    }

    function grid(): AvailabilityWeekGridComponent {
      return fixture.debugElement.query(By.directive(AvailabilityWeekGridComponent))
        .componentInstance as AvailabilityWeekGridComponent;
    }

    function saveButton(): HTMLButtonElement | null {
      return fixture.nativeElement.querySelector('.awg__draft-save') as HTMLButtonElement | null;
    }

    function alertText(): string | null {
      const alert = fixture.nativeElement.querySelector('.awg__draft [role="alert"]') as HTMLElement | null;
      return alert ? (alert.textContent ?? '').trim() : null;
    }

    /** "Şimdi" 23 Eylül 2026 Çar 12:00 (yerel); Cum 25 Eylül H:00 ve H:30 hücrelerine tıklanır. */
    function draftFridayOneHour(): void {
      jasmine.clock().mockDate(new Date(2026, 8, 23, 12, 0));
      render();
      grid()['onDateClick']({ date: draftStart });
      render();
      grid()['onDateClick']({ date: new Date(2026, 8, 25, H, 30) });
      render();
    }

    it('eski form kaldırıldı: sayfada form, tarih/saat girdisi ve submit butonu yok', fakeAsync(() => {
      render();

      expect(fixture.nativeElement.querySelector('form')).toBeNull();
      expect(fixture.nativeElement.querySelector('input')).toBeNull();
      expect(fixture.nativeElement.querySelector('mat-datepicker-toggle')).toBeNull();
      expect(fixture.nativeElement.querySelector('button[type="submit"]')).toBeNull();
    }));

    it('grid düzenlenebilir modda bağlanır ve yönerge metnini gösterir', fakeAsync(() => {
      render();

      expect(grid().editable()).toBeTrue();
      expect(fixture.nativeElement.querySelector('.awg__draft-text')?.textContent).toContain(
        taTr.grid.draft.instruction
      );
    }));

    it('Kaydet → createSlot taslak anlarının UTC gün + saat isteğiyle bir kez çağrılır', fakeAsync(() => {
      draftFridayOneHour();

      saveButton()!.click();
      render();

      expect(bookingService.createSlot).toHaveBeenCalledOnceWith(expectedRequest);
    }));

    it('başarıda snackbar gösterir, taslağı temizler, listeyi ve grid slotlarını yeniden yükler', fakeAsync(() => {
      draftFridayOneHour();
      bookingService.createSlot.and.returnValue(of({ success: true, slot: createdSlot }));
      bookingService.getAllMySlots.and.returnValue(of({ items: [mockSlot, createdSlot], success: true }));

      saveButton()!.click();
      render();

      expect(snackBar.open).toHaveBeenCalledOnceWith(taTr.messages.added, taTr.messages.ok, { duration: 3000 });
      expect(bookingService.getAllMySlots).toHaveBeenCalledTimes(2);
      expect(grid().slots()).toEqual([mockSlot, createdSlot]);
      expect(fixture.nativeElement.querySelectorAll('.avail__row').length).toBe(2);
      expect(grid().draft()).toBeNull();
      expect(saveButton()).toBeNull();
      expect(alertText()).toBeNull();
    }));

    it('409 çakışmada hata grid üzerinde gösterilir, taslak korunur, snackbar ve yeniden yükleme olmaz', fakeAsync(() => {
      draftFridayOneHour();
      const conflict = new HttpErrorResponse({
        status: 409,
        error: { success: false, conflict: true, message: 'Bu saat aralığı mevcut bir aralıkla çakışıyor.' },
      });
      bookingService.createSlot.and.returnValue(throwError(() => conflict));
      bookingService.extractError.and.returnValue('Bu saat aralığı mevcut bir aralıkla çakışıyor.');

      saveButton()!.click();
      render();

      expect(bookingService.extractError).toHaveBeenCalledOnceWith(conflict, taTr.messages.addFailed);
      expect(alertText()).toBe('Bu saat aralığı mevcut bir aralıkla çakışıyor.');
      expect(grid().draft()).toEqual({ start: draftStart, end: draftEnd });
      expect(saveButton()?.getAttribute('aria-disabled')).toBeNull();
      expect(snackBar.open).not.toHaveBeenCalled();
      expect(bookingService.getAllMySlots).toHaveBeenCalledTimes(1);
    }));

    it('200 + success:false gövdesinde sunucu mesajı grid üzerinde gösterilir ve taslak korunur', fakeAsync(() => {
      draftFridayOneHour();
      bookingService.createSlot.and.returnValue(of({ success: false, message: 'Bir aralık en fazla 4 saat olabilir.' }));

      saveButton()!.click();
      render();

      expect(alertText()).toBe('Bir aralık en fazla 4 saat olabilir.');
      expect(grid().draft()).not.toBeNull();
      expect(snackBar.open).not.toHaveBeenCalled();
    }));

    it('hata sonrası taslak değişince eski hata temizlenir', fakeAsync(() => {
      draftFridayOneHour();
      bookingService.createSlot.and.returnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
      saveButton()!.click();
      render();
      expect(alertText()).toBe('Error message');

      grid()['onDateClick']({ date: draftEnd });
      render();

      expect(alertText()).toBeNull();
      expect(grid().draft()?.end).toEqual(new Date(2026, 8, 25, H + 1, 30));
    }));

    it('başarı sonrası yeniden yükleme hata verirse yeni slot grid\'de ve listede görünmeye devam eder', fakeAsync(() => {
      bookingService.getAllMySlots.and.returnValue(of({ items: [], success: true }));
      draftFridayOneHour();
      bookingService.createSlot.and.returnValue(of({ success: true, slot: createdSlot }));
      bookingService.getAllMySlots.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 500, statusText: 'Server Error' }))
      );

      saveButton()!.click();
      render();

      // İlk slotta bile grid kaybolmaz: `res.slot` yerel listeye eklenmiştir.
      expect(fixture.debugElement.query(By.directive(AvailabilityWeekGridComponent))).not.toBeNull();
      expect(grid().slots()).toEqual([createdSlot]);
      expect(grid().draft()).toBeNull();
      expect(fixture.nativeElement.querySelector('.avail__state--error')).not.toBeNull();
    }));

    it('yeniden yükleme aynı slotu getirdiğinde kopya oluşmaz', fakeAsync(() => {
      draftFridayOneHour();
      bookingService.createSlot.and.returnValue(of({ success: true, slot: createdSlot }));
      const pendingLoad = new Subject<{ items: AvailabilitySlot[]; success: boolean }>();
      bookingService.getAllMySlots.and.returnValue(pendingLoad.asObservable());

      saveButton()!.click();
      render();

      // `load()` sürerken grid'in dolu listesi yeni slotu zaten içerir (üstüne taslak kurulamaz).
      expect(grid().slots()).toEqual([mockSlot, createdSlot]);

      pendingLoad.next({ items: [mockSlot, createdSlot], success: true });
      pendingLoad.complete();
      render();

      expect(grid().slots()).toEqual([mockSlot, createdSlot]);
    }));

    it('kayıt sürerken butonlar aria-disabled olur ve ikinci gönderim yapılmaz', fakeAsync(() => {
      draftFridayOneHour();
      const pending = new Subject<AvailabilitySlotResult>();
      bookingService.createSlot.and.returnValue(pending.asObservable());

      saveButton()!.click();
      render();

      expect(saveButton()?.getAttribute('aria-disabled')).toBe('true');
      expect(fixture.nativeElement.querySelector('.awg__draft-cancel')?.getAttribute('aria-disabled')).toBe('true');

      saveButton()!.click();
      component['create'](expectedRequest);
      render();
      expect(bookingService.createSlot).toHaveBeenCalledTimes(1);

      pending.next({ success: true, slot: createdSlot });
      pending.complete();
      render();

      expect(saveButton()).toBeNull();
      expect(grid().saving()).toBeFalse();
    }));
  });
});
