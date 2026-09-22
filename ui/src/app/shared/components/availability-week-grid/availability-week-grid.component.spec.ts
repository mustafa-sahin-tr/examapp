import { ComponentFixture, TestBed, fakeAsync, tick, flush } from '@angular/core/testing';
import { PLATFORM_ID } from '@angular/core';
import { provideNativeDateAdapter } from '@angular/material/core';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { AvailabilityWeekGridComponent } from './availability-week-grid.component';
import {
  AvailabilitySlot,
  ActiveBookingStatus,
  CreateAvailabilitySlotRequest,
  CreateRecurringRuleRequest,
} from '../../../models/booking.model';
import type { EventClickArg } from '@fullcalendar/core';
import interactionPlugin, { DateClickArg } from '@fullcalendar/interaction';
import { formatDraftLabel, nextUtcDayBoundary } from './availability-draft.util';
import { activeIntlLocale } from '../../utils/active-locale.util';
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
function createSlotInWeek(
  id: number,
  dayOffset: number,
  startHour: number,
  endHour: number,
  isBooked = false,
  bookingStatus?: ActiveBookingStatus | null
): AvailabilitySlot {
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
  let component: AvailabilityWeekGridComponent;
  let fixture: ComponentFixture<AvailabilityWeekGridComponent>;

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

  it('should render exactly one event for a single valid slot', fakeAsync(() => {
    fixture.componentRef.setInput('slots', [createSlotInWeek(1, 3, 14, 15)]);
    fixture.detectChanges();
    tick();

    expect(fixture.nativeElement.querySelectorAll('.fc-timegrid-event').length).toBe(1);
  }));

  it('should apply is-free class to free slots', fakeAsync(() => {
    const slot = createSlotInWeek(1, 3, 14, 15, false); // Thu 14:00-15:00, free

    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('slots', [slot]);
    });

    fixture.detectChanges();
    tick(500);

    const eventElements = fixture.nativeElement.querySelectorAll('.fc-timegrid-event') as NodeListOf<HTMLElement>;
    expect(eventElements.length).toBeGreaterThan(0);
    const freeEvent = Array.from(eventElements).find((el) => el.classList.contains('is-free'));
    expect(freeEvent).toBeTruthy();
  }));

  it('should apply is-approved class to approved bookings', fakeAsync(() => {
    const slot = createSlotInWeek(1, 3, 14, 15, true, 'Approved');

    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('slots', [slot]);
    });

    fixture.detectChanges();
    tick(500);

    const eventElements = fixture.nativeElement.querySelectorAll('.fc-timegrid-event') as NodeListOf<HTMLElement>;
    expect(eventElements.length).toBeGreaterThan(0);
    const approvedEvent = Array.from(eventElements).find((el) => el.classList.contains('is-approved'));
    expect(approvedEvent).toBeTruthy();
  }));

  it('should apply is-pending class to pending bookings', fakeAsync(() => {
    const slot = createSlotInWeek(1, 3, 14, 15, true, 'Pending');

    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('slots', [slot]);
    });

    fixture.detectChanges();
    tick(500);

    const eventElements = fixture.nativeElement.querySelectorAll('.fc-timegrid-event') as NodeListOf<HTMLElement>;
    expect(eventElements.length).toBeGreaterThan(0);
    const pendingEvent = Array.from(eventElements).find((el) => el.classList.contains('is-pending'));
    expect(pendingEvent).toBeTruthy();
  }));

  it('should apply is-past class to past slots', fakeAsync(() => {
    // Saat haftanın ortasına (Çarşamba 12:00) sabitlenir; böylece "geçmiş" slot her zaman görünen haftada kalır
    // (Pazartesi gece yarısı civarında koşulsa bile).
    const past = createSlotInWeek(1, 0, 10, 11); // Pzt 10:00-11:00
    const future = createSlotInWeek(2, 4, 10, 11); // Cum 10:00-11:00
    jasmine.clock().mockDate(createWeekTestDate(2, 12));

    fixture.componentRef.setInput('slots', [past, future]);
    fixture.detectChanges();
    tick();

    const events = Array.from(
      fixture.nativeElement.querySelectorAll('.fc-timegrid-event') as NodeListOf<HTMLElement>
    );
    expect(events.length).toBe(2);
    expect(events.filter((el) => el.classList.contains('is-past')).length).toBe(1);
  }));

  it('should render legend with three status items plus the recurring marker', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const legendItems = fixture.nativeElement.querySelectorAll('.awg__legend-item');
    expect(legendItems.length).toBe(4);
    const recurring = legendItems[3] as HTMLElement;
    // Ligatür metni ("repeat") ikonun içinde kalır; görünür etiket çeviridir.
    expect(recurring.textContent?.replace('repeat', '').trim()).toBe(taTr.grid.recurring.legend);
    expect(recurring.querySelector('.awg__legend-icon')?.getAttribute('aria-hidden')).toBe('true');
    expect(recurring.querySelector('.awg__swatch')).toBeNull();
  }));

  it('should render legend swatches with correct status classes', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const swatches = fixture.nativeElement.querySelectorAll('.awg__swatch');
    const classes = Array.from(swatches as NodeListOf<HTMLElement>).map((el) => {
      if (el.classList.contains('is-free')) return 'is-free';
      if (el.classList.contains('is-pending')) return 'is-pending';
      if (el.classList.contains('is-approved')) return 'is-approved';
      return null;
    });

    expect(classes).toContain('is-free');
    expect(classes).toContain('is-pending');
    expect(classes).toContain('is-approved');
  }));

  it('should show loading indicator when loading input is true', fakeAsync(() => {
    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('loading', true);
    });

    fixture.detectChanges();
    tick();

    const section = fixture.nativeElement.querySelector('.awg--loading');
    expect(section).toBeTruthy();
  }));

  it('should set aria-busy when loading', fakeAsync(() => {
    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('loading', true);
    });

    fixture.detectChanges();
    tick();

    const section = fixture.nativeElement.querySelector('.awg');
    expect(section.getAttribute('aria-busy')).toBe('true');
  }));

  it('should render navigation toolbar with prev/next/today buttons', fakeAsync(() => {
    fixture.detectChanges();
    tick(100);

    const navGroup = fixture.nativeElement.querySelector('[role="group"]') as HTMLElement;
    expect(navGroup).toBeTruthy();

    const buttons = navGroup?.querySelectorAll('button') as NodeListOf<HTMLButtonElement>;
    expect(buttons?.length).toBeGreaterThanOrEqual(3);
  }));

  describe('hafta gezinme', () => {
    /** `rangeTitle` `datesSet` içinde `queueMicrotask` ile yazılır: mikro görevleri boşalt, sonra yeniden render et. */
    function rangeText(): string {
      tick();
      fixture.detectChanges();
      return (fixture.nativeElement.querySelector('.awg__range') as HTMLElement).textContent?.trim() ?? '';
    }

    function navButtons(): HTMLButtonElement[] {
      return Array.from(fixture.nativeElement.querySelectorAll('.awg__nav button') as NodeListOf<HTMLButtonElement>);
    }

    it('ilk render sonrası hafta aralığı başlığı dolu', fakeAsync(() => {
      fixture.detectChanges();

      expect(rangeText()).not.toBe('');
      expect(fixture.nativeElement.querySelector('full-calendar')).toBeTruthy();
    }));

    it('sonraki / önceki hafta başlığı değiştirir ve geri döndürür', fakeAsync(() => {
      fixture.detectChanges();
      const initial = rangeText();
      const [prev, , next] = navButtons();

      next.click();
      const afterNext = rangeText();
      expect(afterNext).not.toBe(initial);

      prev.click();
      expect(rangeText()).toBe(initial);

      prev.click();
      const afterPrev = rangeText();
      expect(afterPrev).not.toBe(initial);
      expect(afterPrev).not.toBe(afterNext);
    }));

    it('"Bugün" içinde bulunulan haftaya döndürür', fakeAsync(() => {
      fixture.detectChanges();
      const initial = rangeText();
      const [, today, next] = navButtons();

      next.click();
      next.click();
      expect(rangeText()).not.toBe(initial);

      today.click();
      expect(rangeText()).toBe(initial);
    }));

    it('gezinme sonrası yalnızca o haftanın olayları görünür', fakeAsync(() => {
      fixture.componentRef.setInput('slots', [createSlotInWeek(1, 3, 14, 15)]);
      fixture.detectChanges();
      tick();
      expect(fixture.nativeElement.querySelectorAll('.fc-timegrid-event').length).toBe(1);

      navButtons()[2].click();
      tick();
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelectorAll('.fc-timegrid-event').length).toBe(0);
    }));
  });

  describe('tıkla-seç ile taslak aralık (issue #176)', () => {
    /** "Şimdi" Çarşamba 12:00'ye sabitlenir; Cuma (offset 4) her zaman gelecekte, Pazartesi (0) geçmişte kalır. */
    const H = safeLocalHour(createWeekTestDate(4, 12));

    function setup(slots: AvailabilitySlot[] = []): void {
      jasmine.clock().mockDate(createWeekTestDate(2, 12));
      fixture.componentRef.setInput('editable', true);
      fixture.componentRef.setInput('slots', slots);
      render();
    }

    function render(): void {
      fixture.detectChanges();
      tick();
      fixture.detectChanges();
    }

    /** FullCalendar işaretçi olaylarını taklit etmek yerine `dateClick` handler'ı çağrılır; sonuç DOM'dan okunur. */
    function clickCell(dayOffset: number, hour: number, minute = 0): void {
      component['onDateClick']({ date: createWeekTestDate(dayOffset, hour, minute) });
      render();
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

    it('editable kapalıyken onay çubuğu yoktur ve hücre tıklaması taslak kurmaz', fakeAsync(() => {
      jasmine.clock().mockDate(createWeekTestDate(2, 12));
      fixture.detectChanges();
      tick();

      clickCell(4, H + 1);

      expect(el('.awg__draft')).toBeNull();
      expect(draftEvents().length).toBe(0);
      expect(component.draft()).toBeNull();
    }));

    it('taslak yokken yönerge metni görünür, Kaydet/Vazgeç yoktur', fakeAsync(() => {
      setup();

      expect(barText()).toBe(taTr.grid.draft.instruction);
      expect(el('.awg__draft-save')).toBeNull();
      expect(el('.awg__draft-cancel')).toBeNull();
      expect(el('.awg__draft-text')?.getAttribute('aria-live')).toBe('polite');
    }));

    it('boş hücreye tıklama 30 dk taslağı hem grid üzerinde hem onay çubuğunda gösterir', fakeAsync(() => {
      setup();

      clickCell(4, H + 1);

      expect(draftEvents().length).toBe(1);
      // Tam etiket (gün + saat): gevşek `toContain(gün numarası)` saat metniyle de eşleşebilirdi.
      const expected = formatDraftLabel({ start: createWeekTestDate(4, H + 1), end: createWeekTestDate(4, H + 1, 30) });
      expect(el('.awg__draft-range')?.textContent?.trim()).toBe(expected);
      expect(expected).toContain(`${hhmm(H + 1)} – ${hhmm(H + 1, 30)}`);
      expect(barText()).not.toContain(taTr.grid.draft.instruction);
      expect(el('.awg__draft-save')?.textContent?.trim()).toBe(taTr.grid.draft.save);
      expect(el('.awg__draft-cancel')?.textContent?.trim()).toBe(taTr.grid.draft.cancel);
    }));

    it('art arda tıklama taslağı genişletir; grid üzerinde tek taslak olayı kalır', fakeAsync(() => {
      setup();

      clickCell(4, H + 1);
      clickCell(4, H + 2);

      expect(barText()).toContain(`${hhmm(H + 1)} – ${hhmm(H + 2, 30)}`);
      expect(draftEvents().length).toBe(1);
      expect(component.draft()).toEqual({ start: createWeekTestDate(4, H + 1), end: createWeekTestDate(4, H + 2, 30) });
    }));

    it('taslak olayı tab durağı eklemez ve slot olaylarının erişilebilirliğini bozmaz', fakeAsync(() => {
      setup([createSlotInWeek(1, 3, 14, 15)]);

      clickCell(4, H + 1);

      expect(draftEvents().length).toBe(1);
      expect(draftEvents()[0].querySelector('[tabindex]')).toBeNull();
      expect(draftEvents()[0].querySelector('.awg__ev')).toBeNull();
      expect(draftEvents()[0].textContent?.trim()).toBe(taTr.grid.draft.tag);
      expect(draftEvents()[0].querySelector('.awg__draft-tag')?.getAttribute('aria-hidden')).toBe('true');
      const wrappers = fixture.nativeElement.querySelectorAll('.awg__ev') as NodeListOf<HTMLElement>;
      expect(wrappers.length).toBe(1);
      expect(wrappers[0].getAttribute('tabindex')).toBe('0');
      expect(wrappers[0].getAttribute('aria-label')).toContain('14:00');
    }));

    it('Vazgeç taslağı hem grid üzerinden hem çubuktan temizler', fakeAsync(() => {
      setup();
      clickCell(4, H + 1);

      el<HTMLButtonElement>('.awg__draft-cancel')!.click();
      render();

      expect(draftEvents().length).toBe(0);
      expect(component.draft()).toBeNull();
      expect(barText()).toBe(taTr.grid.draft.instruction);
      expect(el('.awg__draft-save')).toBeNull();
    }));

    it('Kaydet, taslak anlarının UTC gün + saat isteğini yayar ve taslağı kendisi temizlemez', fakeAsync(() => {
      setup();
      const emitted: CreateAvailabilitySlotRequest[] = [];
      component.createRequested.subscribe((req) => emitted.push(req));
      clickCell(4, H + 1);
      clickCell(4, H + 2);

      el<HTMLButtonElement>('.awg__draft-save')!.click();
      render();

      // Yerel saat string'i DEĞİL: tıklanan anların UTC günü/saati.
      expect(emitted).toEqual([utcRequest(createWeekTestDate(4, H + 1), createWeekTestDate(4, H + 2, 30))]);
      // Temizleme sayfanın işidir (başarıda); hata olursa taslak düzeltilebilsin.
      expect(draftEvents().length).toBe(1);
    }));

    it('saving iken butonlar aria-disabled, Kaydet yaymaz, hücre tıklaması ve Escape taslağı değiştirmez', fakeAsync(() => {
      setup();
      const emitted: CreateAvailabilitySlotRequest[] = [];
      component.createRequested.subscribe((req) => emitted.push(req));
      clickCell(4, H + 1);

      fixture.componentRef.setInput('saving', true);
      render();

      const save = el<HTMLButtonElement>('.awg__draft-save')!;
      const cancel = el<HTMLButtonElement>('.awg__draft-cancel')!;
      expect(save.getAttribute('aria-disabled')).toBe('true');
      expect(cancel.getAttribute('aria-disabled')).toBe('true');
      expect(save.textContent?.trim()).toBe(taTr.grid.draft.saving);

      // `disabledInteractive` buton tıklamayı almaya devam eder; engelleme komponentin kendi korumasındadır.
      save.click();
      cancel.click();
      clickCell(4, H + 3);
      el('.awg__scroll')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
      render();

      expect(emitted).toEqual([]);
      expect(barText()).toContain(`${hhmm(H + 1)} – ${hhmm(H + 1, 30)}`);
      expect(draftEvents().length).toBe(1);
    }));

    it('saveError onay çubuğunda role="alert" ile gösterilir ve taslak yerinde kalır', fakeAsync(() => {
      setup();
      clickCell(4, H + 1);

      fixture.componentRef.setInput('saveError', 'Bu aralık mevcut bir aralıkla çakışıyor.');
      render();

      const alert = el('.awg__draft [role="alert"]');
      expect(alert?.textContent?.trim()).toBe('Bu aralık mevcut bir aralıkla çakışıyor.');
      expect(draftEvents().length).toBe(1);
      expect(barText()).toContain(`${hhmm(H + 1)} – ${hhmm(H + 1, 30)}`);
    }));

    it('hata yokken alert elemanı render edilmez', fakeAsync(() => {
      setup();
      clickCell(4, H + 1);

      expect(el('.awg__draft [role="alert"]')).toBeNull();
    }));

    it('geçmiş hücre taslak kurmaz ve ipucu gösterir; geçerli tıklama ipucunu siler', fakeAsync(() => {
      setup();

      clickCell(0, 10);

      expect(draftEvents().length).toBe(0);
      expect(barText()).toContain(taTr.grid.hint.past);

      clickCell(4, H + 1);

      expect(barText()).not.toContain(taTr.grid.hint.past);
      expect(draftEvents().length).toBe(1);
    }));

    it('mevcut slotun üzerinden genişletme reddedilir: ipucu görünür, taslak aynı kalır', fakeAsync(() => {
      setup([createSlotInWeek(1, 4, H + 1, H + 2)]);
      clickCell(4, H);

      clickCell(4, H + 3);

      expect(barText()).toContain(`${hhmm(H)} – ${hhmm(H, 30)}`);
      expect(barText()).toContain(taTr.grid.hint.occupied);
    }));

    it('yerel 23:30 hücresi (bitiş = ertesi yerel gün 00:00) UTC gün sınırını aşmıyorsa taslak olarak çizilir', fakeAsync(() => {
      setup();
      const end = createWeekTestDate(5, 0);
      const endsAtUtcMidnight = end.getTime() % (24 * 60 * 60_000) === 0;

      clickCell(4, 23, 30);

      if (endsAtUtcMidnight) {
        // Yalnızca yerel gece yarısı = UTC gece yarısı olan dilimde (UTC) reddedilir.
        expect(draftEvents().length).toBe(0);
        expect(barText()).toContain(taTr.grid.hint.crossesDayBoundary);
      } else {
        // Tek sütunda tek arka plan olayı; ertesi güne taşan ikinci bir parça yok.
        expect(draftEvents().length).toBe(1);
        const expected = formatDraftLabel({ start: createWeekTestDate(4, 23, 30), end });
        expect(el('.awg__draft-range')?.textContent?.trim()).toBe(expected);
        expect(expected).toContain(`${hhmm(23, 30)} – ${hhmm(0)}`);
        expect(component.draft()).toEqual({ start: createWeekTestDate(4, 23, 30), end });
      }
    }));

    describe('dateClick bağlantısı', () => {
      it('takvim seçenekleri interaction eklentisini ve dateClick handler\'ını içerir', fakeAsync(() => {
        setup();

        const options = component['options']();

        expect(options.plugins).toContain(interactionPlugin);
        expect(typeof options.dateClick).toBe('function');
      }));

      it('seçeneklerdeki dateClick çağrısı taslağı kurar (handler komponentin mantığına bağlı)', fakeAsync(() => {
        setup();
        const start = createWeekTestDate(4, H + 1);

        component['options']().dateClick!({ date: start } as DateClickArg);
        render();

        expect(component.draft()).toEqual({ start, end: createWeekTestDate(4, H + 1, 30) });
        expect(draftEvents().length).toBe(1);
      }));

      it('dateClick handler\'ı taslak değişince yeniden kurulmaz', fakeAsync(() => {
        setup();
        const before = component['options']().dateClick;

        clickCell(4, H + 1);

        expect(component['options']().dateClick).toBe(before);
      }));

      it('editable kapalıyken handler bağlıdır ama taslak kurmaz', fakeAsync(() => {
        jasmine.clock().mockDate(createWeekTestDate(2, 12));
        render();

        component['options']().dateClick!({ date: createWeekTestDate(4, H + 1) } as DateClickArg);
        render();

        expect(component.draft()).toBeNull();
        expect(draftEvents().length).toBe(0);
      }));

      // Duman testi: handler'ı çağırmak yerine render edilmiş hücreye GERÇEK fare olayları gönderilir;
      // `dateClick` satırı ya da interaction eklentisi silinirse bu test düşer. fakeAsync kullanılmaz
      // (FullCalendar işaretçi takibi `document` düzeyinde gerçek olaylarla çalışır) ve `whenStable` beklenmez
      // (`nowIndicator` zamanlayıcısı zone'u hiç boşaltmaz); gerçek makro görev beklenir.
      // Tarihe bağımlı olmamak için bir sonraki haftaya geçilir — oradaki her hücre gelecektedir.
      it('duman: render edilmiş boş hücreye mousedown/mouseup taslağı kurar', async () => {
        const settle = () => new Promise<void>((resolve) => setTimeout(resolve, 20));
        fixture.componentRef.setInput('editable', true);
        fixture.detectChanges();
        await settle();
        (fixture.nativeElement.querySelectorAll('.awg__nav button')[2] as HTMLButtonElement).click();
        fixture.detectChanges();
        await settle();
        fixture.detectChanges();

        const start = createWeekTestDate(7 + 2, H + 1); // gelecek haftanın Çarşambası
        const pad = (n: number) => `${n}`.padStart(2, '0');
        const localDate = `${start.getFullYear()}-${pad(start.getMonth() + 1)}-${pad(start.getDate())}`;
        const column = el(`td.fc-timegrid-col[data-date="${localDate}"]`);
        const lane = el(`td.fc-timegrid-slot-lane[data-time="${hhmm(H + 1)}:00"]`);
        expect(column).withContext('gün sütunu render edilmeli').not.toBeNull();
        expect(lane).withContext('saat satırı render edilmeli').not.toBeNull();
        if (!column || !lane) {
          return;
        }

        // Sentetik MouseEvent'te `pageY` = `clientY` (pencere kaydırması eklenmez); FullCalendar ise hit'i sayfa
        // koordinatıyla arayıp `elementFromPoint` ile doğrular. Pencere 0'da tutulur, hücre iç kaydırıcıyla getirilir.
        const scroller = lane.closest('.fc-scroller') as HTMLElement;
        scroller.scrollTop = lane.offsetTop;
        window.scrollTo(0, 0);
        const colRect = column.getBoundingClientRect();
        const laneRect = lane.getBoundingClientRect();
        const clientX = colRect.left + colRect.width / 2;
        const clientY = laneRect.top + laneRect.height / 2;
        expect(clientY).withContext('hücre görünür alanda olmalı').toBeLessThan(window.innerHeight);
        const target = document.elementFromPoint(clientX, clientY) as HTMLElement | null;
        expect(target && fixture.nativeElement.contains(target)).withContext('hedef nokta grid içinde olmalı').toBeTrue();
        if (!target) {
          return;
        }

        const init: MouseEventInit = { bubbles: true, cancelable: true, button: 0, clientX, clientY };
        target.dispatchEvent(new MouseEvent('mousedown', init));
        target.dispatchEvent(new MouseEvent('mouseup', init));
        await settle();
        fixture.detectChanges();

        expect(component.draft()).toEqual({ start, end: createWeekTestDate(7 + 2, H + 1, 30) });
        expect(draftEvents().length).toBe(1);
      });
    });

    describe('Escape (yalnızca grid içinden)', () => {
      function pressEscape(target: EventTarget, prevented = false): void {
        const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
        if (prevented) {
          event.preventDefault();
        }
        target.dispatchEvent(event);
        render();
      }

      it('grid içinden gelen Escape taslağı iptal eder', fakeAsync(() => {
        setup();
        clickCell(4, H + 1);

        pressEscape(el('.awg__scroll')!);

        expect(draftEvents().length).toBe(0);
        expect(barText()).toBe(taTr.grid.draft.instruction);
      }));

      it('grid dışından (document/body) gelen Escape taslağı SİLMEZ', fakeAsync(() => {
        setup();
        clickCell(4, H + 1);

        pressEscape(document);
        pressEscape(document.body);

        expect(draftEvents().length).toBe(1);
        expect(component.draft()).not.toBeNull();
      }));

      it('başka bir katmanın tükettiği (defaultPrevented) Escape taslağı silmez', fakeAsync(() => {
        setup();
        clickCell(4, H + 1);

        pressEscape(el('.awg__scroll')!, true);

        expect(draftEvents().length).toBe(1);
      }));

      it('editable kapalıyken Escape dışarıdan verilmiş taslağa dokunmaz', fakeAsync(() => {
        jasmine.clock().mockDate(createWeekTestDate(2, 12));
        const draft = { start: createWeekTestDate(4, H + 1), end: createWeekTestDate(4, H + 1, 30) };
        fixture.componentRef.setInput('draft', draft);
        render();

        pressEscape(el('.awg__scroll')!);

        expect(component.draft()).toEqual(draft);
      }));

      it('hücre tıklaması odağı grid içine (kaydırma bölgesine) alır; böylece Escape çalışır', fakeAsync(() => {
        setup();
        (document.activeElement as HTMLElement | null)?.blur();

        clickCell(4, H + 1);

        expect(document.activeElement).toBe(el('.awg__scroll'));
      }));
    });

    describe('odak yönetimi', () => {
      it('saving iken Kaydet odakta kalır ve aria-disabled olur (native disabled değil)', fakeAsync(() => {
        setup();
        clickCell(4, H + 1);
        const save = el<HTMLButtonElement>('.awg__draft-save')!;
        save.focus();

        fixture.componentRef.setInput('saving', true);
        render();

        expect(document.activeElement).toBe(save);
        expect(save.getAttribute('aria-disabled')).toBe('true');
        expect(save.disabled).toBeFalse();
        expect(el('.awg__draft-cancel')?.getAttribute('aria-disabled')).toBe('true');
      }));

      it('Vazgeç sonrası odak kaydırma bölgesine taşınır', fakeAsync(() => {
        setup();
        clickCell(4, H + 1);
        const cancel = el<HTMLButtonElement>('.awg__draft-cancel')!;
        cancel.focus();

        cancel.click();
        render();

        expect(document.activeElement).toBe(el('.awg__scroll'));
      }));

      it('kayıt başarısında (sayfa draft=null yazar) odak Kaydet\'ten kaydırma bölgesine taşınır', fakeAsync(() => {
        setup();
        clickCell(4, H + 1);
        el<HTMLButtonElement>('.awg__draft-save')!.focus();
        fixture.componentRef.setInput('saving', true);
        render();

        fixture.componentRef.setInput('saving', false);
        fixture.componentRef.setInput('draft', null);
        render();

        expect(el('.awg__draft-save')).toBeNull();
        expect(document.activeElement).toBe(el('.awg__scroll'));
      }));

      it('odak grid dışındayken taslak kalkarsa odak çalınmaz', fakeAsync(() => {
        setup();
        clickCell(4, H + 1);
        const outside = document.createElement('button');
        document.body.appendChild(outside);
        outside.focus();

        fixture.componentRef.setInput('draft', null);
        render();

        expect(document.activeElement).toBe(outside);
        outside.remove();
      }));
    });

    it('reddedilen tıklamanın ipucu, taslak kalkınca (kayıt başarısı) ekranda kalmaz', fakeAsync(() => {
      setup();
      clickCell(4, H);
      clickCell(4, H + 4);
      expect(barText()).toContain(taTr.grid.hint.tooLong.replace('{{hours}}', '4'));

      fixture.componentRef.setInput('draft', null);
      render();

      expect(barText()).toBe(taTr.grid.draft.instruction);
    }));

    it('gün sınırı ipucu sınırın YEREL saatini gösterir (sabit metin değil)', fakeAsync(() => {
      setup();
      const boundary = nextUtcDayBoundary(createWeekTestDate(4, 12));
      const boundaryTime = new Intl.DateTimeFormat(activeIntlLocale(), { hour: '2-digit', minute: '2-digit' }).format(
        boundary
      );

      // Bitişi tam sınıra denk gelen hücre (hizasız dilimlerde de geçerli olsun diye handler doğrudan çağrılır).
      component['onDateClick']({ date: new Date(boundary.getTime() - 30 * 60_000) });
      render();

      expect(draftEvents().length).toBe(0);
      expect(barText()).toContain(taTr.grid.hint.crossesDayBoundary.split('{{time}}').join(boundaryTime));
      expect(barText()).not.toContain('{{');
    }));

    it('4 saati aşan genişletmede sınır değeriyle ipucu gösterir', fakeAsync(() => {
      setup();
      clickCell(4, H);

      clickCell(4, H + 4);

      expect(barText()).toContain(`${hhmm(H)} – ${hhmm(H, 30)}`);
      expect(barText()).toContain(taTr.grid.hint.tooLong.replace('{{hours}}', '4'));
    }));
  });

  describe('grid üzerinden silme (issue #177)', () => {
    const H = safeLocalHour(createWeekTestDate(4, 12));
    /** Cuma H:00–(H+1):00, boş. */
    const free = () => createSlotInWeek(11, 4, H, H + 1);
    /** Cuma (H+2):00–(H+3):00, bekleyen randevulu. */
    const booked = () => createSlotInWeek(12, 4, H + 2, H + 3, true, 'Pending');

    function setup(slots: AvailabilitySlot[] = [free(), booked()], editable = true): void {
      jasmine.clock().mockDate(createWeekTestDate(2, 12));
      fixture.componentRef.setInput('editable', editable);
      fixture.componentRef.setInput('slots', slots);
      render();
    }

    function render(): void {
      fixture.detectChanges();
      tick();
      fixture.detectChanges();
    }

    function el<T extends HTMLElement = HTMLElement>(selector: string): T | null {
      return fixture.nativeElement.querySelector(selector) as T | null;
    }

    /** FullCalendar `eventClick` handler'ı seçeneklerden çağrılır (fare/dokunma yolu); DOM'dan doğrulanır. */
    function clickEvent(slotId: number): void {
      component['options']().eventClick!({ event: { id: String(slotId) } } as EventClickArg);
      render();
    }

    function clickCell(dayOffset: number, hour: number): void {
      component['onDateClick']({ date: createWeekTestDate(dayOffset, hour) });
      render();
    }

    /** Slotun `.awg__ev` sarmalayıcısı (`data-slot-id` ile). */
    function wrapperOf(slotId: number): HTMLElement {
      const hit = el(`.awg__ev[data-slot-id="${slotId}"]`);
      expect(hit).withContext(`slot ${slotId} render edilmeli`).not.toBeNull();
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

    it('takvim seçenekleri eventClick handler\'ı içerir ve eventInteractive AÇIKÇA kapalıdır', fakeAsync(() => {
      setup();

      const options = component['options']();
      expect(typeof options.eventClick).toBe('function');
      // `eventClick` bağlıyken FullCalendar `eventInteractive`'i varsayılan açar; `.fc-event` tabindex almamalı.
      expect(options.eventInteractive).toBeFalse();
      const fcEvents = fixture.nativeElement.querySelectorAll('.fc-event') as NodeListOf<HTMLElement>;
      expect(fcEvents.length).toBe(2);
      for (const event of Array.from(fcEvents)) {
        expect(event.hasAttribute('tabindex')).toBeFalse();
      }
    }));

    it('boş slota tıklama silme modunu açar: çubukta aralık + soru + Sil/Vazgeç, olay is-selected', fakeAsync(() => {
      setup();

      clickEvent(11);

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
    }));

    it('randevulu slota tıklama silme moduna girmez; lockedHint ipucu görünür ve süre sonunda kalkar', fakeAsync(() => {
      setup();

      clickEvent(12);

      expect(component.selectedSlotId()).toBeNull();
      expect(selectedEvents().length).toBe(0);
      expect(el('.awg__delete-confirm')).toBeNull();
      expect(el('.awg__locked-hint')?.textContent?.trim()).toBe(taTr.actions.lockedHint);

      tick(4000);
      fixture.detectChanges();

      expect(el('.awg__locked-hint')).toBeNull();
    }));

    it('editable kapalıyken olay tıklaması seçim kurmaz ve sarmalayıcı role="group" kalır', fakeAsync(() => {
      setup([free()], false);

      clickEvent(11);

      expect(component.selectedSlotId()).toBeNull();
      expect(el('.awg__draft')).toBeNull();
      expect(el('.awg__ev')?.getAttribute('role')).toBe('group');
      expect(el('.awg__ev')?.hasAttribute('aria-description')).toBeFalse();
    }));

    it('editable iken sarmalayıcı role="button"; boş slot aria-description/tooltip ile silme ipucu taşır, randevulu taşımaz', fakeAsync(() => {
      setup();

      const freeWrapper = wrapperOf(11);
      const bookedWrapper = wrapperOf(12);
      expect(freeWrapper.getAttribute('role')).toBe('button');
      expect(freeWrapper.getAttribute('aria-description')).toBe(taTr.grid.delete.hint);
      // aria-label değişmez (gün · saat · durum); eylem ipucu ayrı özniteliktedir.
      expect(freeWrapper.getAttribute('aria-label')).not.toContain(taTr.grid.delete.hint);
      expect(freeWrapper.getAttribute('aria-pressed')).toBe('false');
      expect(freeWrapper.hasAttribute('aria-disabled')).toBeFalse();
      // Randevulu slot: rolü button ama eylemsiz → aria-disabled + "silinemez" ipucu.
      expect(bookedWrapper.getAttribute('role')).toBe('button');
      expect(bookedWrapper.getAttribute('aria-disabled')).toBe('true');
      expect(bookedWrapper.getAttribute('aria-description')).toBe(taTr.actions.lockedHint);
      expect(bookedWrapper.hasAttribute('aria-pressed')).toBeFalse();
      expect(fixture.nativeElement.querySelectorAll('.awg__ev[tabindex="0"]').length).toBe(2);
    }));

    it('editable kapalıyken aria-disabled ve aria-description hiçbir slotta yoktur', fakeAsync(() => {
      setup([free(), booked()], false);

      for (const w of Array.from(fixture.nativeElement.querySelectorAll('.awg__ev') as NodeListOf<HTMLElement>)) {
        expect(w.hasAttribute('aria-disabled')).toBeFalse();
        expect(w.hasAttribute('aria-description')).toBeFalse();
      }
    }));

    it('randevulu slota tıklama bayat taslak ipucunu siler; iki ipucu aynı anda görünmez', fakeAsync(() => {
      setup();
      clickCell(0, 10);
      expect(barText()).toContain(taTr.grid.hint.past);

      clickEvent(12);

      expect(barText()).not.toContain(taTr.grid.hint.past);
      expect(el('.awg__locked-hint')).not.toBeNull();
      expect(fixture.nativeElement.querySelectorAll('.awg__draft-hint').length).toBe(1);
    }));

    it('klavye: odaktaki boş slotta Enter seçer, Space seçimi kaldırır; varsayılan engellenir', fakeAsync(() => {
      setup();
      const wrapper = wrapperOf(11);

      const enter = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });
      wrapper.dispatchEvent(enter);
      render();
      expect(enter.defaultPrevented).toBeTrue();
      expect(component.selectedSlotId()).toBe(11);
      expect(el('.awg__delete-confirm')).not.toBeNull();

      const space = new KeyboardEvent('keydown', { key: ' ', bubbles: true, cancelable: true });
      wrapperOf(11).dispatchEvent(space);
      render();
      expect(space.defaultPrevented).toBeTrue();
      expect(component.selectedSlotId()).toBeNull();
      expect(el('.awg__delete-confirm')).toBeNull();
    }));

    it('klavye: randevulu slotta Enter seçim kurmaz, ipucu gösterir', fakeAsync(() => {
      setup();

      wrapperOf(12).dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
      render();

      expect(component.selectedSlotId()).toBeNull();
      expect(el('.awg__locked-hint')).not.toBeNull();
    }));

    it('Sil, seçili slotun kimliğini yayar ve seçimi kendisi temizlemez', fakeAsync(() => {
      setup();
      const emitted: number[] = [];
      component.deleteRequested.subscribe((id) => emitted.push(id));
      clickEvent(11);

      el<HTMLButtonElement>('.awg__delete-confirm')!.click();
      render();

      expect(emitted).toEqual([11]);
      expect(component.selectedSlotId()).toBe(11);
      expect(selectedEvents().length).toBe(1);
    }));

    it('Vazgeç seçimi ve vurguyu kaldırır; yönerge metnine dönülür', fakeAsync(() => {
      setup();
      clickEvent(11);

      el<HTMLButtonElement>('.awg__delete-cancel')!.click();
      render();

      expect(component.selectedSlotId()).toBeNull();
      expect(selectedEvents().length).toBe(0);
      expect(el('.awg__delete-confirm')).toBeNull();
      expect(barText()).toBe(taTr.grid.draft.instruction);
    }));

    it('grid içinden Escape seçimi kaldırır; dışarıdan gelen Escape kaldırmaz', fakeAsync(() => {
      setup();
      clickEvent(11);

      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
      render();
      expect(component.selectedSlotId()).toBe(11);

      el('.awg__scroll')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
      render();
      expect(component.selectedSlotId()).toBeNull();
      expect(selectedEvents().length).toBe(0);
    }));

    it('deleting iken butonlar aria-disabled (odak kaybolmaz), Sil yaymaz, tıklama ve Escape seçimi değiştirmez', fakeAsync(() => {
      setup();
      const emitted: number[] = [];
      component.deleteRequested.subscribe((id) => emitted.push(id));
      clickEvent(11);
      const confirmBtn = el<HTMLButtonElement>('.awg__delete-confirm')!;
      confirmBtn.focus();

      fixture.componentRef.setInput('deleting', true);
      render();

      expect(confirmBtn.getAttribute('aria-disabled')).toBe('true');
      expect(confirmBtn.disabled).toBeFalse();
      expect(confirmBtn.textContent?.trim()).toBe(taTr.grid.delete.deleting);
      expect(el('.awg__delete-cancel')?.getAttribute('aria-disabled')).toBe('true');
      expect(document.activeElement).toBe(confirmBtn);

      confirmBtn.click();
      el<HTMLButtonElement>('.awg__delete-cancel')!.click();
      clickEvent(12);
      clickCell(4, H + 4);
      el('.awg__scroll')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
      render();

      expect(emitted).toEqual([]);
      expect(component.selectedSlotId()).toBe(11);
      expect(component.draft()).toBeNull();
      expect(el('.awg__locked-hint')).toBeNull();
    }));

    it('deleteError çubukta role="alert" ile gösterilir ve seçim korunur', fakeAsync(() => {
      setup();
      clickEvent(11);
      expect(el('.awg__draft [role="alert"]')).toBeNull();

      fixture.componentRef.setInput('deleteError', 'Aktif randevusu olan aralık silinemez.');
      render();

      expect(el('.awg__draft [role="alert"]')?.textContent?.trim()).toBe('Aktif randevusu olan aralık silinemez.');
      expect(component.selectedSlotId()).toBe(11);
      expect(selectedEvents().length).toBe(1);
    }));

    it('sayfa slotu listeden düşürüp seçimi sıfırlayınca çubuk kapanır ve odak kaydırma bölgesine geçer', fakeAsync(() => {
      setup();
      clickEvent(11);
      el<HTMLButtonElement>('.awg__delete-confirm')!.focus();

      fixture.componentRef.setInput('slots', [booked()]);
      fixture.componentRef.setInput('selectedSlotId', null);
      render();

      expect(el('.awg__delete-confirm')).toBeNull();
      expect(fixture.nativeElement.querySelectorAll('.fc-timegrid-event').length).toBe(1);
      expect(barText()).toBe(taTr.grid.draft.instruction);
      expect(document.activeElement).toBe(el('.awg__scroll'));
    }));

    it('seçili kimlik slots\'tan düşerse silme modu kapanır ve model null\'a çekilip selectedSlotIdChange yayılır', fakeAsync(() => {
      setup();
      const emitted: (number | null)[] = [];
      component.selectedSlotId.subscribe((id) => emitted.push(id));
      clickEvent(11);
      expect(emitted).toEqual([11]);

      fixture.componentRef.setInput('slots', [booked()]);
      render();

      expect(el('.awg__delete-confirm')).toBeNull();
      expect(selectedEvents().length).toBe(0);
      expect(component.selectedSlotId()).toBeNull();
      expect(emitted).toEqual([11, null]);
    }));

    it('taslak varken slota tıklama taslağı siler; seçim varken hücreye tıklama seçimi siler', fakeAsync(() => {
      setup();
      clickCell(4, H + 4);
      expect(draftEvents().length).toBe(1);

      clickEvent(11);

      expect(component.draft()).toBeNull();
      expect(draftEvents().length).toBe(0);
      expect(component.selectedSlotId()).toBe(11);
      expect(el('.awg__draft-save')).toBeNull();
      expect(el('.awg__delete-confirm')).not.toBeNull();

      clickCell(4, H + 4);

      expect(component.selectedSlotId()).toBeNull();
      expect(selectedEvents().length).toBe(0);
      expect(component.draft()).not.toBeNull();
      expect(el('.awg__delete-confirm')).toBeNull();
      expect(el('.awg__draft-save')).not.toBeNull();
    }));

    it('taslak reddi ipucu, slot seçilince ekranda kalmaz', fakeAsync(() => {
      setup();
      clickCell(0, 10);
      expect(barText()).toContain(taTr.grid.hint.past);

      clickEvent(11);

      expect(barText()).not.toContain(taTr.grid.hint.past);
    }));

    it('taslak (arka plan) olayına gelen eventClick yok sayılır', fakeAsync(() => {
      setup();
      clickCell(4, H + 4);

      component['options']().eventClick!({ event: { id: 'awg-draft' } } as EventClickArg);
      render();

      expect(component.selectedSlotId()).toBeNull();
      expect(component.draft()).not.toBeNull();
    }));

    // Duman testi: handler yerine render edilmiş `.fc-timegrid-event` üzerine GERÇEK click gönderilir;
    // FullCalendar `EventClicking` (document düzeyinde `.fc-event` seçicili delege click) `eventInteractive`
    // kapalıyken de `eventClick`'i tetiklemelidir. fakeAsync kullanılmaz (bkz. #176 duman testi notu).
    it('duman: render edilmiş slot olayına gerçek click silme modunu açar', async () => {
      const settle = () => new Promise<void>((resolve) => setTimeout(resolve, 20));
      fixture.componentRef.setInput('editable', true);
      fixture.componentRef.setInput('slots', [createSlotInWeek(11, 3, 14, 15)]);
      fixture.detectChanges();
      await settle();
      fixture.detectChanges();

      const target = el('.fc-timegrid-event .awg__ev');
      expect(target).withContext('slot olayı render edilmeli').not.toBeNull();
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

    function setup(slots: AvailabilitySlot[] = [single(), recurring()], editable = true): void {
      jasmine.clock().mockDate(createWeekTestDate(2, 12));
      fixture.componentRef.setInput('editable', editable);
      fixture.componentRef.setInput('slots', slots);
      render();
    }

    function render(): void {
      fixture.detectChanges();
      tick();
      fixture.detectChanges();
    }

    function el<T extends HTMLElement = HTMLElement>(selector: string): T | null {
      return fixture.nativeElement.querySelector(selector) as T | null;
    }

    function all(selector: string): HTMLElement[] {
      return Array.from(fixture.nativeElement.querySelectorAll(selector) as NodeListOf<HTMLElement>);
    }

    function clickEvent(slotId: number): void {
      component['options']().eventClick!({ event: { id: String(slotId) } } as EventClickArg);
      render();
    }

    function clickCell(dayOffset: number, hour: number, minute = 0): void {
      component['onDateClick']({ date: createWeekTestDate(dayOffset, hour, minute) });
      render();
    }

    function wrapperOf(slotId: number): HTMLElement {
      const hit = el(`.awg__ev[data-slot-id="${slotId}"]`);
      expect(hit).withContext(`slot ${slotId} render edilmeli`).not.toBeNull();
      return hit!;
    }

    /** Onay çubuğundaki "Her hafta tekrarla" kutusunun native input'u. */
    function toggleInput(): HTMLInputElement | null {
      return el<HTMLInputElement>('.awg__repeat-toggle input[type="checkbox"]');
    }

    function checkRepeat(): void {
      const input = toggleInput();
      expect(input).withContext('tekrar kutusu render edilmeli').not.toBeNull();
      input!.click();
      render();
    }

    /** Bitiş gününü datepicker seçimi gibi reactive kontrole yazar (validator'lar çalışır). */
    function setUntil(value: Date | null): void {
      component['untilControl'].setValue(value);
      render();
    }

    /** NativeDateAdapter.toIso8601 karşılığı: anın UTC günü ("2026-10-01"). */
    function isoDay(d: Date): string {
      return d.toISOString().slice(0, 10);
    }

    // ---- Görsel ayrışma ----

    it('kuraldan üretilen slot is-recurring sınıfı, tekrar ikonu ve aria-label\'da "tekrarlayan" parçası taşır; tekil taşımaz', fakeAsync(() => {
      setup([single(), recurring()], false);

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
    }));

    it('tekrar ikonu tab durağı eklemez: olay başına tek tabindex, .fc-event\'te yok', fakeAsync(() => {
      setup([recurring()], false);

      expect(all('[tabindex]').filter((e) => e.closest('.fc-timegrid-event')).length).toBe(1);
      expect(el('.fc-event')?.hasAttribute('tabindex')).toBeFalse();
    }));

    it('randevulu tekrarlayan slotta aria-label sıra: gün · saat · durum · öğrenci · tekrarlayan; aria-description "silinemez"', fakeAsync(() => {
      setup([{ ...recurringBooked(), studentName: 'Ayşe' }]);

      const label = wrapperOf(22).getAttribute('aria-label') ?? '';
      const student = taTr.grid.tooltip.student.replace('{{name}}', 'Ayşe');
      expect(label.indexOf(taTr.status.pending)).toBeLessThan(label.indexOf(student));
      expect(label.indexOf(student)).toBeLessThan(label.indexOf(taTr.grid.recurring.tooltip));
      expect(wrapperOf(22).getAttribute('aria-description')).toBe(taTr.actions.lockedHint);
    }));

    // ---- Oluşturma ----

    it('taslak yokken tekrar kutusu yoktur; taslak kurulunca işaretsiz gelir ve tarih seçici gizlidir', fakeAsync(() => {
      setup([]);
      expect(toggleInput()).toBeNull();

      clickCell(4, H + 4);

      expect(toggleInput()).not.toBeNull();
      expect(toggleInput()!.checked).toBeFalse();
      expect(el('.awg__repeat-until')).toBeNull();
      expect(el('.awg__draft-save')?.textContent?.trim()).toBe(taTr.grid.draft.save);
    }));

    it('kutu işaretsizken Kaydet tekil isteği yayar, kural isteği yaymaz', fakeAsync(() => {
      setup([]);
      const single: CreateAvailabilitySlotRequest[] = [];
      const rules: CreateRecurringRuleRequest[] = [];
      component.createRequested.subscribe((r) => single.push(r));
      component.createRecurringRequested.subscribe((r) => rules.push(r));
      clickCell(4, H + 4);

      el<HTMLButtonElement>('.awg__draft-save')!.click();
      render();

      expect(single).toEqual([utcRequest(createWeekTestDate(4, H + 4), createWeekTestDate(4, H + 4, 30))]);
      expect(rules).toEqual([]);
    }));

    it('kutu işaretliyken Kaydet kural isteğini yayar: UTC gün/saat, effectiveFrom = taslağın UTC günü, effectiveUntil null; taslak korunur', fakeAsync(() => {
      setup([]);
      const single: CreateAvailabilitySlotRequest[] = [];
      const rules: CreateRecurringRuleRequest[] = [];
      component.createRequested.subscribe((r) => single.push(r));
      component.createRecurringRequested.subscribe((r) => rules.push(r));
      clickCell(4, H + 4);
      clickCell(4, H + 5);
      checkRepeat();

      expect(el('.awg__repeat-until')).not.toBeNull();
      expect(el('.awg__draft-save')?.textContent?.trim()).toBe(taTr.grid.recurring.save);

      el<HTMLButtonElement>('.awg__draft-save')!.click();
      render();

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
    }));

    it('bitiş günü seçilince effectiveUntil o günün (taslak saatindeki) UTC günü olur; seçici min/max taslak günü+7 / +1 yıl-1', fakeAsync(() => {
      setup([]);
      const rules: CreateRecurringRuleRequest[] = [];
      component.createRecurringRequested.subscribe((r) => rules.push(r));
      clickCell(4, H + 4);
      checkRepeat();

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
      setUntil(until);
      expect(component['untilInvalid']()).toBeFalse();
      expect(el('.awg__repeat-error')).toBeNull();
      el<HTMLButtonElement>('.awg__draft-save')!.click();
      render();

      const untilAtDraftTime = new Date(until.getFullYear(), until.getMonth(), until.getDate(), start.getHours(), start.getMinutes());
      expect(rules.length).toBe(1);
      expect(rules[0].effectiveUntil).toBe(untilAtDraftTime.toISOString().slice(0, 10));
    }));

    it('tam +7 gün (ilk tekrar) sınırın içindedir ve effectiveUntil olarak gönderilir', fakeAsync(() => {
      setup([]);
      const rules: CreateRecurringRuleRequest[] = [];
      component.createRecurringRequested.subscribe((r) => rules.push(r));
      clickCell(4, H + 4);
      checkRepeat();
      const start = createWeekTestDate(4, H + 4);
      const until = new Date(start.getFullYear(), start.getMonth(), start.getDate() + 7);

      setUntil(until);
      expect(component['untilInvalid']()).toBeFalse();
      el<HTMLButtonElement>('.awg__draft-save')!.click();
      render();

      const untilAtDraftTime = new Date(until.getFullYear(), until.getMonth(), until.getDate(), start.getHours(), start.getMinutes());
      expect(rules.length).toBe(1);
      expect(rules[0].effectiveUntil).toBe(untilAtDraftTime.toISOString().slice(0, 10));
    }));

    it('sınır dışı bitiş günü (taslak günü+7\'den önce) SESSİZCE süresize düşmez: hata görünür, Kaydet kilitli, istek yayılmaz', fakeAsync(() => {
      setup([]);
      const rules: CreateRecurringRuleRequest[] = [];
      component.createRecurringRequested.subscribe((r) => rules.push(r));
      clickCell(4, H + 4);
      checkRepeat();
      const start = createWeekTestDate(4, H + 4);

      setUntil(new Date(start.getFullYear(), start.getMonth(), start.getDate() + 3));

      expect(component['untilInvalid']()).toBeTrue();
      expect(component['untilControl'].hasError('matDatepickerMin')).toBeTrue();
      expect(el('.awg__repeat-error')?.textContent?.trim()).toBe(taTr.grid.recurring.untilOutOfRange);
      const save = el<HTMLButtonElement>('.awg__draft-save')!;
      expect(save.getAttribute('aria-disabled')).toBe('true');
      expect(save.disabled).toBeFalse(); // disabledInteractive: odak kaybolmaz
      save.click();
      render();
      expect(rules).toEqual([]);
      expect(component.draft()).not.toBeNull();

      // Bir yıldan ileri de aynı yol (matDatepickerMax).
      setUntil(new Date(start.getFullYear() + 1, start.getMonth(), start.getDate() + 30));
      expect(component['untilControl'].hasError('matDatepickerMax')).toBeTrue();
      expect(component['untilInvalid']()).toBeTrue();

      // Geçerli tarihe dönünce kilit açılır.
      setUntil(new Date(start.getFullYear(), start.getMonth(), start.getDate() + 14));
      expect(component['untilInvalid']()).toBeFalse();
      expect(el('.awg__repeat-error')).toBeNull();
      expect(save.getAttribute('aria-disabled')).toBeNull();
    }));

    it('parse edilemeyen metin (değer null, kontrol geçersiz) de Kaydet\'i kilitler; süresiz olarak gitmez', fakeAsync(() => {
      setup([]);
      const rules: CreateRecurringRuleRequest[] = [];
      component.createRecurringRequested.subscribe((r) => rules.push(r));
      clickCell(4, H + 4);
      checkRepeat();

      const input = el<HTMLInputElement>('.awg__repeat-until input')!;
      input.value = 'abc';
      input.dispatchEvent(new Event('input', { bubbles: true }));
      render();

      expect(component['repeatUntil']()).toBeNull();
      expect(component['untilControl'].hasError('matDatepickerParse')).toBeTrue();
      expect(component['untilInvalid']()).toBeTrue();
      expect(el('.awg__repeat-error')).not.toBeNull();
      el<HTMLButtonElement>('.awg__draft-save')!.click();
      render();
      expect(rules).toEqual([]);
    }));

    it('tarih seçildikten sonra taslak başka güne taşınınca tarih min altına düşerse hata görünür ve Kaydet kilitlenir', fakeAsync(() => {
      setup([]);
      clickCell(4, H + 4); // Cuma
      checkRepeat();
      const friday = createWeekTestDate(4, H + 4);
      // Cuma+7 geçerli; Cumartesi'ye taşınınca min Cumartesi+7 olur → Cuma+7 artık min altında.
      setUntil(new Date(friday.getFullYear(), friday.getMonth(), friday.getDate() + 7));
      expect(component['untilInvalid']()).toBeFalse();

      clickCell(5, H + 4); // Cumartesi (taslak taşınır, kutu ve tarih korunur)

      expect(component.draft()?.start.getDay()).toBe(6);
      expect(toggleInput()!.checked).toBeTrue();
      expect(component['untilInvalid']()).toBeTrue();
      expect(el('.awg__repeat-error')?.textContent?.trim()).toBe(taTr.grid.recurring.untilOutOfRange);
      expect(el('.awg__draft-save')?.getAttribute('aria-disabled')).toBe('true');
    }));

    it('bitiş günü alanında Escape taslağı SİLMEZ (yazımı iptal içindir); kaydırma bölgesinde Escape siler', fakeAsync(() => {
      setup([]);
      clickCell(4, H + 4);
      checkRepeat();
      const input = el<HTMLInputElement>('.awg__repeat-until input')!;

      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
      render();

      expect(component.draft()).not.toBeNull();
      expect(toggleInput()!.checked).toBeTrue();

      el('.awg__scroll')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
      render();
      expect(component.draft()).toBeNull();
    }));

    it('kutu kaldırılınca bitiş günü sıfırlanır; taslak kalkınca (Vazgeç) kutu ve tarih sıfırlanır', fakeAsync(() => {
      setup([]);
      clickCell(4, H + 4);
      checkRepeat();
      const start = createWeekTestDate(4, H + 4);
      setUntil(new Date(start.getFullYear(), start.getMonth(), start.getDate() + 14));
      expect(component['repeatUntil']()).not.toBeNull();

      toggleInput()!.click();
      render();
      expect(component['repeatWeekly']()).toBeFalse();
      expect(component['repeatUntil']()).toBeNull();
      expect(el('.awg__repeat-until')).toBeNull();

      checkRepeat();
      el<HTMLButtonElement>('.awg__draft-cancel')!.click();
      render();
      expect(component.draft()).toBeNull();
      expect(component['repeatWeekly']()).toBeFalse();

      // Yeni taslak yine işaretsiz başlar.
      clickCell(4, H + 4);
      expect(toggleInput()!.checked).toBeFalse();
    }));

    it('sayfa kayıt başarısında draft\'ı null yapınca kutu sıfırlanır (bir sonraki taslağa sızmaz)', fakeAsync(() => {
      setup([]);
      clickCell(4, H + 4);
      checkRepeat();

      fixture.componentRef.setInput('draft', null);
      render();
      clickCell(4, H + 5);

      expect(toggleInput()!.checked).toBeFalse();
      expect(component['repeatWeekly']()).toBeFalse();
    }));

    it('saving iken kutu ve Kaydet devre dışıdır; Kaydet kural isteği yaymaz', fakeAsync(() => {
      setup([]);
      const rules: CreateRecurringRuleRequest[] = [];
      component.createRecurringRequested.subscribe((r) => rules.push(r));
      clickCell(4, H + 4);
      checkRepeat();

      fixture.componentRef.setInput('saving', true);
      render();

      expect(toggleInput()!.disabled).toBeTrue();
      expect(el('.awg__draft-save')?.getAttribute('aria-disabled')).toBe('true');
      el<HTMLButtonElement>('.awg__draft-save')!.click();
      render();
      expect(rules).toEqual([]);
      expect(component['repeatWeekly']()).toBeTrue();
    }));

    it('saveError kural modunda da çubukta role="alert" ile görünür; taslak ve kutu korunur', fakeAsync(() => {
      setup([]);
      clickCell(4, H + 4);
      checkRepeat();

      fixture.componentRef.setInput('saveError', 'Aynı gün kesişen bir kural var.');
      render();

      expect(el('.awg__draft [role="alert"]')?.textContent?.trim()).toBe('Aynı gün kesişen bir kural var.');
      expect(component.draft()).not.toBeNull();
      expect(toggleInput()!.checked).toBeTrue();
    }));

    // ---- Silme ----

    it('tekil slot seçilince tek silme butonu ("Sil") ve "Tüm seri" yoktur', fakeAsync(() => {
      setup();

      clickEvent(11);

      expect(el('.awg__delete-confirm')?.textContent?.trim()).toBe(taTr.grid.delete.confirm);
      expect(el('.awg__delete-series')).toBeNull();
      expect(el('.awg__delete-prompt')?.textContent?.trim()).toBe(taTr.grid.delete.prompt);
    }));

    it('tekrarlayan slot seçilince iki seçenek: "Sadece bu hafta" + "Tüm seri" + Vazgeç; soru metni seriye özel', fakeAsync(() => {
      setup();

      clickEvent(21);

      expect(component.selectedSlotId()).toBe(21);
      expect(el('.awg__delete-confirm')?.textContent?.trim()).toBe(taTr.grid.recurring.deleteThisWeek);
      expect(el('.awg__delete-series')?.textContent?.trim()).toContain(taTr.grid.recurring.deleteSeries);
      expect(el('.awg__delete-cancel')?.textContent?.trim()).toBe(taTr.grid.delete.cancel);
      expect(el('.awg__delete-prompt')?.textContent?.trim()).toBe(taTr.grid.recurring.deletePrompt);
      expect(all('.awg__draft-actions button').length).toBe(3);
      expect(el('.fc-timegrid-event.is-selected')?.classList).toContain('is-recurring');
    }));

    it('"Sadece bu hafta" deleteRequested(slotId) yayar, deleteSeriesRequested yaymaz; seçim korunur', fakeAsync(() => {
      setup();
      const slots: number[] = [];
      const series: number[] = [];
      component.deleteRequested.subscribe((id) => slots.push(id));
      component.deleteSeriesRequested.subscribe((id) => series.push(id));
      clickEvent(21);

      el<HTMLButtonElement>('.awg__delete-confirm')!.click();
      render();

      expect(slots).toEqual([21]);
      expect(series).toEqual([]);
      expect(component.selectedSlotId()).toBe(21);
    }));

    it('"Tüm seri" deleteSeriesRequested(ruleId) yayar, deleteRequested yaymaz; seçim korunur', fakeAsync(() => {
      setup();
      const slots: number[] = [];
      const series: number[] = [];
      component.deleteRequested.subscribe((id) => slots.push(id));
      component.deleteSeriesRequested.subscribe((id) => series.push(id));
      clickEvent(21);

      el<HTMLButtonElement>('.awg__delete-series')!.click();
      render();

      expect(series).toEqual([5]);
      expect(slots).toEqual([]);
      expect(component.selectedSlotId()).toBe(21);
    }));

    it('deleting iken "Tüm seri" aria-disabled (odak kalır) ve yaymaz', fakeAsync(() => {
      setup();
      const series: number[] = [];
      component.deleteSeriesRequested.subscribe((id) => series.push(id));
      clickEvent(21);
      const btn = el<HTMLButtonElement>('.awg__delete-series')!;
      btn.focus();

      fixture.componentRef.setInput('deleting', true);
      render();

      expect(btn.getAttribute('aria-disabled')).toBe('true');
      expect(btn.disabled).toBeFalse();
      expect(document.activeElement).toBe(btn);
      // Her iki silme butonu da "Siliniyor..." gösterir (hangisine basıldığından bağımsız tek `deleting` durumu).
      expect(btn.textContent?.replace('repeat', '').trim()).toBe(taTr.grid.delete.deleting);
      expect(el('.awg__delete-confirm')?.textContent?.trim()).toBe(taTr.grid.delete.deleting);
      btn.click();
      render();
      expect(series).toEqual([]);

      // Seri silme sürerken Escape seçimi kapatmaz.
      el('.awg__scroll')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
      render();
      expect(component.selectedSlotId()).toBe(21);
      expect(el('.awg__delete-series')).not.toBeNull();
    }));

    it('deleteError seri modunda da çubukta görünür; seçim ve iki buton korunur', fakeAsync(() => {
      setup();
      clickEvent(21);

      fixture.componentRef.setInput('deleteError', 'Seri silinemedi.');
      render();

      expect(el('.awg__draft [role="alert"]')?.textContent?.trim()).toBe('Seri silinemedi.');
      expect(component.selectedSlotId()).toBe(21);
      expect(el('.awg__delete-series')).not.toBeNull();
    }));

    it('"Tüm seri" sonrası sayfa seçimi sıfırlayıp slotları yenileyince çubuk kapanır ve odak kaydırma bölgesine geçer', fakeAsync(() => {
      setup();
      clickEvent(21);
      el<HTMLButtonElement>('.awg__delete-series')!.focus();

      fixture.componentRef.setInput('slots', [single()]);
      fixture.componentRef.setInput('selectedSlotId', null);
      render();

      expect(el('.awg__delete-series')).toBeNull();
      expect(el('.awg__delete-confirm')).toBeNull();
      expect(document.activeElement).toBe(el('.awg__scroll'));
    }));

    it('randevulu tekrarlayan slota tıklama seçim kurmaz, "silinemez" ipucu gösterir', fakeAsync(() => {
      setup([recurringBooked()]);

      clickEvent(22);

      expect(component.selectedSlotId()).toBeNull();
      expect(el('.awg__delete-series')).toBeNull();
      expect(el('.awg__locked-hint')).not.toBeNull();
    }));

    it('klavye: tekrarlayan slotta Enter seçer ve iki silme seçeneği görünür; Escape ikisini de kapatır', fakeAsync(() => {
      setup();

      wrapperOf(21).dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
      render();
      expect(el('.awg__delete-series')).not.toBeNull();

      el('.awg__scroll')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
      render();
      expect(component.selectedSlotId()).toBeNull();
      expect(el('.awg__delete-series')).toBeNull();
    }));

    it('confirmDeleteSeries tekil slotta (kural yok) hiçbir şey yaymaz', fakeAsync(() => {
      setup();
      const series: number[] = [];
      component.deleteSeriesRequested.subscribe((id) => series.push(id));
      clickEvent(11);

      component['confirmDeleteSeries']();

      expect(series).toEqual([]);
    }));
  });

  it('should have tabindex="0" on wrapper element and NOT on .fc-event', fakeAsync(() => {
    const slot = createSlotInWeek(1, 3, 14, 15, false); // Thu 14:00-15:00, free

    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('slots', [slot]);
    });

    fixture.detectChanges();
    tick(500);

    // Wrapper .awg__ev should have tabindex="0"
    const eventWrappers = fixture.nativeElement.querySelectorAll('.awg__ev') as NodeListOf<HTMLElement>;
    expect(eventWrappers.length).toBeGreaterThan(0);
    const wrapperWithTabindex = Array.from(eventWrappers).find((el) => el.getAttribute('tabindex') === '0');
    expect(wrapperWithTabindex).toBeTruthy('Wrapper should have tabindex="0"');

    // .fc-event elements should NOT have tabindex (eventInteractive off)
    const fcEvents = fixture.nativeElement.querySelectorAll('.fc-event') as NodeListOf<HTMLElement>;
    for (const event of Array.from(fcEvents)) {
      expect(event.hasAttribute('tabindex')).toBeFalsy('.fc-event should not have tabindex');
    }
  }));

  it('should expose day, time, status and student name in aria-label of a booked slot', fakeAsync(() => {
    const slot = createSlotInWeek(1, 3, 14, 15, true, 'Approved');
    slot.studentName = 'Ayşe';

    fixture.componentRef.setInput('slots', [slot]);
    fixture.detectChanges();
    tick();

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
  }));

  it('should not expose student name when slot is not booked', fakeAsync(() => {
    const slot = createSlotInWeek(1, 3, 14, 15, false);
    slot.studentName = 'Ayşe';

    fixture.componentRef.setInput('slots', [slot]);
    fixture.detectChanges();
    tick();

    const wrapper = fixture.nativeElement.querySelector('.awg__ev') as HTMLElement;
    const label = wrapper.getAttribute('aria-label') ?? '';
    expect(label).not.toContain('Ayşe');
    expect(label.split(' · ')[2]).toBe(taTr.status.free);
    expect(wrapper.querySelector('.awg__ev-note')?.textContent?.trim()).toBe(taTr.status.free);
  }));

  it('should skip invalid slots', fakeAsync(() => {
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
    tick(500);

    // Component should render without error - invalid slot is skipped internally
    const calendar = fixture.nativeElement.querySelector('.awg__canvas') as HTMLElement;
    expect(calendar).toBeTruthy();

    // Verify only the valid slot is rendered (invalid one is skipped)
    const eventElements = fixture.nativeElement.querySelectorAll('.fc-timegrid-event') as NodeListOf<HTMLElement>;
    expect(eventElements.length).toBe(1, 'Should render only 1 valid event, skip invalid');
  }));

  it('should handle empty slots', fakeAsync(() => {
    TestBed.runInInjectionContext(() => {
      fixture.componentRef.setInput('slots', []);
    });

    fixture.detectChanges();
    tick();

    const section = fixture.nativeElement.querySelector('.awg');
    expect(section).toBeTruthy();
  }));

  it('should only render calendar on browser (not SSR)', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const canvas = fixture.nativeElement.querySelector('.awg__canvas') as HTMLElement;
    expect(canvas).toBeTruthy();
  }));

  it('should NOT render calendar in SSR environment but render toolbar', fakeAsync(() => {
    // Create a separate test bed with server platform
    const ssrTestBed = TestBed.resetTestingModule();
    ssrTestBed.configureTestingModule({
      imports: [AvailabilityWeekGridComponent, translocoTesting],
      providers: [{ provide: PLATFORM_ID, useValue: 'server' }],
    });

    const ssrFixture = ssrTestBed.createComponent(AvailabilityWeekGridComponent);
    ssrFixture.detectChanges();
    tick();

    // In SSR, FullCalendar canvas should not be rendered
    const canvas = ssrFixture.nativeElement.querySelector('.awg__canvas') as HTMLElement;
    expect(canvas).toBeFalsy('Calendar canvas should NOT be rendered in SSR');

    // But toolbar should still be rendered
    const toolbar = ssrFixture.nativeElement.querySelector('.awg__toolbar') as HTMLElement;
    expect(toolbar).toBeTruthy('Toolbar should be rendered in SSR');

    // Scroll area should not be rendered in SSR
    const scrollArea = ssrFixture.nativeElement.querySelector('.awg__scroll') as HTMLElement;
    expect(scrollArea).toBeFalsy('Scroll area should NOT be rendered in SSR');
  }));

  it('should have role="region" on scroll area for accessibility', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const scrollArea = fixture.nativeElement.querySelector('.awg__scroll');
    expect(scrollArea?.getAttribute('role')).toBe('region');
  }));

  it('should include aria-label on scroll area', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const scrollArea = fixture.nativeElement.querySelector('.awg__scroll');
    expect(scrollArea?.getAttribute('aria-label')).toBeTruthy();
  }));
});
