import { ComponentFixture, TestBed, fakeAsync, tick, flush } from '@angular/core/testing';
import { PLATFORM_ID } from '@angular/core';
import { AvailabilityWeekGridComponent } from './availability-week-grid.component';
import { AvailabilitySlot, ActiveBookingStatus, CreateAvailabilitySlotRequest } from '../../../models/booking.model';
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
      imports: [AvailabilityWeekGridComponent, translocoTesting],
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

  it('should render legend with three status items', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    const legendItems = fixture.nativeElement.querySelectorAll('.awg__legend-item');
    expect(legendItems.length).toBe(3);
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
