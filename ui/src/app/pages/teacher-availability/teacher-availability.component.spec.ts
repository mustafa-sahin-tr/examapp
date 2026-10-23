import type { MockedObject } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { By } from '@angular/platform-browser';
import { Subject, of, throwError } from 'rxjs';

import { TeacherAvailabilityComponent } from './teacher-availability.component';
import { BookingService } from '../../services/booking.service';
import { AvailabilitySlot, AvailabilitySlotResult, CreateRecurringRuleRequest, RecurringRuleDeleteResult, RecurringRuleResult, } from '../../models/booking.model';
import { TranslocoService } from '@jsverse/transloco';
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
    beforeEach(() => {
        vi.useFakeTimers();
    });
    afterEach(() => {
        vi.useRealTimers();
    });
    let component: TeacherAvailabilityComponent;
    let fixture: ComponentFixture<TeacherAvailabilityComponent>;
    let bookingService: MockedObject<BookingService>;
    let snackBar: MockedObject<MatSnackBar>;

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
        bookingService = {
            getAllMySlots: vi.fn().mockName("BookingService.getAllMySlots"),
            createSlot: vi.fn().mockName("BookingService.createSlot"),
            deleteSlot: vi.fn().mockName("BookingService.deleteSlot"),
            createRecurringRule: vi.fn().mockName("BookingService.createRecurringRule"),
            deleteRecurringRule: vi.fn().mockName("BookingService.deleteRecurringRule"),
            extractError: vi.fn().mockName("BookingService.extractError")
        } as unknown as MockedObject<BookingService>;
        bookingService.getAllMySlots.mockReturnValue(of({ items: [mockSlot], success: true }));
        bookingService.createSlot.mockReturnValue(of({ success: true, slot: mockSlot }));
        bookingService.deleteSlot.mockReturnValue(of(undefined));
        bookingService.createRecurringRule.mockReturnValue(of({ success: true, generatedSlotIds: [], skippedDates: [] }));
        bookingService.deleteRecurringRule.mockReturnValue(of({ success: true, deletedSlotIds: [], preservedSlotIds: [], preservedBookedCount: 0 }));
        bookingService.extractError.mockReturnValue('Error message');

        snackBar = {
            open: vi.fn().mockName("MatSnackBar.open")
        } as unknown as MockedObject<MatSnackBar>;

        await TestBed.configureTestingModule({
            imports: [TeacherAvailabilityComponent, HttpClientTestingModule, translocoTesting, BrowserAnimationsModule],
            providers: [
                { provide: BookingService, useValue: bookingService },
                { provide: MatSnackBar, useValue: snackBar },
                provideRouter([]),
                // Grid'deki bitiş günü seçicisi (issue #179) DateAdapter ister.
                provideNativeDateAdapter(),
            ],
        }).compileComponents();

        fixture = TestBed.createComponent(TeacherAvailabilityComponent);
        component = fixture.componentInstance;
    });

    it('should create', () => {
        expect(component).toBeTruthy();
    });

    it('liste: gün aşan slot aralığına "(+1 gün)" eklenir, aynı gündeki slota eklenmez (issue #300)', async () => {
        const crossingStart = new Date(2026, 8, 25, 23, 30);
        const crossingEnd = new Date(2026, 8, 26, 0, 30);
        const crossing: AvailabilitySlot = {
            ...mockSlot,
            id: 77,
            startUtc: crossingStart.toISOString(),
            endUtc: crossingEnd.toISOString(),
        };
        bookingService.getAllMySlots.mockReturnValue(of({ items: [mockSlot, crossing], success: true }));

        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);
        fixture.detectChanges();

        const marks = fixture.nativeElement.querySelectorAll('.avail__row-next-day') as NodeListOf<HTMLElement>;
        expect(marks.length).toBe(1);
        expect(marks[0].textContent?.trim()).toBe('(+1 gün)');
    });

    it('should call getAllMySlots on init', async () => {
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        expect(bookingService.getAllMySlots).toHaveBeenCalled();
    });

    it('should render page title', async () => {
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        const title = fixture.nativeElement.querySelector('h1');
        expect(title).toBeTruthy();
    });

    it('should render grid component when no error', async () => {
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        const grid = fixture.nativeElement.querySelector('app-availability-week-grid');
        expect(grid).toBeTruthy();
    });

    it('should pass slots from service to grid component', async () => {
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
        bookingService.getAllMySlots.mockReturnValue(of({ items: slots, success: true }));

        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        // Grid'in `slots` input'u servisten dönen diziyle aynı olmalı (yalnızca sayfanın kendi sinyali değil).
        const grid = fixture.debugElement.query(By.directive(AvailabilityWeekGridComponent));
        expect(grid).toBeTruthy();
        expect((grid.componentInstance as AvailabilityWeekGridComponent).slots()).toEqual(slots);
    });

    it('should render grid when error occurs but previous data exists', async () => {
        bookingService.getAllMySlots.mockReturnValue(of({ items: [mockSlot], success: true }));

        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        expect(fixture.nativeElement.querySelector('app-availability-week-grid')).toBeTruthy();

        // Now simulate error on reload
        const errorResponse = new HttpErrorResponse({ status: 500, statusText: 'Server Error' });
        bookingService.getAllMySlots.mockReturnValue(throwError(() => errorResponse));

        // Trigger load again
        component['load']();
        await vi.advanceTimersByTimeAsync(0);
        fixture.detectChanges();

        // Hata gerçekten render edildi VE grid önceki veriyle yerinde kaldı.
        expect(fixture.nativeElement.querySelector('.avail__state--error')).toBeTruthy();
        const gridDe = fixture.debugElement.query(By.directive(AvailabilityWeekGridComponent));
        expect(gridDe).toBeTruthy();
        expect((gridDe.componentInstance as AvailabilityWeekGridComponent).slots()).toEqual([mockSlot]);
    });

    it('should not render grid when initial load error and no previous data', async () => {
        const errorResponse = new HttpErrorResponse({ status: 500, statusText: 'Server Error' });
        bookingService.getAllMySlots.mockReturnValue(throwError(() => errorResponse));
        bookingService.extractError.mockReturnValue('Failed to load slots');

        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        const grid = fixture.nativeElement.querySelector('app-availability-week-grid');
        expect(grid).toBeFalsy();
    });

    it('should show error message when load fails', async () => {
        const errorResponse = new HttpErrorResponse({ status: 500, statusText: 'Server Error' });
        bookingService.getAllMySlots.mockReturnValue(throwError(() => errorResponse));
        bookingService.extractError.mockReturnValue('Failed to load slots');

        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        const errorBlock = fixture.nativeElement.querySelector('.avail__state--error');
        expect(errorBlock).toBeTruthy();
    });

    it('should show retry button in error state', async () => {
        const errorResponse = new HttpErrorResponse({ status: 500, statusText: 'Server Error' });
        bookingService.getAllMySlots.mockReturnValue(throwError(() => errorResponse));
        bookingService.extractError.mockReturnValue('Failed to load slots');

        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        const errorBlock = fixture.nativeElement.querySelector('.avail__state--error');
        const retryButton = errorBlock?.querySelector('button');
        expect(retryButton).toBeTruthy();
    });

    it('should show empty state when no slots', async () => {
        bookingService.getAllMySlots.mockReturnValue(of({ items: [], success: true }));

        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        const emptyIcon = fixture.nativeElement.querySelector('.avail__empty-icon');
        expect(emptyIcon).toBeTruthy();
    });

    it('should render list items for each slot', async () => {
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
        bookingService.getAllMySlots.mockReturnValue(of({ items: slots, success: true }));

        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        const listItems = fixture.nativeElement.querySelectorAll('.avail__row');
        expect(listItems.length).toBe(2);
    });

    it('liste salt okunur: satırlarda silme butonu yok (issue #177 silmeyi grid\'e taşıdı)', async () => {
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        expect(fixture.nativeElement.querySelectorAll('.avail__row').length).toBe(1);
        expect(fixture.nativeElement.querySelector('.avail__row button')).toBeNull();
        expect(fixture.nativeElement.querySelector('.avail__locked')).toBeNull();
    });

    it('should show locked icon for booked slots', async () => {
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
        bookingService.getAllMySlots.mockReturnValue(of({ items: [bookedSlot], success: true }));

        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        const lockedIcon = fixture.nativeElement.querySelector('.avail__locked');
        expect(lockedIcon).toBeTruthy();
    });

    it('should display booking requests button', async () => {
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        const button = fixture.nativeElement.querySelector('a[routerLink="/booking-requests"]');
        expect(button).toBeTruthy();
    });

    it('should display slot information in list', async () => {
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        const daySpan = fixture.nativeElement.querySelector('.avail__row-day');
        expect(daySpan?.textContent).toBeTruthy();
    });

    it('should display time range in list', async () => {
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        const timeRange = fixture.nativeElement.querySelector('.avail__row-range');
        expect(timeRange?.textContent).toContain('–');
    });

    it('should display status chip', async () => {
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        const chip = fixture.nativeElement.querySelector('.avail__chip');
        expect(chip).toBeTruthy();
    });

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

        async function render(): Promise< void > {
            fixture.detectChanges();
            await vi.advanceTimersByTimeAsync(0);
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
        async function selectFreeSlot(): Promise< void > {
            bookingService.getAllMySlots.mockReturnValue(of({ items: [freeSlot, bookedSlot], success: true }));
            await render();
            grid()['onEventClick']('11');
            await render();
        }

        it('slota tıklama grid\'i silme moduna alır; Sil → deleteSlot(id) bir kez çağrılır', async () => {
            await selectFreeSlot();
            expect(confirmButton()).not.toBeNull();
            expect(grid().selectedSlotId()).toBe(11);

            confirmButton()!.click();
            await render();

            expect(bookingService.deleteSlot).toHaveBeenCalledTimes(1);

            expect(bookingService.deleteSlot).toHaveBeenCalledWith(11);
        });

        it('başarıda snackbar, slot listeden ve grid\'den düşer, seçim kapanır, yeniden yükleme yapılmaz', async () => {
            await selectFreeSlot();
            expect(fixture.nativeElement.querySelectorAll('.avail__row').length).toBe(2);

            confirmButton()!.click();
            await render();

            expect(snackBar.open).toHaveBeenCalledTimes(1);

            expect(snackBar.open).toHaveBeenCalledWith(taTr.messages.deleted, taTr.messages.ok, { duration: 3000 });
            expect(component['slots']().map((s) => s.id)).toEqual([12]);
            expect(grid().slots().map((s) => s.id)).toEqual([12]);
            expect(fixture.nativeElement.querySelectorAll('.avail__row').length).toBe(1);
            expect(fixture.nativeElement.querySelectorAll('.fc-timegrid-event').length).toBe(1);
            expect(grid().selectedSlotId()).toBeNull();
            expect(confirmButton()).toBeNull();
            expect(alertText()).toBeNull();
            expect(bookingService.getAllMySlots).toHaveBeenCalledTimes(1);
        });

        it('sunucu reddi (randevusu var) grid çubuğunda gösterilir; seçim ve slot korunur, snackbar yok', async () => {
            await selectFreeSlot();
            const rejected = new HttpErrorResponse({
                status: 400,
                error: { success: false, message: 'Aktif randevusu olan aralık silinemez.' },
            });
            bookingService.deleteSlot.mockReturnValue(throwError(() => rejected));
            bookingService.extractError.mockReturnValue('Aktif randevusu olan aralık silinemez.');

            confirmButton()!.click();
            await render();

            expect(bookingService.extractError).toHaveBeenCalledTimes(1);

            expect(bookingService.extractError).toHaveBeenCalledWith(rejected, taTr.messages.deleteFailed);
            expect(alertText()).toBe('Aktif randevusu olan aralık silinemez.');
            expect(grid().selectedSlotId()).toBe(11);
            expect(component['slots']().length).toBe(2);
            expect(confirmButton()?.getAttribute('aria-disabled')).toBeNull();
            expect(snackBar.open).not.toHaveBeenCalled();
        });

        it('409 çakışmada da hata grid\'e düşer ve seçim değişince temizlenir', async () => {
            await selectFreeSlot();
            bookingService.deleteSlot.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
            confirmButton()!.click();
            await render();
            expect(alertText()).toBe('Error message');

            // Vazgeç → seçim null → sayfa hatayı temizler.
            (fixture.nativeElement.querySelector('.awg__delete-cancel') as HTMLButtonElement).click();
            await render();

            expect(alertText()).toBeNull();
            expect(grid().selectedSlotId()).toBeNull();
            expect(component['deleteError']()).toBeNull();
        });

        it('seçili slot yeniden yüklemede listeden düşerse grid seçimi null yayar ve sayfa deleteError\'ı temizler', async () => {
            await selectFreeSlot();
            bookingService.deleteSlot.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
            confirmButton()!.click();
            await render();
            expect(alertText()).toBe('Error message');

            bookingService.getAllMySlots.mockReturnValue(of({ items: [bookedSlot], success: true }));
            component['load']();
            await render();

            expect(component['selectedSlotId']()).toBeNull();
            expect(component['deleteError']()).toBeNull();
            expect(alertText()).toBeNull();
            expect(confirmButton()).toBeNull();
        });

        it('randevulu slota tıklama silme moduna girmez ve deleteSlot çağrılmaz', async () => {
            bookingService.getAllMySlots.mockReturnValue(of({ items: [freeSlot, bookedSlot], success: true }));
            await render();

            grid()['onEventClick']('12');
            await render();

            expect(confirmButton()).toBeNull();
            expect(fixture.nativeElement.querySelector('.awg__locked-hint')?.textContent?.trim()).toBe(taTr.actions.lockedHint);
            expect(bookingService.deleteSlot).not.toHaveBeenCalled();
        });

        it('silme sürerken butonlar aria-disabled olur ve ikinci gönderim yapılmaz', async () => {
            await selectFreeSlot();
            const pending = new Subject<void>();
            bookingService.deleteSlot.mockReturnValue(pending.asObservable());

            confirmButton()!.click();
            await render();

            expect(confirmButton()?.getAttribute('aria-disabled')).toBe('true');
            expect(fixture.nativeElement.querySelector('.awg__delete-cancel')?.getAttribute('aria-disabled')).toBe('true');
            expect(grid().deleting()).toBe(true);

            confirmButton()!.click();
            component['deleteSlot'](11);
            await render();
            expect(bookingService.deleteSlot).toHaveBeenCalledTimes(1);

            pending.next();
            pending.complete();
            await render();

            expect(confirmButton()).toBeNull();
            expect(grid().deleting()).toBe(false);
            expect(component['slots']().map((s) => s.id)).toEqual([12]);
        });
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

        async function render(): Promise< void > {
            fixture.detectChanges();
            await vi.advanceTimersByTimeAsync(0);
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
        async function draftFridayOneHour(): Promise< void > {
            vi.setSystemTime(new Date(2026, 8, 23, 12, 0));
            await render();
            grid()['onDateClick']({ date: draftStart });
            await render();
            grid()['onDateClick']({ date: new Date(2026, 8, 25, H, 30) });
            await render();
        }

        it('eski form kaldırıldı: sayfada form, tarih/saat girdisi ve submit butonu yok', async () => {
            await render();

            expect(fixture.nativeElement.querySelector('form')).toBeNull();
            expect(fixture.nativeElement.querySelector('input')).toBeNull();
            expect(fixture.nativeElement.querySelector('mat-datepicker-toggle')).toBeNull();
            expect(fixture.nativeElement.querySelector('button[type="submit"]')).toBeNull();
        });

        it('grid düzenlenebilir modda bağlanır ve yönerge metnini gösterir', async () => {
            await render();

            expect(grid().editable()).toBe(true);
            expect(fixture.nativeElement.querySelector('.awg__draft-text')?.textContent).toContain(taTr.grid.draft.instruction);
        });

        it('Kaydet → createSlot taslak anlarının UTC gün + saat isteğiyle bir kez çağrılır', async () => {
            await draftFridayOneHour();

            saveButton()!.click();
            await render();

            expect(bookingService.createSlot).toHaveBeenCalledTimes(1);

            expect(bookingService.createSlot).toHaveBeenCalledWith(expectedRequest);
        });

        it('başarıda snackbar gösterir, taslağı temizler, listeyi ve grid slotlarını yeniden yükler', async () => {
            await draftFridayOneHour();
            bookingService.createSlot.mockReturnValue(of({ success: true, slot: createdSlot }));
            bookingService.getAllMySlots.mockReturnValue(of({ items: [mockSlot, createdSlot], success: true }));

            saveButton()!.click();
            await render();

            expect(snackBar.open).toHaveBeenCalledTimes(1);

            expect(snackBar.open).toHaveBeenCalledWith(taTr.messages.added, taTr.messages.ok, { duration: 3000 });
            expect(bookingService.getAllMySlots).toHaveBeenCalledTimes(2);
            expect(grid().slots()).toEqual([mockSlot, createdSlot]);
            expect(fixture.nativeElement.querySelectorAll('.avail__row').length).toBe(2);
            expect(grid().draft()).toBeNull();
            expect(saveButton()).toBeNull();
            expect(alertText()).toBeNull();
        });

        it('409 çakışmada hata grid üzerinde gösterilir, taslak korunur, snackbar ve yeniden yükleme olmaz', async () => {
            await draftFridayOneHour();
            const conflict = new HttpErrorResponse({
                status: 409,
                error: { success: false, conflict: true, message: 'Bu saat aralığı mevcut bir aralıkla çakışıyor.' },
            });
            bookingService.createSlot.mockReturnValue(throwError(() => conflict));
            bookingService.extractError.mockReturnValue('Bu saat aralığı mevcut bir aralıkla çakışıyor.');

            saveButton()!.click();
            await render();

            expect(bookingService.extractError).toHaveBeenCalledTimes(1);

            expect(bookingService.extractError).toHaveBeenCalledWith(conflict, taTr.messages.addFailed);
            expect(alertText()).toBe('Bu saat aralığı mevcut bir aralıkla çakışıyor.');
            expect(grid().draft()).toEqual({ start: draftStart, end: draftEnd });
            expect(saveButton()?.getAttribute('aria-disabled')).toBeNull();
            expect(snackBar.open).not.toHaveBeenCalled();
            expect(bookingService.getAllMySlots).toHaveBeenCalledTimes(1);
        });

        it('200 + success:false gövdesinde sunucu mesajı grid üzerinde gösterilir ve taslak korunur', async () => {
            await draftFridayOneHour();
            bookingService.createSlot.mockReturnValue(of({ success: false, message: 'Bir aralık en fazla 4 saat olabilir.' }));

            saveButton()!.click();
            await render();

            expect(alertText()).toBe('Bir aralık en fazla 4 saat olabilir.');
            expect(grid().draft()).not.toBeNull();
            expect(snackBar.open).not.toHaveBeenCalled();
        });

        it('hata sonrası taslak değişince eski hata temizlenir', async () => {
            await draftFridayOneHour();
            bookingService.createSlot.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
            saveButton()!.click();
            await render();
            expect(alertText()).toBe('Error message');

            grid()['onDateClick']({ date: draftEnd });
            await render();

            expect(alertText()).toBeNull();
            expect(grid().draft()?.end).toEqual(new Date(2026, 8, 25, H + 1, 30));
        });

        it('başarı sonrası yeniden yükleme hata verirse yeni slot grid\'de ve listede görünmeye devam eder', async () => {
            bookingService.getAllMySlots.mockReturnValue(of({ items: [], success: true }));
            await draftFridayOneHour();
            bookingService.createSlot.mockReturnValue(of({ success: true, slot: createdSlot }));
            bookingService.getAllMySlots.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500, statusText: 'Server Error' })));

            saveButton()!.click();
            await render();

            // İlk slotta bile grid kaybolmaz: `res.slot` yerel listeye eklenmiştir.
            expect(fixture.debugElement.query(By.directive(AvailabilityWeekGridComponent))).not.toBeNull();
            expect(grid().slots()).toEqual([createdSlot]);
            expect(grid().draft()).toBeNull();
            expect(fixture.nativeElement.querySelector('.avail__state--error')).not.toBeNull();
        });

        it('yeniden yükleme aynı slotu getirdiğinde kopya oluşmaz', async () => {
            await draftFridayOneHour();
            bookingService.createSlot.mockReturnValue(of({ success: true, slot: createdSlot }));
            const pendingLoad = new Subject<{
                items: AvailabilitySlot[];
                success: boolean;
            }>();
            bookingService.getAllMySlots.mockReturnValue(pendingLoad.asObservable());

            saveButton()!.click();
            await render();

            // `load()` sürerken grid'in dolu listesi yeni slotu zaten içerir (üstüne taslak kurulamaz).
            expect(grid().slots()).toEqual([mockSlot, createdSlot]);

            pendingLoad.next({ items: [mockSlot, createdSlot], success: true });
            pendingLoad.complete();
            await render();

            expect(grid().slots()).toEqual([mockSlot, createdSlot]);
        });

        it('kayıt sürerken butonlar aria-disabled olur ve ikinci gönderim yapılmaz', async () => {
            await draftFridayOneHour();
            const pending = new Subject<AvailabilitySlotResult>();
            bookingService.createSlot.mockReturnValue(pending.asObservable());

            saveButton()!.click();
            await render();

            expect(saveButton()?.getAttribute('aria-disabled')).toBe('true');
            expect(fixture.nativeElement.querySelector('.awg__draft-cancel')?.getAttribute('aria-disabled')).toBe('true');

            saveButton()!.click();
            component['create'](expectedRequest);
            await render();
            expect(bookingService.createSlot).toHaveBeenCalledTimes(1);

            pending.next({ success: true, slot: createdSlot });
            pending.complete();
            await render();

            expect(saveButton()).toBeNull();
            expect(grid().saving()).toBe(false);
        });
    });

    describe('tekrarlayan haftalık aralık (issue #179)', () => {
        // Cum 25 Eylül 2026, yerel H:00–(H+1):00; "şimdi" Çar 23 Eylül 12:00.
        const H = safeLocalHour(new Date(2026, 8, 25, 12, 0));
        const draftStart = new Date(2026, 8, 25, H, 0);
        const draftEnd = new Date(2026, 8, 25, H + 1, 0);
        const utc = utcRequest(draftStart, draftEnd);
        const expectedRule: CreateRecurringRuleRequest = {
            dayOfWeek: draftStart.getUTCDay() as CreateRecurringRuleRequest['dayOfWeek'],
            startTime: utc.startTime,
            endTime: utc.endTime,
            effectiveFrom: utc.date,
            effectiveUntil: null,
        };
        const okText = (key: string, params?: Record<string, unknown>) => TestBed.inject(TranslocoService).translate<string>(`teacher-availability.${key}`, params);

        /** Görünen haftada (dilimden bağımsız saatte) kural 5'ten üretilmiş boş slot ve tekil boş slot. */
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
        const WH = safeLocalHour(friday(12));
        const slotAt = (id: number, startHour: number, ruleId: number | null): AvailabilitySlot => ({
            id,
            teacherId: 10,
            date: friday(startHour).toISOString().slice(0, 10),
            startTime: friday(startHour).toISOString().slice(11, 19),
            endTime: friday(startHour + 1).toISOString().slice(11, 19),
            createdAt: '2026-09-15T10:00:00Z',
            startUtc: friday(startHour).toISOString(),
            endUtc: friday(startHour + 1).toISOString(),
            isBooked: false,
            recurringAvailabilityRuleId: ruleId,
        });
        const singleSlot = slotAt(11, WH, null);
        const recurringSlot = slotAt(21, WH + 2, 5);

        async function render(): Promise<void> {
            fixture.detectChanges();
            await vi.advanceTimersByTimeAsync(0);
            fixture.detectChanges();
        }

        function grid(): AvailabilityWeekGridComponent {
            return fixture.debugElement.query(By.directive(AvailabilityWeekGridComponent))
                .componentInstance as AvailabilityWeekGridComponent;
        }

        function q<T extends HTMLElement = HTMLElement>(selector: string): T | null {
            return fixture.nativeElement.querySelector(selector) as T | null;
        }

        function alertText(): string | null {
            const alert = q('.awg__draft [role="alert"]');
            return alert ? (alert.textContent ?? '').trim() : null;
        }

        /** Taslak kurulur ve "Her hafta tekrarla" işaretlenir. */
        async function draftRecurring(): Promise< void > {
            vi.setSystemTime(new Date(2026, 8, 23, 12, 0));
            await render();
            grid()['onDateClick']({ date: draftStart });
            await render();
            grid()['onDateClick']({ date: new Date(2026, 8, 25, H, 30) });
            await render();
            q<HTMLInputElement>('.awg__repeat-toggle input[type="checkbox"]')!.click();
            await render();
        }

        /** Sayfa yüklenir, grid'de tekrarlayan slota (21) tıklanır → seri silme modu. */
        async function selectRecurringSlot(): Promise<void> {
            bookingService.getAllMySlots.mockReturnValue(of({ items: [singleSlot, recurringSlot], success: true }));
            await render();
            grid()['onEventClick']('21');
            await render();
        }

        it('Kaydet → createRecurringRule taslağın UTC gün/saat kural isteğiyle bir kez çağrılır; createSlot çağrılmaz', async () => {
            await draftRecurring();

            q<HTMLButtonElement>('.awg__draft-save')!.click();
            await render();

            expect(bookingService.createRecurringRule).toHaveBeenCalledTimes(1);

            expect(bookingService.createRecurringRule).toHaveBeenCalledWith(expectedRule);
            expect(bookingService.createSlot).not.toHaveBeenCalled();
        });

        it('süresiz kuralda başarı: snackbar "İlk N hafta ... süresiz devam eder", taslak temizlenir ve liste yeniden yüklenir', async () => {
            await draftRecurring();
            const generated = [slotAt(31, WH + 4, 9), slotAt(32, WH + 6, 9)];
            bookingService.createRecurringRule.mockReturnValue(of({ success: true, generatedSlotIds: [31, 32, 33, 34, 35, 36], skippedDates: [] }));
            bookingService.getAllMySlots.mockReturnValue(of({ items: [mockSlot, ...generated], success: true }));

            q<HTMLButtonElement>('.awg__draft-save')!.click();
            await render();

            // effectiveUntil null → 90 günlük ufuk üretildi; "N hafta için" DEĞİL, süresiz metni.
            expect(snackBar.open).toHaveBeenCalledTimes(1);

            // effectiveUntil null → 90 günlük ufuk üretildi; "N hafta için" DEĞİL, süresiz metni.
            expect(snackBar.open).toHaveBeenCalledWith(okText('messages.recurringAddedIndefinite', { count: 6 }), taTr.messages.ok, { duration: 5000 });
            expect(vi.mocked(snackBar.open).mock.lastCall![0]).toContain('6 ');
            expect(vi.mocked(snackBar.open).mock.lastCall![0]).not.toContain('{{');
            expect(vi.mocked(snackBar.open).mock.lastCall![0]).not.toBe(okText('messages.recurringAdded', { count: 6 }));
            expect(bookingService.getAllMySlots).toHaveBeenCalledTimes(2);
            expect(grid().slots()).toEqual([mockSlot, ...generated]);
            expect(grid().draft()).toBeNull();
            expect(q('.awg__draft-save')).toBeNull();
            expect(alertText()).toBeNull();
        });

        it('atlanan hafta varsa snackbar metnine "K hafta dolu olduğu için atlandı" eklenir', async () => {
            await draftRecurring();
            bookingService.createRecurringRule.mockReturnValue(of({ success: true, generatedSlotIds: [31, 32, 33], skippedDates: ['2026-10-09', '2026-10-23'] }));

            q<HTMLButtonElement>('.awg__draft-save')!.click();
            await render();

            const message = vi.mocked(snackBar.open).mock.lastCall![0];
            expect(message).toBe(`${okText('messages.recurringAddedIndefinite', { count: 3 })} ${okText('messages.recurringSkipped', { count: 2 })}`);
            expect(message).toContain(taTr.messages.recurringSkipped.replace('{{count}}', '2'));
        });

        it('bitiş günü verilmişse effectiveUntil gönderilir ve başarı metni "N hafta için" olur', async () => {
            await draftRecurring();
            const until = new Date(2026, 8, 25 + 28); // 4 hafta sonraki Cuma (yerel)
            grid()['untilControl'].setValue(until);
            await render();
            bookingService.createRecurringRule.mockReturnValue(of({ success: true, generatedSlotIds: [31, 32, 33, 34, 35], skippedDates: [] }));

            q<HTMLButtonElement>('.awg__draft-save')!.click();
            await render();

            const untilAtDraftTime = new Date(until.getFullYear(), until.getMonth(), until.getDate(), H, 0);
            expect(bookingService.createRecurringRule).toHaveBeenCalledTimes(1);
            expect(bookingService.createRecurringRule).toHaveBeenCalledWith({
                ...expectedRule,
                effectiveUntil: untilAtDraftTime.toISOString().slice(0, 10),
            });
            expect(vi.mocked(snackBar.open).mock.lastCall![0]).toBe(okText('messages.recurringAdded', { count: 5 }));
        });

        it('tarih seçili + 409: hata çubukta, taslak, kutu ve seçili tarih korunur', async () => {
            await draftRecurring();
            const until = new Date(2026, 8, 25 + 14);
            grid()['untilControl'].setValue(until);
            await render();
            bookingService.createRecurringRule.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409, error: { success: false, conflict: true } })));
            bookingService.extractError.mockReturnValue('Kesişen kural.');

            q<HTMLButtonElement>('.awg__draft-save')!.click();
            await render();

            expect(alertText()).toBe('Kesişen kural.');
            expect(grid().draft()).not.toBeNull();
            expect(q<HTMLInputElement>('.awg__repeat-toggle input[type="checkbox"]')?.checked).toBe(true);
            expect(grid()['untilControl'].value).toEqual(until);
            expect(grid()['untilControl'].enabled).toBe(true); // saving bitti, alan yeniden açık
            expect(q<HTMLInputElement>('.awg__repeat-until input')?.value).not.toBe('');
        });

        it('409 (kesişen kural) hata grid çubuğunda gösterilir; taslak ve kutu korunur, snackbar/yeniden yükleme yok', async () => {
            await draftRecurring();
            const conflict = new HttpErrorResponse({
                status: 409,
                error: { success: false, conflict: true, message: 'Aynı gün kesişen bir kural var.' },
            });
            bookingService.createRecurringRule.mockReturnValue(throwError(() => conflict));
            bookingService.extractError.mockReturnValue('Aynı gün kesişen bir kural var.');

            q<HTMLButtonElement>('.awg__draft-save')!.click();
            await render();

            expect(bookingService.extractError).toHaveBeenCalledTimes(1);

            expect(bookingService.extractError).toHaveBeenCalledWith(conflict, taTr.messages.recurringAddFailed);
            expect(alertText()).toBe('Aynı gün kesişen bir kural var.');
            expect(grid().draft()).not.toBeNull();
            expect(q<HTMLInputElement>('.awg__repeat-toggle input[type="checkbox"]')?.checked).toBe(true);
            expect(snackBar.open).not.toHaveBeenCalled();
            expect(bookingService.getAllMySlots).toHaveBeenCalledTimes(1);
        });

        it('200 + success:false gövdesinde sunucu mesajı çubukta gösterilir, taslak korunur', async () => {
            await draftRecurring();
            bookingService.createRecurringRule.mockReturnValue(of({ success: false, message: 'En fazla 50 aktif kural.', generatedSlotIds: [], skippedDates: [] }));

            q<HTMLButtonElement>('.awg__draft-save')!.click();
            await render();

            expect(alertText()).toBe('En fazla 50 aktif kural.');
            expect(grid().draft()).not.toBeNull();
            expect(snackBar.open).not.toHaveBeenCalled();
        });

        it('kayıt sürerken ikinci gönderim yapılmaz; tamamlanınca taslak kapanır', async () => {
            await draftRecurring();
            const pending = new Subject<RecurringRuleResult>();
            bookingService.createRecurringRule.mockReturnValue(pending.asObservable());

            q<HTMLButtonElement>('.awg__draft-save')!.click();
            await render();
            expect(grid().saving()).toBe(true);
            q<HTMLButtonElement>('.awg__draft-save')!.click();
            component['createRecurring'](expectedRule);
            await render();
            expect(bookingService.createRecurringRule).toHaveBeenCalledTimes(1);

            pending.next({ success: true, generatedSlotIds: [1], skippedDates: [] });
            pending.complete();
            await render();

            expect(grid().saving()).toBe(false);
            expect(q('.awg__draft-save')).toBeNull();
        });

        it('tekrarlayan slotta "Tüm seri" → deleteRecurringRule(ruleId) bir kez; deleteSlot çağrılmaz', async () => {
            await selectRecurringSlot();
            expect(q('.awg__delete-series')).not.toBeNull();

            q<HTMLButtonElement>('.awg__delete-series')!.click();
            await render();

            expect(bookingService.deleteRecurringRule).toHaveBeenCalledTimes(1);

            expect(bookingService.deleteRecurringRule).toHaveBeenCalledWith(5);
            expect(bookingService.deleteSlot).not.toHaveBeenCalled();
        });

        it('tekrarlayan slotta "Sadece bu hafta" → deleteSlot(slotId); deleteRecurringRule çağrılmaz', async () => {
            await selectRecurringSlot();

            q<HTMLButtonElement>('.awg__delete-confirm')!.click();
            await render();

            expect(bookingService.deleteSlot).toHaveBeenCalledTimes(1);

            expect(bookingService.deleteSlot).toHaveBeenCalledWith(21);
            expect(bookingService.deleteRecurringRule).not.toHaveBeenCalled();
        });

        it('seri silme başarısında snackbar "N aralık silindi", seçim kapanır, liste yeniden yüklenir', async () => {
            await selectRecurringSlot();
            bookingService.deleteRecurringRule.mockReturnValue(of({ success: true, deletedSlotIds: [21, 41, 42], preservedSlotIds: [], preservedBookedCount: 0 }));
            bookingService.getAllMySlots.mockReturnValue(of({ items: [singleSlot], success: true }));

            q<HTMLButtonElement>('.awg__delete-series')!.click();
            await render();

            expect(snackBar.open).toHaveBeenCalledTimes(1);

            expect(snackBar.open).toHaveBeenCalledWith(okText('messages.seriesDeleted', { count: 3 }), taTr.messages.ok, {
                duration: 5000,
            });
            expect(bookingService.getAllMySlots).toHaveBeenCalledTimes(2);
            expect(grid().slots().map((s) => s.id)).toEqual([11]);
            expect(grid().selectedSlotId()).toBeNull();
            expect(q('.awg__delete-series')).toBeNull();
            expect(alertText()).toBeNull();
        });

        it('randevulu aralık korunduysa snackbar "K randevulu aralık korundu" ekler', async () => {
            await selectRecurringSlot();
            bookingService.deleteRecurringRule.mockReturnValue(of({ success: true, deletedSlotIds: [21, 41], preservedSlotIds: [42], preservedBookedCount: 1 }));

            q<HTMLButtonElement>('.awg__delete-series')!.click();
            await render();

            expect(vi.mocked(snackBar.open).mock.lastCall![0]).toBe(`${okText('messages.seriesDeleted', { count: 2 })} ${okText('messages.seriesPreserved', { count: 1 })}`);
        });

        it('seri silme hatası grid çubuğunda gösterilir; seçim ve slotlar korunur, snackbar yok', async () => {
            await selectRecurringSlot();
            const err = new HttpErrorResponse({ status: 404, error: { success: false, notFound: true, message: 'Kural bulunamadı.' } });
            bookingService.deleteRecurringRule.mockReturnValue(throwError(() => err));
            bookingService.extractError.mockReturnValue('Kural bulunamadı.');

            q<HTMLButtonElement>('.awg__delete-series')!.click();
            await render();

            expect(bookingService.extractError).toHaveBeenCalledTimes(1);

            expect(bookingService.extractError).toHaveBeenCalledWith(err, taTr.messages.seriesDeleteFailed);
            expect(alertText()).toBe('Kural bulunamadı.');
            expect(grid().selectedSlotId()).toBe(21);
            expect(component['slots']().length).toBe(2);
            expect(q('.awg__delete-series')).not.toBeNull();
            expect(snackBar.open).not.toHaveBeenCalled();
            expect(bookingService.getAllMySlots).toHaveBeenCalledTimes(1);
        });

        it('seri silme sürerken butonlar aria-disabled ve ikinci gönderim yapılmaz', async () => {
            await selectRecurringSlot();
            const pending = new Subject<RecurringRuleDeleteResult>();
            bookingService.deleteRecurringRule.mockReturnValue(pending.asObservable());

            q<HTMLButtonElement>('.awg__delete-series')!.click();
            await render();

            expect(q('.awg__delete-series')?.getAttribute('aria-disabled')).toBe('true');
            expect(q('.awg__delete-confirm')?.getAttribute('aria-disabled')).toBe('true');
            q<HTMLButtonElement>('.awg__delete-series')!.click();
            q<HTMLButtonElement>('.awg__delete-confirm')!.click();
            component['deleteSeries'](5);
            await render();
            expect(bookingService.deleteRecurringRule).toHaveBeenCalledTimes(1);
            expect(bookingService.deleteSlot).not.toHaveBeenCalled();

            pending.next({ success: true, deletedSlotIds: [21], preservedSlotIds: [], preservedBookedCount: 0 });
            pending.complete();
            await render();

            expect(grid().deleting()).toBe(false);
            expect(q('.awg__delete-series')).toBeNull();
        });

        it('tekil slotta "Tüm seri" butonu yoktur', async () => {
            bookingService.getAllMySlots.mockReturnValue(of({ items: [singleSlot, recurringSlot], success: true }));
            await render();

            grid()['onEventClick']('11');
            await render();

            expect(q('.awg__delete-confirm')).not.toBeNull();
            expect(q('.awg__delete-series')).toBeNull();
        });

        it('listede tekrarlayan slot satırı tekrar ikonu taşır, tekil satır taşımaz', async () => {
            bookingService.getAllMySlots.mockReturnValue(of({ items: [singleSlot, recurringSlot], success: true }));
            await render();

            const rows = Array.from(fixture.nativeElement.querySelectorAll('.avail__row') as NodeListOf<HTMLElement>);
            expect(rows.length).toBe(2);
            expect(rows.filter((r) => r.querySelector('.avail__row-repeat')).length).toBe(1);
        });
    });
});
