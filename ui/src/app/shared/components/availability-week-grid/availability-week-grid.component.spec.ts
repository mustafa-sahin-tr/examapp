import { ComponentFixture, TestBed, flush } from '@angular/core/testing';
import { PLATFORM_ID } from '@angular/core';
import { provideNativeDateAdapter } from '@angular/material/core';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { AvailabilityWeekGridComponent } from './availability-week-grid.component';
import { AvailabilitySlot, ActiveBookingStatus, CreateAvailabilitySlotRequest, CreateRecurringRuleRequest, } from '../../../models/booking.model';
import type { EventClickArg, EventContentArg } from '@fullcalendar/core';
import interactionPlugin, { DateClickArg } from '@fullcalendar/interaction';
import { formatDraftLabel } from './availability-draft.util';
import { hhmm, safeLocalHour, utcRequest } from '../../testing/booking-time-testing';
import { translocoTestingModule } from '../../testing/transloco-testing';
import taTr from '../../../../../public/i18n/teacher-availability/tr.json';
import taEn from '../../../../../public/i18n/teacher-availability/en.json';

// Scope sözlüğü gerçek dosyadan yüklenir; aksi halde şablon ham anahtarı basar ve etiket testleri anlamsızlaşır.
const translocoTesting = translocoTestingModule({
    langs: { 'teacher-availability/tr': taTr, 'teacher-availability/en': taEn },
});

/**
 * Helper: İçinde bulunulan (Pazartesi başlangıçlı) haftanın `dayOffset`. gününde bir an üretir.
 * FullCalendar da aynı `new Date()` haftasını gösterdiği için slot her zaman görünen aralıkta kalır.
 */
function createWeekTestDate(dayOffset: number, hour: number, minute: number = 0): Date {
    // Get Monday of current week
    const now = new Date();
    const day = now.getDay();
    const diff = now.getDate() - day + (day === 0 ? -6 : 1); // Monday
    const monday = new Date(now.setDate(diff));
    monday.setHours(0, 0, 0, 0);

    // Add dayOffset days (0=Mon, 3=Thu, 5=Sat, 6=Sun)
    const slotDate = new Date(monday);
    slotDate.setDate(slotDate.getDate() + dayOffset);
    slotDate.setHours(hour, minute, 0, 0);
    return slotDate;
}

/**
 * Create AvailabilitySlot within the visible week.
 * @param id - slot ID
 * @param dayOffset - 0-6 (Mon-Sun)
 * @param startHour - start time hour
 * @param endHour - end time hour
 * @param isBooked - booking status
 * @param bookingStatus - if booked, status type
 */
function createSlotInWeek(id: number, dayOffset: number, startHour: number, endHour: number, isBooked = false, bookingStatus?: ActiveBookingStatus | null): AvailabilitySlot {
    const start = createWeekTestDate(dayOffset, startHour);
    const end = createWeekTestDate(dayOffset, endHour);
    const dateStr = start.toISOString().split('T')[0];

    return {
        id,
        teacherId: 10,
        date: dateStr,
        startTime: start.toISOString().split('T')[1].substring(0, 8),
        endTime: end.toISOString().split('T')[1].substring(0, 8),
        createdAt: new Date().toISOString(),
        startUtc: start.toISOString(),
        endUtc: end.toISOString(),
        isBooked,
        bookingStatus,
    };
}

describe('AvailabilityWeekGridComponent', () => {
    beforeEach(() => {
        vi.useFakeTimers();
    });
    afterEach(() => {
        vi.useRealTimers();
    });
    let component: AvailabilityWeekGridComponent;
    let fixture: ComponentFixture<AvailabilityWeekGridComponent>;
    let originalHtmlLang: string | null;

    // Etiket/saat beklentileri Türkçe (24 saat) biçimdir. Bileşen dili `<html lang>`'tan okur
    // (activeAppLocale); gerçek LocaleService'i kuran diğer spec'ler bunu tarayıcı diline (en)
    // çekebildiği için ön koşul burada açıkça sabitlenir ve sonra geri yüklenir (#394).
    beforeEach(() => {
        originalHtmlLang = document.documentElement.getAttribute('lang');
        document.documentElement.setAttribute('lang', 'tr');
    });

    afterEach(() => {
        if (originalHtmlLang === null) {
            document.documentElement.removeAttribute('lang');
        }
        else {
            document.documentElement.setAttribute('lang', originalHtmlLang);
        }
    });

    beforeEach(async () => {
        await TestBed.configureTestingModule({
            imports: [AvailabilityWeekGridComponent, translocoTesting, NoopAnimationsModule],
            // Bitiş günü seçicisi (issue #179) bir DateAdapter ister; uygulamada date-fns adapter'ı app.config sağlar.
            providers: [provideNativeDateAdapter()],
        }).compileComponents();

        fixture = TestBed.createComponent(AvailabilityWeekGridComponent);
        component = fixture.componentInstance;
    });

    it('should create', () => {
        fixture.detectChanges();
        expect(component).toBeTruthy();
    });

    it('should render exactly one event for a single valid slot', async () => {
        fixture.componentRef.setInput('slots', [createSlotInWeek(1, 3, 14, 15)]);
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        expect(fixture.nativeElement.querySelectorAll('.fc-timegrid-event').length).toBe(1);
    });

    it('should apply is-free class to free slots', async () => {
        const slot = createSlotInWeek(1, 3, 14, 15, false); // Thu 14:00-15:00, free

        TestBed.runInInjectionContext(() => {
            fixture.componentRef.setInput('slots', [slot]);
        });

        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(500);

        const eventElements = fixture.nativeElement.querySelectorAll('.fc-timegrid-event') as NodeListOf<HTMLElement>;
        expect(eventElements.length).toBeGreaterThan(0);
        const freeEvent = Array.from(eventElements).find((el) => el.classList.contains('is-free'));
        expect(freeEvent).toBeTruthy();
    });

    it('should apply is-approved class to approved bookings', async () => {
        const slot = createSlotInWeek(1, 3, 14, 15, true, 'Approved');

        TestBed.runInInjectionContext(() => {
            fixture.componentRef.setInput('slots', [slot]);
        });

        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(500);

        const eventElements = fixture.nativeElement.querySelectorAll('.fc-timegrid-event') as NodeListOf<HTMLElement>;
        expect(eventElements.length).toBeGreaterThan(0);
        const approvedEvent = Array.from(eventElements).find((el) => el.classList.contains('is-approved'));
        expect(approvedEvent).toBeTruthy();
    });

    it('should apply is-pending class to pending bookings', async () => {
        const slot = createSlotInWeek(1, 3, 14, 15, true, 'Pending');

        TestBed.runInInjectionContext(() => {
            fixture.componentRef.setInput('slots', [slot]);
        });

        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(500);

        const eventElements = fixture.nativeElement.querySelectorAll('.fc-timegrid-event') as NodeListOf<HTMLElement>;
        expect(eventElements.length).toBeGreaterThan(0);
        const pendingEvent = Array.from(eventElements).find((el) => el.classList.contains('is-pending'));
        expect(pendingEvent).toBeTruthy();
    });

    it('should apply is-past class to past slots', async () => {
        // Saat haftanın ortasına (Çarşamba 12:00) sabitlenir; böylece "geçmiş" slot her zaman görünen haftada kalır
        // (Pazartesi gece yarısı civarında koşulsa bile).
        const past = createSlotInWeek(1, 0, 10, 11); // Pzt 10:00-11:00
        const future = createSlotInWeek(2, 4, 10, 11); // Cum 10:00-11:00
        vi.setSystemTime(createWeekTestDate(2, 12));

        fixture.componentRef.setInput('slots', [past, future]);
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        const events = Array.from(fixture.nativeElement.querySelectorAll('.fc-timegrid-event') as NodeListOf<HTMLElement>);
        expect(events.length).toBe(2);
        expect(events.filter((el) => el.classList.contains('is-past')).length).toBe(1);
    });

    it('should render legend with three status items plus the recurring marker', async () => {
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        const legendItems = fixture.nativeElement.querySelectorAll('.awg__legend-item');
        expect(legendItems.length).toBe(4);
        const recurring = legendItems[3] as HTMLElement;
        // Ligatür metni ("repeat") ikonun içinde kalır; görünür etiket çeviridir.
        expect(recurring.textContent?.replace('repeat', '').trim()).toBe(taTr.grid.recurring.legend);
        expect(recurring.querySelector('.awg__legend-icon')?.getAttribute('aria-hidden')).toBe('true');
        expect(recurring.querySelector('.awg__swatch')).toBeNull();
    });

    it('should render legend swatches with correct status classes', async () => {
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        const swatches = fixture.nativeElement.querySelectorAll('.awg__swatch');
        const classes = Array.from(swatches as NodeListOf<HTMLElement>).map((el) => {
            if (el.classList.contains('is-free'))
                return 'is-free';
            if (el.classList.contains('is-pending'))
                return 'is-pending';
            if (el.classList.contains('is-approved'))
                return 'is-approved';
            return null;
        });

        expect(classes).toContain('is-free');
        expect(classes).toContain('is-pending');
        expect(classes).toContain('is-approved');
    });

    it('should show loading indicator when loading input is true', async () => {
        TestBed.runInInjectionContext(() => {
            fixture.componentRef.setInput('loading', true);
        });

        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        const section = fixture.nativeElement.querySelector('.awg--loading');
        expect(section).toBeTruthy();
    });

    it('should set aria-busy when loading', async () => {
        TestBed.runInInjectionContext(() => {
            fixture.componentRef.setInput('loading', true);
        });

        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        const section = fixture.nativeElement.querySelector('.awg');
        expect(section.getAttribute('aria-busy')).toBe('true');
    });

    it('should render navigation toolbar with prev/next/today buttons', async () => {
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(100);

        const navGroup = fixture.nativeElement.querySelector('[role="group"]') as HTMLElement;
        expect(navGroup).toBeTruthy();

        const buttons = navGroup?.querySelectorAll('button') as NodeListOf<HTMLButtonElement>;
        expect(buttons?.length).toBeGreaterThanOrEqual(3);
    });

    describe('hafta gezinme', () => {
        /** `rangeTitle` `datesSet` içinde `queueMicrotask` ile yazılır: mikro görevleri boşalt, sonra yeniden render et. */
        async function rangeText(): Promise< string > {
            await vi.advanceTimersByTimeAsync(0);
            fixture.detectChanges();
            return (fixture.nativeElement.querySelector('.awg__range') as HTMLElement).textContent?.trim() ?? '';
        }

        function navButtons(): HTMLButtonElement[] {
            return Array.from(fixture.nativeElement.querySelectorAll('.awg__nav button') as NodeListOf<HTMLButtonElement>);
        }

        it('ilk render sonrası hafta aralığı başlığı dolu', async () => {
            fixture.detectChanges();

            expect(await rangeText()).not.toBe('');
            expect(fixture.nativeElement.querySelector('full-calendar')).toBeTruthy();
        });

        it('sonraki / önceki hafta başlığı değiştirir ve geri döndürür', async () => {
            fixture.detectChanges();
            const initial = await rangeText();
            const [prev, , next] = navButtons();

            next.click();
            const afterNext = await rangeText();
            expect(afterNext).not.toBe(initial);

            prev.click();
            expect(await rangeText()).toBe(initial);

            prev.click();
            const afterPrev = await rangeText();
            expect(afterPrev).not.toBe(initial);
            expect(afterPrev).not.toBe(afterNext);
        });

        it('"Bugün" içinde bulunulan haftaya döndürür', async () => {
            fixture.detectChanges();
            const initial = await rangeText();
            const [, today, next] = navButtons();

            next.click();
            next.click();
            expect(await rangeText()).not.toBe(initial);

            today.click();
            expect(await rangeText()).toBe(initial);
        });

        it('gezinme sonrası yalnızca o haftanın olayları görünür', async () => {
            fixture.componentRef.setInput('slots', [createSlotInWeek(1, 3, 14, 15)]);
            fixture.detectChanges();
            await vi.advanceTimersByTimeAsync(0);
            expect(fixture.nativeElement.querySelectorAll('.fc-timegrid-event').length).toBe(1);

            navButtons()[2].click();
            await vi.advanceTimersByTimeAsync(0);
            fixture.detectChanges();
            expect(fixture.nativeElement.querySelectorAll('.fc-timegrid-event').length).toBe(0);
        });
    });

    describe('tıkla-seç ile taslak aralık (issue #176)', () => {
        /** "Şimdi" Çarşamba 12:00'ye sabitlenir; Cuma (offset 4) her zaman gelecekte, Pazartesi (0) geçmişte kalır. */
        const H = safeLocalHour(createWeekTestDate(4, 12));

        async function setup(slots: AvailabilitySlot[] = []): Promise< void > {
            vi.setSystemTime(createWeekTestDate(2, 12));
            fixture.componentRef.setInput('editable', true);
            fixture.componentRef.setInput('slots', slots);
            await render();
        }

        async function render(): Promise< void > {
            fixture.detectChanges();
            await vi.advanceTimersByTimeAsync(0);
            fixture.detectChanges();
        }

        /** FullCalendar işaretçi olaylarını taklit etmek yerine `dateClick` handler'ı çağrılır; sonuç DOM'dan okunur. */
        async function clickCell(dayOffset: number, hour: number, minute = 0): Promise< void > {
            component['onDateClick']({ date: createWeekTestDate(dayOffset, hour, minute) });
            await render();
        }

        function el<T extends HTMLElement = HTMLElement>(selector: string): T | null {
            return fixture.nativeElement.querySelector(selector) as T | null;
        }

        function draftEvents(): HTMLElement[] {
            return Array.from(fixture.nativeElement.querySelectorAll('.fc-bg-event.awg-draft') as NodeListOf<HTMLElement>);
        }

        function barText(): string {
            return el('.awg__draft-text')?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
        }

        it('editable kapalıyken onay çubuğu yoktur ve hücre tıklaması taslak kurmaz', async () => {
            vi.setSystemTime(createWeekTestDate(2, 12));
            fixture.detectChanges();
            await vi.advanceTimersByTimeAsync(0);

            await clickCell(4, H + 1);

            expect(el('.awg__draft')).toBeNull();
            expect(draftEvents().length).toBe(0);
            expect(component.draft()).toBeNull();
        });

        it('taslak yokken yönerge metni görünür, Kaydet/Vazgeç yoktur', async () => {
            await setup();

            expect(barText()).toBe(taTr.grid.draft.instruction);
            expect(el('.awg__draft-save')).toBeNull();
            expect(el('.awg__draft-cancel')).toBeNull();
            expect(el('.awg__draft-text')?.getAttribute('aria-live')).toBe('polite');
        });

        it('boş hücreye tıklama 30 dk taslağı hem grid üzerinde hem onay çubuğunda gösterir', async () => {
            await setup();

            await clickCell(4, H + 1);

            expect(draftEvents().length).toBe(1);
            // Tam etiket (gün + saat): gevşek `toContain(gün numarası)` saat metniyle de eşleşebilirdi.
            const expected = formatDraftLabel({ start: createWeekTestDate(4, H + 1), end: createWeekTestDate(4, H + 1, 30) });
            expect(el('.awg__draft-range')?.textContent?.trim()).toBe(expected);
            expect(expected).toContain(`${hhmm(H + 1)} – ${hhmm(H + 1, 30)}`);
            expect(barText()).not.toContain(taTr.grid.draft.instruction);
            expect(el('.awg__draft-save')?.textContent?.trim()).toBe(taTr.grid.draft.save);
            expect(el('.awg__draft-cancel')?.textContent?.trim()).toBe(taTr.grid.draft.cancel);
        });

        it('art arda tıklama taslağı genişletir; grid üzerinde tek taslak olayı kalır', async () => {
            await setup();

            await clickCell(4, H + 1);
            await clickCell(4, H + 2);

            expect(barText()).toContain(`${hhmm(H + 1)} – ${hhmm(H + 2, 30)}`);
            expect(draftEvents().length).toBe(1);
            expect(component.draft()).toEqual({ start: createWeekTestDate(4, H + 1), end: createWeekTestDate(4, H + 2, 30) });
        });

        it('taslak olayı tab durağı eklemez ve slot olaylarının erişilebilirliğini bozmaz', async () => {
            await setup([createSlotInWeek(1, 3, 14, 15)]);

            await clickCell(4, H + 1);

            expect(draftEvents().length).toBe(1);
            expect(draftEvents()[0].querySelector('[tabindex]')).toBeNull();
            expect(draftEvents()[0].querySelector('.awg__ev')).toBeNull();
            expect(draftEvents()[0].textContent?.trim()).toBe(taTr.grid.draft.tag);
            expect(draftEvents()[0].querySelector('.awg__draft-tag')?.getAttribute('aria-hidden')).toBe('true');
            const wrappers = fixture.nativeElement.querySelectorAll('.awg__ev') as NodeListOf<HTMLElement>;
            expect(wrappers.length).toBe(1);
            expect(wrappers[0].getAttribute('tabindex')).toBe('0');
            expect(wrappers[0].getAttribute('aria-label')).toContain('14:00');
        });

        it('Vazgeç taslağı hem grid üzerinden hem çubuktan temizler', async () => {
            await setup();
            await clickCell(4, H + 1);

            el<HTMLButtonElement>('.awg__draft-cancel')!.click();
            await render();

            expect(draftEvents().length).toBe(0);
            expect(component.draft()).toBeNull();
            expect(barText()).toBe(taTr.grid.draft.instruction);
            expect(el('.awg__draft-save')).toBeNull();
        });

        it('Kaydet, taslak anlarının UTC gün + saat isteğini yayar ve taslağı kendisi temizlemez', async () => {
            await setup();
            const emitted: CreateAvailabilitySlotRequest[] = [];
            component.createRequested.subscribe((req) => emitted.push(req));
            await clickCell(4, H + 1);
            await clickCell(4, H + 2);

            el<HTMLButtonElement>('.awg__draft-save')!.click();
            await render();

            // Yerel saat string'i DEĞİL: tıklanan anların UTC günü/saati.
            expect(emitted).toEqual([utcRequest(createWeekTestDate(4, H + 1), createWeekTestDate(4, H + 2, 30))]);
            // Temizleme sayfanın işidir (başarıda); hata olursa taslak düzeltilebilsin.
            expect(draftEvents().length).toBe(1);
        });

        it('saving iken butonlar aria-disabled, Kaydet yaymaz, hücre tıklaması ve Escape taslağı değiştirmez', async () => {
            await setup();
            const emitted: CreateAvailabilitySlotRequest[] = [];
            component.createRequested.subscribe((req) => emitted.push(req));
            await clickCell(4, H + 1);

            fixture.componentRef.setInput('saving', true);
            await render();

            const save = el<HTMLButtonElement>('.awg__draft-save')!;
            const cancel = el<HTMLButtonElement>('.awg__draft-cancel')!;
            expect(save.getAttribute('aria-disabled')).toBe('true');
            expect(cancel.getAttribute('aria-disabled')).toBe('true');
            expect(save.textContent?.trim()).toBe(taTr.grid.draft.saving);

            // `disabledInteractive` buton tıklamayı almaya devam eder; engelleme komponentin kendi korumasındadır.
            save.click();
            cancel.click();
            await clickCell(4, H + 3);
            el('.awg__scroll')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
            await render();

            expect(emitted).toEqual([]);
            expect(barText()).toContain(`${hhmm(H + 1)} – ${hhmm(H + 1, 30)}`);
            expect(draftEvents().length).toBe(1);
        });

        it('saveError onay çubuğunda role="alert" ile gösterilir ve taslak yerinde kalır', async () => {
            await setup();
            await clickCell(4, H + 1);

            fixture.componentRef.setInput('saveError', 'Bu aralık mevcut bir aralıkla çakışıyor.');
            await render();

            const alert = el('.awg__draft [role="alert"]');
            expect(alert?.textContent?.trim()).toBe('Bu aralık mevcut bir aralıkla çakışıyor.');
            expect(draftEvents().length).toBe(1);
            expect(barText()).toContain(`${hhmm(H + 1)} – ${hhmm(H + 1, 30)}`);
        });

        it('hata yokken alert elemanı render edilmez', async () => {
            await setup();
            await clickCell(4, H + 1);

            expect(el('.awg__draft [role="alert"]')).toBeNull();
        });

        it('geçmiş hücre taslak kurmaz ve ipucu gösterir; geçerli tıklama ipucunu siler', async () => {
            await setup();

            await clickCell(0, 10);

            expect(draftEvents().length).toBe(0);
            expect(barText()).toContain(taTr.grid.hint.past);

            await clickCell(4, H + 1);

            expect(barText()).not.toContain(taTr.grid.hint.past);
            expect(draftEvents().length).toBe(1);
        });

        it('mevcut slotun üzerinden genişletme reddedilir: ipucu görünür, taslak aynı kalır', async () => {
            await setup([createSlotInWeek(1, 4, H + 1, H + 2)]);
            await clickCell(4, H);

            await clickCell(4, H + 3);

            expect(barText()).toContain(`${hhmm(H)} – ${hhmm(H, 30)}`);
            expect(barText()).toContain(taTr.grid.hint.occupied);
        });

        it('yerel 23:30 hücresi (bitiş = ertesi yerel gün 00:00) her dilimde taslak olarak çizilir (issue #300)', async () => {
            await setup();
            const end = createWeekTestDate(5, 0);

            await clickCell(4, 23, 30);

            // Tek sütunda tek arka plan olayı; tam gece yarısında biten aralık "(+1 gün)" almaz.
            expect(draftEvents().length).toBe(1);
            const expected = formatDraftLabel({ start: createWeekTestDate(4, 23, 30), end });
            expect(el('.awg__draft-range')?.textContent?.trim()).toBe(expected);
            expect(expected).toContain(`${hhmm(23, 30)} – ${hhmm(0)}`);
            expect(el('.awg__next-day')).toBeNull();
            expect(component.draft()).toEqual({ start: createWeekTestDate(4, 23, 30), end });
        });

        describe('gece yarısını aşan aralık (issue #300)', () => {
            it('Cuma 23:00 + Cumartesi 00:30 tıklaması tek taslak olur; FullCalendar iki sütunda çizer, çubukta "+1 gün"', async () => {
                await setup();

                await clickCell(4, 23);
                await clickCell(5, 0, 30);

                const start = createWeekTestDate(4, 23);
                const end = createWeekTestDate(5, 1);
                expect(component.draft()).toEqual({ start, end });
                // FullCalendar gün aşan olayı yerel gece yarısında böler: iki arka plan parçası.
                expect(draftEvents().length).toBe(2);
                expect(el('.awg__draft-range .awg__next-day')?.textContent?.trim()).toBe(taTr.grid.nextDay);
                expect(barText()).toContain(`${hhmm(23)} – ${hhmm(1)} ${taTr.grid.nextDay}`);
                expect(el('.awg__draft-hint')).toBeNull();
            });

            it('Kaydet gün aşan taslağın UTC isteğini yayar (endTime < startTime olabilir, date = başlangıcın UTC günü)', async () => {
                await setup();
                const emitted: CreateAvailabilitySlotRequest[] = [];
                component.createRequested.subscribe((req) => emitted.push(req));
                await clickCell(4, 23);
                await clickCell(5, 0, 30);

                el<HTMLButtonElement>('.awg__draft-save')!.click();
                await render();

                expect(emitted).toEqual([utcRequest(createWeekTestDate(4, 23), createWeekTestDate(5, 1))]);
            });

            it('İstanbul (UTC+3) yerel 02:30–03:30: istek önceki UTC günü, 23:30–00:30; kural Perşembe', async () => {
                await setup();
                const friday = createWeekTestDate(4, 0);
                // Cuma 02:30 +03:00 = Perşembe 23:30Z (ofset sabit; makinenin diliminden bağımsız).
                const start = new Date(Date.UTC(friday.getFullYear(), friday.getMonth(), friday.getDate(), -1, 30));
                const end = new Date(start.getTime() + 60 * 60000);
                const emitted: CreateAvailabilitySlotRequest[] = [];
                const rules: CreateRecurringRuleRequest[] = [];
                component.createRequested.subscribe((req) => emitted.push(req));
                component.createRecurringRequested.subscribe((req) => rules.push(req));

                component['onDateClick']({ date: start });
                component['onDateClick']({ date: new Date(start.getTime() + 30 * 60000) });
                await render();

                expect(component['hint']()).toBeNull();
                expect(component.draft()).toEqual({ start, end });

                el<HTMLButtonElement>('.awg__draft-save')!.click();
                component['repeatWeekly'].set(true);
                el<HTMLButtonElement>('.awg__draft-save')!.click();
                await render();

                const thursdayUtc = new Date(start).toISOString().slice(0, 10);
                expect(emitted).toEqual([{ date: thursdayUtc, startTime: '23:30:00', endTime: '00:30:00' }]);
                expect(rules.length).toBe(1);
                expect(rules[0]).toEqual(expect.objectContaining({ dayOfWeek: 4, startTime: '23:30:00', endTime: '00:30:00', effectiveFrom: thursdayUtc }));
            });

            it('kayıtlı gün aşan slot iki parça çizilir: tek tab durağı ilk parçada, devam parçası aria-hidden', async () => {
                const start = createWeekTestDate(4, 23);
                const end = createWeekTestDate(5, 1);
                const crossing: AvailabilitySlot = {
                    id: 42,
                    teacherId: 10,
                    date: start.toISOString().slice(0, 10),
                    startTime: start.toISOString().slice(11, 19),
                    endTime: end.toISOString().slice(11, 19),
                    createdAt: new Date().toISOString(),
                    startUtc: start.toISOString(),
                    endUtc: end.toISOString(),
                    isBooked: false,
                };
                await setup([crossing]);

                expect(fixture.nativeElement.querySelectorAll('.fc-timegrid-event').length).toBe(2);
                const focusable = fixture.nativeElement.querySelectorAll('.awg__ev[tabindex="0"]') as NodeListOf<HTMLElement>;
                expect(focusable.length).toBe(1);
                expect(focusable[0].querySelector('.awg__next-day')?.textContent?.trim()).toBe(taTr.grid.nextDay);
                expect(focusable[0].getAttribute('aria-label')).toContain(taTr.grid.nextDay);
                const continuation = el('.awg__ev--continuation');
                expect(continuation?.getAttribute('aria-hidden')).toBe('true');
                expect(continuation?.hasAttribute('tabindex')).toBe(false);
            });

            it('hafta sınırı: Pazar 23:30 – Pzt 00:30 slotu sonraki haftada tam etiketli tek tab durağı olarak çizilir', async () => {
                // Görünen hafta Pazartesi başlar (`firstDay: 1`); ilk parça (önceki Pazar) ekranda değildir.
                const start = createWeekTestDate(-1, 23, 30);
                const end = createWeekTestDate(0, 0, 30);
                const crossing: AvailabilitySlot = {
                    id: 43,
                    teacherId: 10,
                    date: start.toISOString().slice(0, 10),
                    startTime: start.toISOString().slice(11, 19),
                    endTime: end.toISOString().slice(11, 19),
                    createdAt: new Date().toISOString(),
                    startUtc: start.toISOString(),
                    endUtc: end.toISOString(),
                    isBooked: false,
                };
                await setup([crossing]);

                expect(fixture.nativeElement.querySelectorAll('.fc-timegrid-event').length).toBe(1);
                expect(el('.awg__ev--continuation')).toBeNull();
                const focusable = fixture.nativeElement.querySelectorAll('.awg__ev[tabindex="0"]') as NodeListOf<HTMLElement>;
                expect(focusable.length).toBe(1);
                expect(focusable[0].getAttribute('data-slot-id')).toBe('43');
                expect(focusable[0].getAttribute('aria-label')).toContain(taTr.grid.nextDay);
                expect(focusable[0].getAttribute('aria-hidden')).toBeNull();
            });

            it('isContinuation: yalnız ilk parçası görünen haftada olan devam parçası için true', () => {
                const activeStart = createWeekTestDate(0, 0);
                const arg = (isStart: boolean, start: Date) => ({ isStart, event: { start }, view: { activeStart } }) as unknown as EventContentArg;

                expect(component['isContinuation'](arg(true, createWeekTestDate(4, 23)))).toBe(false);
                expect(component['isContinuation'](arg(false, createWeekTestDate(4, 23)))).toBe(true);
                expect(component['isContinuation'](arg(false, createWeekTestDate(-1, 23, 30)))).toBe(false);
            });
        });

        it('reddedilen tıklamanın ipucu, taslak kalkınca (kayıt başarısı) ekranda kalmaz', async () => {
            await setup();
            await clickCell(4, H);
            await clickCell(4, H + 4);
            expect(barText()).toContain(taTr.grid.hint.tooLong.replace('{{hours}}', '4'));

            fixture.componentRef.setInput('draft', null);
            await render();

            expect(barText()).toBe(taTr.grid.draft.instruction);
        });


        it('4 saati aşan genişletmede sınır değeriyle ipucu gösterir', async () => {
            await setup();
            await clickCell(4, H);

            await clickCell(4, H + 4);

            expect(barText()).toContain(`${hhmm(H)} – ${hhmm(H, 30)}`);
            expect(barText()).toContain(taTr.grid.hint.tooLong.replace('{{hours}}', '4'));
        });
    });

    describe('grid üzerinden silme (issue #177)', () => {
        const H = safeLocalHour(createWeekTestDate(4, 12));
        /** Cuma H:00–(H+1):00, boş. */
        const free = () => createSlotInWeek(11, 4, H, H + 1);
        /** Cuma (H+2):00–(H+3):00, bekleyen randevulu. */
        const booked = () => createSlotInWeek(12, 4, H + 2, H + 3, true, 'Pending');

        async function setup(slots: AvailabilitySlot[] = [free(), booked()], editable = true): Promise< void > {
            vi.setSystemTime(createWeekTestDate(2, 12));
            fixture.componentRef.setInput('editable', editable);
            fixture.componentRef.setInput('slots', slots);
            await render();
        }

        async function render(): Promise< void > {
            fixture.detectChanges();
            await vi.advanceTimersByTimeAsync(0);
            fixture.detectChanges();
        }

        function el<T extends HTMLElement = HTMLElement>(selector: string): T | null {
            return fixture.nativeElement.querySelector(selector) as T | null;
        }

        /** FullCalendar `eventClick` handler'ı seçeneklerden çağrılır (fare/dokunma yolu); DOM'dan doğrulanır. */
        async function clickEvent(slotId: number): Promise< void > {
            component['options']().eventClick!({ event: { id: String(slotId) } } as EventClickArg);
            await render();
        }

        async function clickCell(dayOffset: number, hour: number): Promise< void > {
            component['onDateClick']({ date: createWeekTestDate(dayOffset, hour) });
            await render();
        }

        /** Slotun `.awg__ev` sarmalayıcısı (`data-slot-id` ile). */
        function wrapperOf(slotId: number): HTMLElement {
            const hit = el(`.awg__ev[data-slot-id="${slotId}"]`);
            expect(hit, `slot ${slotId} render edilmeli`).not.toBeNull();
            return hit!;
        }

        function selectedEvents(): HTMLElement[] {
            return Array.from(fixture.nativeElement.querySelectorAll('.fc-timegrid-event.is-selected') as NodeListOf<HTMLElement>);
        }

        function barText(): string {
            return el('.awg__draft-text')?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
        }

        function draftEvents(): HTMLElement[] {
            return Array.from(fixture.nativeElement.querySelectorAll('.fc-bg-event.awg-draft') as NodeListOf<HTMLElement>);
        }

        it('takvim seçenekleri eventClick handler\'ı içerir ve eventInteractive AÇIKÇA kapalıdır', async () => {
            await setup();

            const options = component['options']();
            expect(typeof options.eventClick).toBe('function');
            // `eventClick` bağlıyken FullCalendar `eventInteractive`'i varsayılan açar; `.fc-event` tabindex almamalı.
            expect(options.eventInteractive).toBe(false);
            const fcEvents = fixture.nativeElement.querySelectorAll('.fc-event') as NodeListOf<HTMLElement>;
            expect(fcEvents.length).toBe(2);
            for (const event of Array.from(fcEvents)) {
                expect(event.hasAttribute('tabindex')).toBe(false);
            }
        });

        it('boş slota tıklama silme modunu açar: çubukta aralık + soru + Sil/Vazgeç, olay is-selected', async () => {
            await setup();

            await clickEvent(11);

            expect(component.selectedSlotId()).toBe(11);
            expect(el('.awg__draft')?.classList).toContain('awg__draft--delete');
            const expected = formatDraftLabel({ start: createWeekTestDate(4, H), end: createWeekTestDate(4, H + 1) });
            expect(el('.awg__draft-range')?.textContent?.trim()).toBe(expected);
            expect(barText()).toContain(taTr.grid.delete.prompt);
            expect(el('.awg__delete-confirm')?.textContent?.trim()).toBe(taTr.grid.delete.confirm);
            expect(el('.awg__delete-cancel')?.textContent?.trim()).toBe(taTr.grid.delete.cancel);
            expect(el('.awg__draft-save')).toBeNull();
            expect(selectedEvents().length).toBe(1);
            expect(selectedEvents()[0].classList).toContain('is-free');
            expect(wrapperOf(11).getAttribute('aria-pressed')).toBe('true');
        });

        it('randevulu slota tıklama silme moduna girmez; lockedHint ipucu görünür ve süre sonunda kalkar', async () => {
            await setup();

            await clickEvent(12);

            expect(component.selectedSlotId()).toBeNull();
            expect(selectedEvents().length).toBe(0);
            expect(el('.awg__delete-confirm')).toBeNull();
            expect(el('.awg__locked-hint')?.textContent?.trim()).toBe(taTr.actions.lockedHint);

            await vi.advanceTimersByTimeAsync(4000);
            fixture.detectChanges();

            expect(el('.awg__locked-hint')).toBeNull();
        });

        it('editable kapalıyken olay tıklaması seçim kurmaz ve sarmalayıcı role="group" kalır', async () => {
            await setup([free()], false);

            await clickEvent(11);

            expect(component.selectedSlotId()).toBeNull();
            expect(el('.awg__draft')).toBeNull();
            expect(el('.awg__ev')?.getAttribute('role')).toBe('group');
            expect(el('.awg__ev')?.hasAttribute('aria-description')).toBe(false);
        });

        it('editable iken sarmalayıcı role="button"; boş slot aria-description/tooltip ile silme ipucu taşır, randevulu taşımaz', async () => {
            await setup();

            const freeWrapper = wrapperOf(11);
            const bookedWrapper = wrapperOf(12);
            expect(freeWrapper.getAttribute('role')).toBe('button');
            expect(freeWrapper.getAttribute('aria-description')).toBe(taTr.grid.delete.hint);
            // aria-label değişmez (gün · saat · durum); eylem ipucu ayrı özniteliktedir.
            expect(freeWrapper.getAttribute('aria-label')).not.toContain(taTr.grid.delete.hint);
            expect(freeWrapper.getAttribute('aria-pressed')).toBe('false');
            expect(freeWrapper.hasAttribute('aria-disabled')).toBe(false);
            // Randevulu slot: rolü button ama eylemsiz → aria-disabled + "silinemez" ipucu.
            expect(bookedWrapper.getAttribute('role')).toBe('button');
            expect(bookedWrapper.getAttribute('aria-disabled')).toBe('true');
            expect(bookedWrapper.getAttribute('aria-description')).toBe(taTr.actions.lockedHint);
            expect(bookedWrapper.hasAttribute('aria-pressed')).toBe(false);
            expect(fixture.nativeElement.querySelectorAll('.awg__ev[tabindex="0"]').length).toBe(2);
        });

        it('editable kapalıyken aria-disabled ve aria-description hiçbir slotta yoktur', async () => {
            await setup([free(), booked()], false);

            for (const w of Array.from(fixture.nativeElement.querySelectorAll('.awg__ev') as NodeListOf<HTMLElement>)) {
                expect(w.hasAttribute('aria-disabled')).toBe(false);
                expect(w.hasAttribute('aria-description')).toBe(false);
            }
        });

        it('randevulu slota tıklama bayat taslak ipucunu siler; iki ipucu aynı anda görünmez', async () => {
            await setup();
            await clickCell(0, 10);
            expect(barText()).toContain(taTr.grid.hint.past);

            await clickEvent(12);

            expect(barText()).not.toContain(taTr.grid.hint.past);
            expect(el('.awg__locked-hint')).not.toBeNull();
            expect(fixture.nativeElement.querySelectorAll('.awg__draft-hint').length).toBe(1);
        });

        it('klavye: odaktaki boş slotta Enter seçer, Space seçimi kaldırır; varsayılan engellenir', async () => {
            await setup();
            const wrapper = wrapperOf(11);

            const enter = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });
            wrapper.dispatchEvent(enter);
            await render();
            expect(enter.defaultPrevented).toBe(true);
            expect(component.selectedSlotId()).toBe(11);
            expect(el('.awg__delete-confirm')).not.toBeNull();

            const space = new KeyboardEvent('keydown', { key: ' ', bubbles: true, cancelable: true });
            wrapperOf(11).dispatchEvent(space);
            await render();
            expect(space.defaultPrevented).toBe(true);
            expect(component.selectedSlotId()).toBeNull();
            expect(el('.awg__delete-confirm')).toBeNull();
        });

        it('klavye: randevulu slotta Enter seçim kurmaz, ipucu gösterir', async () => {
            await setup();

            wrapperOf(12).dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
            await render();

            expect(component.selectedSlotId()).toBeNull();
            expect(el('.awg__locked-hint')).not.toBeNull();
        });

        it('Sil, seçili slotun kimliğini yayar ve seçimi kendisi temizlemez', async () => {
            await setup();
            const emitted: number[] = [];
            component.deleteRequested.subscribe((id) => emitted.push(id));
            await clickEvent(11);

            el<HTMLButtonElement>('.awg__delete-confirm')!.click();
            await render();

            expect(emitted).toEqual([11]);
            expect(component.selectedSlotId()).toBe(11);
            expect(selectedEvents().length).toBe(1);
        });

        // Issue #439: yalnız veli onayı bekleyen talebi tutan slot silinebilir; çubukta "talep kapatılacak" uyarısı.
        it('AwaitingParent slot seçilir, uyarı gösterir ve Sil kimliğini yayar; Pending slot kilitli kalır', async () => {
            const awaiting = createSlotInWeek(13, 4, H + 4, H + 5, true, 'AwaitingParent');
            await setup([free(), booked(), awaiting]);
            const emitted: number[] = [];
            component.deleteRequested.subscribe((id) => emitted.push(id));

            expect(wrapperOf(13).getAttribute('aria-description')).toBe(taTr.grid.delete.hint);
            await clickEvent(13);

            expect(component.selectedSlotId()).toBe(13);
            expect(el('[data-test="awaiting-parent-warning"]')?.textContent?.trim()).toBe(taTr.grid.delete.awaitingParentWarning);
            el<HTMLButtonElement>('.awg__delete-confirm')!.click();
            await render();
            expect(emitted).toEqual([13]);

            await clickEvent(11);
            expect(el('[data-test="awaiting-parent-warning"]')).toBeNull();

            await clickEvent(12);
            expect(el('.awg__locked-hint')).not.toBeNull();
            await vi.advanceTimersByTimeAsync(4000);
        });

        it('Vazgeç seçimi ve vurguyu kaldırır; yönerge metnine dönülür', async () => {
            await setup();
            await clickEvent(11);

            el<HTMLButtonElement>('.awg__delete-cancel')!.click();
            await render();

            expect(component.selectedSlotId()).toBeNull();
            expect(selectedEvents().length).toBe(0);
            expect(el('.awg__delete-confirm')).toBeNull();
            expect(barText()).toBe(taTr.grid.draft.instruction);
        });

        it('grid içinden Escape seçimi kaldırır; dışarıdan gelen Escape kaldırmaz', async () => {
            await setup();
            await clickEvent(11);

            document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
            await render();
            expect(component.selectedSlotId()).toBe(11);

            el('.awg__scroll')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
            await render();
            expect(component.selectedSlotId()).toBeNull();
            expect(selectedEvents().length).toBe(0);
        });

        it('deleting iken butonlar aria-disabled (odak kaybolmaz), Sil yaymaz, tıklama ve Escape seçimi değiştirmez', async () => {
            await setup();
            const emitted: number[] = [];
            component.deleteRequested.subscribe((id) => emitted.push(id));
            await clickEvent(11);
            const confirmBtn = el<HTMLButtonElement>('.awg__delete-confirm')!;
            confirmBtn.focus();

            fixture.componentRef.setInput('deleting', true);
            await render();

            expect(confirmBtn.getAttribute('aria-disabled')).toBe('true');
            expect(confirmBtn.disabled).toBe(false);
            expect(confirmBtn.textContent?.trim()).toBe(taTr.grid.delete.deleting);
            expect(el('.awg__delete-cancel')?.getAttribute('aria-disabled')).toBe('true');
            expect(document.activeElement).toBe(confirmBtn);

            confirmBtn.click();
            el<HTMLButtonElement>('.awg__delete-cancel')!.click();
            await clickEvent(12);
            await clickCell(4, H + 4);
            el('.awg__scroll')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
            await render();

            expect(emitted).toEqual([]);
            expect(component.selectedSlotId()).toBe(11);
            expect(component.draft()).toBeNull();
            expect(el('.awg__locked-hint')).toBeNull();
        });

        it('deleteError çubukta role="alert" ile gösterilir ve seçim korunur', async () => {
            await setup();
            await clickEvent(11);
            expect(el('.awg__draft [role="alert"]')).toBeNull();

            fixture.componentRef.setInput('deleteError', 'Aktif randevusu olan aralık silinemez.');
            await render();

            expect(el('.awg__draft [role="alert"]')?.textContent?.trim()).toBe('Aktif randevusu olan aralık silinemez.');
            expect(component.selectedSlotId()).toBe(11);
            expect(selectedEvents().length).toBe(1);
        });

        it('sayfa slotu listeden düşürüp seçimi sıfırlayınca çubuk kapanır ve odak kaydırma bölgesine geçer', async () => {
            await setup();
            await clickEvent(11);
            el<HTMLButtonElement>('.awg__delete-confirm')!.focus();

            fixture.componentRef.setInput('slots', [booked()]);
            fixture.componentRef.setInput('selectedSlotId', null);
            await render();

            expect(el('.awg__delete-confirm')).toBeNull();
            expect(fixture.nativeElement.querySelectorAll('.fc-timegrid-event').length).toBe(1);
            expect(barText()).toBe(taTr.grid.draft.instruction);
            expect(document.activeElement).toBe(el('.awg__scroll'));
        });

        it('seçili kimlik slots\'tan düşerse silme modu kapanır ve model null\'a çekilip selectedSlotIdChange yayılır', async () => {
            await setup();
            const emitted: (number | null)[] = [];
            component.selectedSlotId.subscribe((id) => emitted.push(id));
            await clickEvent(11);
            expect(emitted).toEqual([11]);

            fixture.componentRef.setInput('slots', [booked()]);
            await render();

            expect(el('.awg__delete-confirm')).toBeNull();
            expect(selectedEvents().length).toBe(0);
            expect(component.selectedSlotId()).toBeNull();
            expect(emitted).toEqual([11, null]);
        });

        it('taslak varken slota tıklama taslağı siler; seçim varken hücreye tıklama seçimi siler', async () => {
            await setup();
            await clickCell(4, H + 4);
            expect(draftEvents().length).toBe(1);

            await clickEvent(11);

            expect(component.draft()).toBeNull();
            expect(draftEvents().length).toBe(0);
            expect(component.selectedSlotId()).toBe(11);
            expect(el('.awg__draft-save')).toBeNull();
            expect(el('.awg__delete-confirm')).not.toBeNull();

            await clickCell(4, H + 4);

            expect(component.selectedSlotId()).toBeNull();
            expect(selectedEvents().length).toBe(0);
            expect(component.draft()).not.toBeNull();
            expect(el('.awg__delete-confirm')).toBeNull();
            expect(el('.awg__draft-save')).not.toBeNull();
        });

        it('taslak reddi ipucu, slot seçilince ekranda kalmaz', async () => {
            await setup();
            await clickCell(0, 10);
            expect(barText()).toContain(taTr.grid.hint.past);

            await clickEvent(11);

            expect(barText()).not.toContain(taTr.grid.hint.past);
        });

        it('taslak (arka plan) olayına gelen eventClick yok sayılır', async () => {
            await setup();
            await clickCell(4, H + 4);

            component['options']().eventClick!({ event: { id: 'awg-draft' } } as EventClickArg);
            await render();

            expect(component.selectedSlotId()).toBeNull();
            expect(component.draft()).not.toBeNull();
        });

        // Duman testi: handler yerine render edilmiş `.fc-timegrid-event` üzerine GERÇEK click gönderilir;
        // FullCalendar `EventClicking` (document düzeyinde `.fc-event` seçicili delege click) `eventInteractive`
        // kapalıyken de `eventClick`'i tetiklemelidir. sahte zamanlayıcı kullanılmaz (bkz. #176 duman testi notu).
        it('duman: render edilmiş slot olayına gerçek click silme modunu açar', async () => {
            const settle = () => vi.advanceTimersByTimeAsync(20);
            fixture.componentRef.setInput('editable', true);
            fixture.componentRef.setInput('slots', [createSlotInWeek(11, 3, 14, 15)]);
            fixture.detectChanges();
            await settle();
            fixture.detectChanges();

            const target = el('.fc-timegrid-event .awg__ev');
            expect(target, 'slot olayı render edilmeli').not.toBeNull();
            if (!target) {
                return;
            }

            const init: MouseEventInit = { bubbles: true, cancelable: true, button: 0 };
            target.dispatchEvent(new MouseEvent('mousedown', init));
            target.dispatchEvent(new MouseEvent('mouseup', init));
            target.dispatchEvent(new MouseEvent('click', init));
            await settle();
            fixture.detectChanges();

            expect(component.selectedSlotId()).toBe(11);
            expect(component.draft()).toBeNull();
            expect(el('.awg__delete-confirm')).not.toBeNull();
            expect(el('.fc-timegrid-event.is-selected')).not.toBeNull();
        });
    });

    describe('tekrarlayan haftalık aralık (issue #179)', () => {
        const H = safeLocalHour(createWeekTestDate(4, 12));
        /** Cuma H:00–(H+1):00, boş, tekil. */
        const single = () => createSlotInWeek(11, 4, H, H + 1);
        /** Cuma (H+2):00–(H+3):00, boş, kural 5'ten üretilmiş. */
        const recurring = (): AvailabilitySlot => ({ ...createSlotInWeek(21, 4, H + 2, H + 3), recurringAvailabilityRuleId: 5 });
        /** Perşembe 14:00–15:00, bekleyen randevulu, kural 5'ten üretilmiş. */
        const recurringBooked = (): AvailabilitySlot => ({
            ...createSlotInWeek(22, 3, 14, 15, true, 'Pending'),
            recurringAvailabilityRuleId: 5,
        });

        async function setup(slots: AvailabilitySlot[] = [single(), recurring()], editable = true): Promise< void > {
            vi.setSystemTime(createWeekTestDate(2, 12));
            fixture.componentRef.setInput('editable', editable);
            fixture.componentRef.setInput('slots', slots);
            await render();
        }

        async function render(): Promise<void> {
            fixture.detectChanges();
            await vi.advanceTimersByTimeAsync(0);
            fixture.detectChanges();
        }

        function el<T extends HTMLElement = HTMLElement>(selector: string): T | null {
            return fixture.nativeElement.querySelector(selector) as T | null;
        }

        function all(selector: string): HTMLElement[] {
            return Array.from(fixture.nativeElement.querySelectorAll(selector) as NodeListOf<HTMLElement>);
        }

        async function clickEvent(slotId: number): Promise< void > {
            component['options']().eventClick!({ event: { id: String(slotId) } } as EventClickArg);
            await render();
        }

        async function clickCell(dayOffset: number, hour: number, minute = 0): Promise< void > {
            component['onDateClick']({ date: createWeekTestDate(dayOffset, hour, minute) });
            await render();
        }

        function wrapperOf(slotId: number): HTMLElement {
            const hit = el(`.awg__ev[data-slot-id="${slotId}"]`);
            expect(hit, `slot ${slotId} render edilmeli`).not.toBeNull();
            return hit!;
        }

        /** Onay çubuğundaki "Her hafta tekrarla" kutusunun native input'u. */
        function toggleInput(): HTMLInputElement | null {
            return el<HTMLInputElement>('.awg__repeat-toggle input[type="checkbox"]');
        }

        async function checkRepeat(): Promise< void > {
            const input = toggleInput();
            expect(input, 'tekrar kutusu render edilmeli').not.toBeNull();
            input!.click();
            await render();
        }

        /** Bitiş gününü datepicker seçimi gibi reactive kontrole yazar (validator'lar çalışır). */
        async function setUntil(value: Date | null): Promise<void> {
            component['untilControl'].setValue(value);
            await render();
        }

        /** NativeDateAdapter.toIso8601 karşılığı: anın UTC günü ("2026-10-01"). */
        function isoDay(d: Date): string {
            return d.toISOString().slice(0, 10);
        }

        // ---- Görsel ayrışma ----

        it('kuraldan üretilen slot is-recurring sınıfı, tekrar ikonu ve aria-label\'da "tekrarlayan" parçası taşır; tekil taşımaz', async () => {
            await setup([single(), recurring()], false);

            const events = all('.fc-timegrid-event');
            expect(events.length).toBe(2);
            expect(events.filter((e) => e.classList.contains('is-recurring')).length).toBe(1);

            const rec = wrapperOf(21);
            expect(rec.closest('.fc-timegrid-event')?.classList).toContain('is-recurring');
            // Durum rengi korunur: tekrarlayan olay yine is-free'dir.
            expect(rec.closest('.fc-timegrid-event')?.classList).toContain('is-free');
            const icon = rec.querySelector('.awg__ev-repeat');
            expect(icon?.textContent?.trim()).toBe('repeat');
            expect(icon?.getAttribute('aria-hidden')).toBe('true');
            expect(rec.getAttribute('aria-label')).toContain(taTr.grid.recurring.tooltip);

            const sgl = wrapperOf(11);
            expect(sgl.closest('.fc-timegrid-event')?.classList).not.toContain('is-recurring');
            expect(sgl.querySelector('.awg__ev-repeat')).toBeNull();
            expect(sgl.getAttribute('aria-label')).not.toContain(taTr.grid.recurring.tooltip);
        });

        it('tekrar ikonu tab durağı eklemez: olay başına tek tabindex, .fc-event\'te yok', async () => {
            await setup([recurring()], false);

            expect(all('[tabindex]').filter((e) => e.closest('.fc-timegrid-event')).length).toBe(1);
            expect(el('.fc-event')?.hasAttribute('tabindex')).toBe(false);
        });

        it('randevulu tekrarlayan slotta aria-label sıra: gün · saat · durum · öğrenci · tekrarlayan; aria-description "silinemez"', async () => {
            await setup([{ ...recurringBooked(), studentName: 'Ayşe' }]);

            const label = wrapperOf(22).getAttribute('aria-label') ?? '';
            const student = taTr.grid.tooltip.student.replace('{{name}}', 'Ayşe');
            expect(label.indexOf(taTr.status.pending)).toBeLessThan(label.indexOf(student));
            expect(label.indexOf(student)).toBeLessThan(label.indexOf(taTr.grid.recurring.tooltip));
            expect(wrapperOf(22).getAttribute('aria-description')).toBe(taTr.actions.lockedHint);
        });

        // ---- Oluşturma ----

        it('taslak yokken tekrar kutusu yoktur; taslak kurulunca işaretsiz gelir ve tarih seçici gizlidir', async () => {
            await setup([]);
            expect(toggleInput()).toBeNull();

            await clickCell(4, H + 4);

            expect(toggleInput()).not.toBeNull();
            expect(toggleInput()!.checked).toBe(false);
            expect(el('.awg__repeat-until')).toBeNull();
            expect(el('.awg__draft-save')?.textContent?.trim()).toBe(taTr.grid.draft.save);
        });

        it('kutu işaretsizken Kaydet tekil isteği yayar, kural isteği yaymaz', async () => {
            await setup([]);
            const single: CreateAvailabilitySlotRequest[] = [];
            const rules: CreateRecurringRuleRequest[] = [];
            component.createRequested.subscribe((r) => single.push(r));
            component.createRecurringRequested.subscribe((r) => rules.push(r));
            await clickCell(4, H + 4);

            el<HTMLButtonElement>('.awg__draft-save')!.click();
            await render();

            expect(single).toEqual([utcRequest(createWeekTestDate(4, H + 4), createWeekTestDate(4, H + 4, 30))]);
            expect(rules).toEqual([]);
        });

        it('kutu işaretliyken Kaydet kural isteğini yayar: UTC gün/saat, effectiveFrom = taslağın UTC günü, effectiveUntil null; taslak korunur', async () => {
            await setup([]);
            const single: CreateAvailabilitySlotRequest[] = [];
            const rules: CreateRecurringRuleRequest[] = [];
            component.createRequested.subscribe((r) => single.push(r));
            component.createRecurringRequested.subscribe((r) => rules.push(r));
            await clickCell(4, H + 4);
            await clickCell(4, H + 5);
            await checkRepeat();

            expect(el('.awg__repeat-until')).not.toBeNull();
            expect(el('.awg__draft-save')?.textContent?.trim()).toBe(taTr.grid.recurring.save);

            el<HTMLButtonElement>('.awg__draft-save')!.click();
            await render();

            const start = createWeekTestDate(4, H + 4);
            const end = createWeekTestDate(4, H + 5, 30);
            const expected = utcRequest(start, end);
            expect(rules).toEqual([
                {
                    dayOfWeek: start.getUTCDay() as CreateRecurringRuleRequest['dayOfWeek'],
                    startTime: expected.startTime,
                    endTime: expected.endTime,
                    effectiveFrom: expected.date,
                    effectiveUntil: null,
                },
            ]);
            expect(single).toEqual([]);
            expect(component.draft()).not.toBeNull();
        });

        it('bitiş günü seçilince effectiveUntil o günün (taslak saatindeki) UTC günü olur; seçici min/max taslak günü+7 / +1 yıl-1', async () => {
            await setup([]);
            const rules: CreateRecurringRuleRequest[] = [];
            component.createRecurringRequested.subscribe((r) => rules.push(r));
            await clickCell(4, H + 4);
            await checkRepeat();

            const start = createWeekTestDate(4, H + 4);
            const min = new Date(start.getFullYear(), start.getMonth(), start.getDate() + 7);
            const max = new Date(start.getFullYear() + 1, start.getMonth(), start.getDate() - 1);
            const input = el<HTMLInputElement>('.awg__repeat-until input')!;
            // matDatepicker `min`/`max` özniteliklerini `DateAdapter.toIso8601` ile yazar; testteki NativeDateAdapter
            // UTC bileşenlerini kullanır (yerel gece yarısı → UTC'nin doğusunda bir önceki gün). Değer sınır nesnesinden türer.
            expect(input.getAttribute('min')).toBe(isoDay(min));
            expect(input.getAttribute('max')).toBe(isoDay(max));
            expect(component['untilBounds']()).toEqual({ min, max });

            const until = new Date(start.getFullYear(), start.getMonth(), start.getDate() + 21);
            await setUntil(until);
            expect(component['untilInvalid']()).toBe(false);
            expect(el('.awg__repeat-error')).toBeNull();
            el<HTMLButtonElement>('.awg__draft-save')!.click();
            await render();

            const untilAtDraftTime = new Date(until.getFullYear(), until.getMonth(), until.getDate(), start.getHours(), start.getMinutes());
            expect(rules.length).toBe(1);
            expect(rules[0].effectiveUntil).toBe(untilAtDraftTime.toISOString().slice(0, 10));
        });

        it('tam +7 gün (ilk tekrar) sınırın içindedir ve effectiveUntil olarak gönderilir', async () => {
            await setup([]);
            const rules: CreateRecurringRuleRequest[] = [];
            component.createRecurringRequested.subscribe((r) => rules.push(r));
            await clickCell(4, H + 4);
            await checkRepeat();
            const start = createWeekTestDate(4, H + 4);
            const until = new Date(start.getFullYear(), start.getMonth(), start.getDate() + 7);

            await setUntil(until);
            expect(component['untilInvalid']()).toBe(false);
            el<HTMLButtonElement>('.awg__draft-save')!.click();
            await render();

            const untilAtDraftTime = new Date(until.getFullYear(), until.getMonth(), until.getDate(), start.getHours(), start.getMinutes());
            expect(rules.length).toBe(1);
            expect(rules[0].effectiveUntil).toBe(untilAtDraftTime.toISOString().slice(0, 10));
        });

        it('sınır dışı bitiş günü (taslak günü+7\'den önce) SESSİZCE süresize düşmez: hata görünür, Kaydet kilitli, istek yayılmaz', async () => {
            await setup([]);
            const rules: CreateRecurringRuleRequest[] = [];
            component.createRecurringRequested.subscribe((r) => rules.push(r));
            await clickCell(4, H + 4);
            await checkRepeat();
            const start = createWeekTestDate(4, H + 4);

            await setUntil(new Date(start.getFullYear(), start.getMonth(), start.getDate() + 3));

            expect(component['untilInvalid']()).toBe(true);
            expect(component['untilControl'].hasError('matDatepickerMin')).toBe(true);
            expect(el('.awg__repeat-error')?.textContent?.trim()).toBe(taTr.grid.recurring.untilOutOfRange);
            const save = el<HTMLButtonElement>('.awg__draft-save')!;
            expect(save.getAttribute('aria-disabled')).toBe('true');
            expect(save.disabled).toBe(false); // disabledInteractive: odak kaybolmaz
            save.click();
            await render();
            expect(rules).toEqual([]);
            expect(component.draft()).not.toBeNull();

            // Bir yıldan ileri de aynı yol (matDatepickerMax).
            await setUntil(new Date(start.getFullYear() + 1, start.getMonth(), start.getDate() + 30));
            expect(component['untilControl'].hasError('matDatepickerMax')).toBe(true);
            expect(component['untilInvalid']()).toBe(true);

            // Geçerli tarihe dönünce kilit açılır.
            await setUntil(new Date(start.getFullYear(), start.getMonth(), start.getDate() + 14));
            expect(component['untilInvalid']()).toBe(false);
            expect(el('.awg__repeat-error')).toBeNull();
            expect(save.getAttribute('aria-disabled')).toBeNull();
        });

        it('parse edilemeyen metin (değer null, kontrol geçersiz) de Kaydet\'i kilitler; süresiz olarak gitmez', async () => {
            await setup([]);
            const rules: CreateRecurringRuleRequest[] = [];
            component.createRecurringRequested.subscribe((r) => rules.push(r));
            await clickCell(4, H + 4);
            await checkRepeat();

            const input = el<HTMLInputElement>('.awg__repeat-until input')!;
            input.value = 'abc';
            input.dispatchEvent(new Event('input', { bubbles: true }));
            await render();

            expect(component['repeatUntil']()).toBeNull();
            expect(component['untilControl'].hasError('matDatepickerParse')).toBe(true);
            expect(component['untilInvalid']()).toBe(true);
            expect(el('.awg__repeat-error')).not.toBeNull();
            el<HTMLButtonElement>('.awg__draft-save')!.click();
            await render();
            expect(rules).toEqual([]);
        });

        it('tarih seçildikten sonra taslak başka güne taşınınca tarih min altına düşerse hata görünür ve Kaydet kilitlenir', async () => {
            await setup([]);
            await clickCell(4, H + 4); // Cuma
            await checkRepeat();
            const friday = createWeekTestDate(4, H + 4);
            // Cuma+7 geçerli; Cumartesi'ye taşınınca min Cumartesi+7 olur → Cuma+7 artık min altında.
            await setUntil(new Date(friday.getFullYear(), friday.getMonth(), friday.getDate() + 7));
            expect(component['untilInvalid']()).toBe(false);

            await clickCell(5, H + 4); // Cumartesi (taslak taşınır, kutu ve tarih korunur)

            expect(component.draft()?.start.getDay()).toBe(6);
            expect(toggleInput()!.checked).toBe(true);
            expect(component['untilInvalid']()).toBe(true);
            expect(el('.awg__repeat-error')?.textContent?.trim()).toBe(taTr.grid.recurring.untilOutOfRange);
            expect(el('.awg__draft-save')?.getAttribute('aria-disabled')).toBe('true');
        });

        it('bitiş günü alanında Escape taslağı SİLMEZ (yazımı iptal içindir); kaydırma bölgesinde Escape siler', async () => {
            await setup([]);
            await clickCell(4, H + 4);
            await checkRepeat();
            const input = el<HTMLInputElement>('.awg__repeat-until input')!;

            input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
            await render();

            expect(component.draft()).not.toBeNull();
            expect(toggleInput()!.checked).toBe(true);

            el('.awg__scroll')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
            await render();
            expect(component.draft()).toBeNull();
        });

        it('kutu kaldırılınca bitiş günü sıfırlanır; taslak kalkınca (Vazgeç) kutu ve tarih sıfırlanır', async () => {
            await setup([]);
            await clickCell(4, H + 4);
            await checkRepeat();
            const start = createWeekTestDate(4, H + 4);
            await setUntil(new Date(start.getFullYear(), start.getMonth(), start.getDate() + 14));
            expect(component['repeatUntil']()).not.toBeNull();

            toggleInput()!.click();
            await render();
            expect(component['repeatWeekly']()).toBe(false);
            expect(component['repeatUntil']()).toBeNull();
            expect(el('.awg__repeat-until')).toBeNull();

            await checkRepeat();
            el<HTMLButtonElement>('.awg__draft-cancel')!.click();
            await render();
            expect(component.draft()).toBeNull();
            expect(component['repeatWeekly']()).toBe(false);

            // Yeni taslak yine işaretsiz başlar.
            await clickCell(4, H + 4);
            expect(toggleInput()!.checked).toBe(false);
        });

        it('sayfa kayıt başarısında draft\'ı null yapınca kutu sıfırlanır (bir sonraki taslağa sızmaz)', async () => {
            await setup([]);
            await clickCell(4, H + 4);
            await checkRepeat();

            fixture.componentRef.setInput('draft', null);
            await render();
            await clickCell(4, H + 5);

            expect(toggleInput()!.checked).toBe(false);
            expect(component['repeatWeekly']()).toBe(false);
        });

        it('saving iken kutu ve Kaydet devre dışıdır; Kaydet kural isteği yaymaz', async () => {
            await setup([]);
            const rules: CreateRecurringRuleRequest[] = [];
            component.createRecurringRequested.subscribe((r) => rules.push(r));
            await clickCell(4, H + 4);
            await checkRepeat();

            fixture.componentRef.setInput('saving', true);
            await render();

            expect(toggleInput()!.disabled).toBe(true);
            expect(el('.awg__draft-save')?.getAttribute('aria-disabled')).toBe('true');
            el<HTMLButtonElement>('.awg__draft-save')!.click();
            await render();
            expect(rules).toEqual([]);
            expect(component['repeatWeekly']()).toBe(true);
        });

        it('saveError kural modunda da çubukta role="alert" ile görünür; taslak ve kutu korunur', async () => {
            await setup([]);
            await clickCell(4, H + 4);
            await checkRepeat();

            fixture.componentRef.setInput('saveError', 'Aynı gün kesişen bir kural var.');
            await render();

            expect(el('.awg__draft [role="alert"]')?.textContent?.trim()).toBe('Aynı gün kesişen bir kural var.');
            expect(component.draft()).not.toBeNull();
            expect(toggleInput()!.checked).toBe(true);
        });

        // ---- Silme ----

        it('tekil slot seçilince tek silme butonu ("Sil") ve "Tüm seri" yoktur', async () => {
            await setup();

            await clickEvent(11);

            expect(el('.awg__delete-confirm')?.textContent?.trim()).toBe(taTr.grid.delete.confirm);
            expect(el('.awg__delete-series')).toBeNull();
            expect(el('.awg__delete-prompt')?.textContent?.trim()).toBe(taTr.grid.delete.prompt);
        });

        it('tekrarlayan slot seçilince iki seçenek: "Sadece bu hafta" + "Tüm seri" + Vazgeç; soru metni seriye özel', async () => {
            await setup();

            await clickEvent(21);

            expect(component.selectedSlotId()).toBe(21);
            expect(el('.awg__delete-confirm')?.textContent?.trim()).toBe(taTr.grid.recurring.deleteThisWeek);
            expect(el('.awg__delete-series')?.textContent?.trim()).toContain(taTr.grid.recurring.deleteSeries);
            expect(el('.awg__delete-cancel')?.textContent?.trim()).toBe(taTr.grid.delete.cancel);
            expect(el('.awg__delete-prompt')?.textContent?.trim()).toBe(taTr.grid.recurring.deletePrompt);
            expect(all('.awg__draft-actions button').length).toBe(3);
            expect(el('.fc-timegrid-event.is-selected')?.classList).toContain('is-recurring');
        });

        it('"Sadece bu hafta" deleteRequested(slotId) yayar, deleteSeriesRequested yaymaz; seçim korunur', async () => {
            await setup();
            const slots: number[] = [];
            const series: number[] = [];
            component.deleteRequested.subscribe((id) => slots.push(id));
            component.deleteSeriesRequested.subscribe((id) => series.push(id));
            await clickEvent(21);

            el<HTMLButtonElement>('.awg__delete-confirm')!.click();
            await render();

            expect(slots).toEqual([21]);
            expect(series).toEqual([]);
            expect(component.selectedSlotId()).toBe(21);
        });

        it('"Tüm seri" deleteSeriesRequested(ruleId) yayar, deleteRequested yaymaz; seçim korunur', async () => {
            await setup();
            const slots: number[] = [];
            const series: number[] = [];
            component.deleteRequested.subscribe((id) => slots.push(id));
            component.deleteSeriesRequested.subscribe((id) => series.push(id));
            await clickEvent(21);

            el<HTMLButtonElement>('.awg__delete-series')!.click();
            await render();

            expect(series).toEqual([5]);
            expect(slots).toEqual([]);
            expect(component.selectedSlotId()).toBe(21);
        });

        it('deleting iken "Tüm seri" aria-disabled (odak kalır) ve yaymaz', async () => {
            await setup();
            const series: number[] = [];
            component.deleteSeriesRequested.subscribe((id) => series.push(id));
            await clickEvent(21);
            const btn = el<HTMLButtonElement>('.awg__delete-series')!;
            btn.focus();

            fixture.componentRef.setInput('deleting', true);
            await render();

            expect(btn.getAttribute('aria-disabled')).toBe('true');
            expect(btn.disabled).toBe(false);
            expect(document.activeElement).toBe(btn);
            // Her iki silme butonu da "Siliniyor..." gösterir (hangisine basıldığından bağımsız tek `deleting` durumu).
            expect(btn.textContent?.replace('repeat', '').trim()).toBe(taTr.grid.delete.deleting);
            expect(el('.awg__delete-confirm')?.textContent?.trim()).toBe(taTr.grid.delete.deleting);
            btn.click();
            await render();
            expect(series).toEqual([]);

            // Seri silme sürerken Escape seçimi kapatmaz.
            el('.awg__scroll')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
            await render();
            expect(component.selectedSlotId()).toBe(21);
            expect(el('.awg__delete-series')).not.toBeNull();
        });

        it('deleteError seri modunda da çubukta görünür; seçim ve iki buton korunur', async () => {
            await setup();
            await clickEvent(21);

            fixture.componentRef.setInput('deleteError', 'Seri silinemedi.');
            await render();

            expect(el('.awg__draft [role="alert"]')?.textContent?.trim()).toBe('Seri silinemedi.');
            expect(component.selectedSlotId()).toBe(21);
            expect(el('.awg__delete-series')).not.toBeNull();
        });

        it('"Tüm seri" sonrası sayfa seçimi sıfırlayıp slotları yenileyince çubuk kapanır ve odak kaydırma bölgesine geçer', async () => {
            await setup();
            await clickEvent(21);
            el<HTMLButtonElement>('.awg__delete-series')!.focus();

            fixture.componentRef.setInput('slots', [single()]);
            fixture.componentRef.setInput('selectedSlotId', null);
            await render();

            expect(el('.awg__delete-series')).toBeNull();
            expect(el('.awg__delete-confirm')).toBeNull();
            expect(document.activeElement).toBe(el('.awg__scroll'));
        });

        it('randevulu tekrarlayan slota tıklama seçim kurmaz, "silinemez" ipucu gösterir', async () => {
            await setup([recurringBooked()]);

            await clickEvent(22);

            expect(component.selectedSlotId()).toBeNull();
            expect(el('.awg__delete-series')).toBeNull();
            expect(el('.awg__locked-hint')).not.toBeNull();
        });

        it('klavye: tekrarlayan slotta Enter seçer ve iki silme seçeneği görünür; Escape ikisini de kapatır', async () => {
            await setup();

            wrapperOf(21).dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
            await render();
            expect(el('.awg__delete-series')).not.toBeNull();

            el('.awg__scroll')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
            await render();
            expect(component.selectedSlotId()).toBeNull();
            expect(el('.awg__delete-series')).toBeNull();
        });

        it('confirmDeleteSeries tekil slotta (kural yok) hiçbir şey yaymaz', async () => {
            await setup();
            const series: number[] = [];
            component.deleteSeriesRequested.subscribe((id) => series.push(id));
            await clickEvent(11);

            component['confirmDeleteSeries']();

            expect(series).toEqual([]);
        });
    });

    it('should have tabindex="0" on wrapper element and NOT on .fc-event', async () => {
        const slot = createSlotInWeek(1, 3, 14, 15, false); // Thu 14:00-15:00, free

        TestBed.runInInjectionContext(() => {
            fixture.componentRef.setInput('slots', [slot]);
        });

        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(500);

        // Wrapper .awg__ev should have tabindex="0"
        const eventWrappers = fixture.nativeElement.querySelectorAll('.awg__ev') as NodeListOf<HTMLElement>;
        expect(eventWrappers.length).toBeGreaterThan(0);
        const wrapperWithTabindex = Array.from(eventWrappers).find((el) => el.getAttribute('tabindex') === '0');
        expect(wrapperWithTabindex, 'Wrapper should have tabindex="0"').toBeTruthy();

        // .fc-event elements should NOT have tabindex (eventInteractive off)
        const fcEvents = fixture.nativeElement.querySelectorAll('.fc-event') as NodeListOf<HTMLElement>;
        for (const event of Array.from(fcEvents)) {
            expect(event.hasAttribute('tabindex'), '.fc-event should not have tabindex').toBeFalsy();
        }
    });

    it('should expose day, time, status and student name in aria-label of a booked slot', async () => {
        // Saati sabitle: slot (Perşembe) bugüne göre geçmişte kalırsa etikete "geçmiş" parçası eklenir.
        vi.setSystemTime(createWeekTestDate(2, 12));
        const slot = createSlotInWeek(1, 3, 14, 15, true, 'Approved');
        slot.studentName = 'Ayşe';

        fixture.componentRef.setInput('slots', [slot]);
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        const wrappers = fixture.nativeElement.querySelectorAll('.awg__ev') as NodeListOf<HTMLElement>;
        expect(wrappers.length).toBe(1);

        const label = wrappers[0].getAttribute('aria-label') ?? '';
        const parts = label.split(' · ');
        expect(parts.length).toBe(4); // gün · saat · durum · öğrenci
        expect(parts[1]).toContain('14:00');
        expect(parts[1]).toContain('15:00');
        expect(parts[2]).toBe(taTr.status.approved);
        expect(parts[3]).toBe('Öğrenci: Ayşe');
        expect(wrappers[0].querySelector('.awg__ev-note')?.textContent).toContain('Ayşe');
    });

    it('should not expose student name when slot is not booked', async () => {
        const slot = createSlotInWeek(1, 3, 14, 15, false);
        slot.studentName = 'Ayşe';

        fixture.componentRef.setInput('slots', [slot]);
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        const wrapper = fixture.nativeElement.querySelector('.awg__ev') as HTMLElement;
        const label = wrapper.getAttribute('aria-label') ?? '';
        expect(label).not.toContain('Ayşe');
        expect(label.split(' · ')[2]).toBe(taTr.status.free);
        expect(wrapper.querySelector('.awg__ev-note')?.textContent?.trim()).toBe(taTr.status.free);
    });

    it('should skip invalid slots', async () => {
        const validSlot = createSlotInWeek(1, 3, 14, 15, false); // Thu 14:00-15:00

        const invalidSlot: AvailabilitySlot = {
            id: 2,
            teacherId: 10,
            date: validSlot.date,
            startTime: '16:00:00',
            endTime: '15:00:00', // Invalid: end before start
            createdAt: new Date().toISOString(),
            startUtc: new Date(new Date(validSlot.startUtc).getTime() + 2 * 60 * 60 * 1000).toISOString(),
            endUtc: new Date(new Date(validSlot.startUtc).getTime() + 1 * 60 * 60 * 1000).toISOString(),
            isBooked: false,
        };

        TestBed.runInInjectionContext(() => {
            fixture.componentRef.setInput('slots', [validSlot, invalidSlot]);
        });

        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(500);

        // Component should render without error - invalid slot is skipped internally
        const calendar = fixture.nativeElement.querySelector('.awg__canvas') as HTMLElement;
        expect(calendar).toBeTruthy();

        // Verify only the valid slot is rendered (invalid one is skipped)
        const eventElements = fixture.nativeElement.querySelectorAll('.fc-timegrid-event') as NodeListOf<HTMLElement>;
        expect(eventElements.length, 'Should render only 1 valid event, skip invalid').toBe(1);
    });

    it('should handle empty slots', async () => {
        TestBed.runInInjectionContext(() => {
            fixture.componentRef.setInput('slots', []);
        });

        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        const section = fixture.nativeElement.querySelector('.awg');
        expect(section).toBeTruthy();
    });

    it('should only render calendar on browser (not SSR)', async () => {
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        const canvas = fixture.nativeElement.querySelector('.awg__canvas') as HTMLElement;
        expect(canvas).toBeTruthy();
    });

    it('should NOT render calendar in SSR environment but render toolbar', async () => {
        // Create a separate test bed with server platform
        const ssrTestBed = TestBed.resetTestingModule();
        ssrTestBed.configureTestingModule({
            imports: [AvailabilityWeekGridComponent, translocoTesting],
            providers: [{ provide: PLATFORM_ID, useValue: 'server' }],
        });

        const ssrFixture = ssrTestBed.createComponent(AvailabilityWeekGridComponent);
        ssrFixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        // In SSR, FullCalendar canvas should not be rendered
        const canvas = ssrFixture.nativeElement.querySelector('.awg__canvas') as HTMLElement;
        expect(canvas, 'Calendar canvas should NOT be rendered in SSR').toBeFalsy();

        // But toolbar should still be rendered
        const toolbar = ssrFixture.nativeElement.querySelector('.awg__toolbar') as HTMLElement;
        expect(toolbar, 'Toolbar should be rendered in SSR').toBeTruthy();

        // Scroll area should not be rendered in SSR
        const scrollArea = ssrFixture.nativeElement.querySelector('.awg__scroll') as HTMLElement;
        expect(scrollArea, 'Scroll area should NOT be rendered in SSR').toBeFalsy();
    });

    it('should have role="region" on scroll area for accessibility', async () => {
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        const scrollArea = fixture.nativeElement.querySelector('.awg__scroll');
        expect(scrollArea?.getAttribute('role')).toBe('region');
    });

    it('should include aria-label on scroll area', async () => {
        fixture.detectChanges();
        await vi.advanceTimersByTimeAsync(0);

        const scrollArea = fixture.nativeElement.querySelector('.awg__scroll');
        expect(scrollArea?.getAttribute('aria-label')).toBeTruthy();
    });
});
