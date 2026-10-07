import { HttpErrorResponse } from '@angular/common/http';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { TranslocoService } from '@jsverse/transloco';
import { Observable, Subject, isObservable, of, throwError } from 'rxjs';

import { ParentChildScheduleComponent, buildDays, shiftWeek } from './parent-child-schedule.component';
import { ParentDashboardService } from '../../../services/parent-dashboard.service';
import { LocaleService } from '../../../services/locale.service';
import { ParentChildSchedule } from '../../../models/parent-dashboard.model';
import { dayInTimeZone, formatTime } from '../parent-format';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import parentDashboardTr from '../../../../../public/i18n/parent-dashboard/tr.json';
import parentDashboardEn from '../../../../../public/i18n/parent-dashboard/en.json';

@Component({
  standalone: true,
  imports: [ParentChildScheduleComponent],
  template: `<app-parent-child-schedule [studentId]="studentId()" (notFound)="notFoundCount = notFoundCount + 1" />`,
})
class HostComponent {
  readonly studentId = signal(11);
  notFoundCount = 0;
}

/**
 * Issue #422: veli "Program" — haftalık liste (Europe/Istanbul günleri ve saatleri, tarayıcı saat diliminden bağımsız),
 * önceki/sonraki hafta, boş/yükleniyor/hata, ders bağlantısı yok.
 */
describe('ParentChildScheduleComponent (issue #422)', () => {
  let service: jasmine.SpyObj<ParentDashboardService>;
  let fixture: ComponentFixture<HostComponent>;

  // Saatler UTC verilir; beklenen gösterim Istanbul (UTC+3): 11:00Z → 14:00.
  function week(overrides: Partial<ParentChildSchedule> = {}): ParentChildSchedule {
    return {
      studentId: 11,
      from: '2026-10-05',
      to: '2026-10-11',
      plans: [
        { title: 'Kesirler', subject: 'Matematik', plannedOn: '2026-10-06' },
        { title: 'Oran Orantı', subject: null, plannedOn: '2026-10-08' },
      ],
      lessons: [
        { teacherName: 'Zeynep Hoca', startsOn: '2026-10-06', startAt: '2026-10-06T11:00:00Z', endAt: '2026-10-06T12:00:00Z', status: 'approved' },
        { teacherName: null, startsOn: '2026-10-09', startAt: '2026-10-09T06:00:00Z', endAt: '2026-10-09T07:00:00Z', status: 'pending' },
      ],
      ...overrides,
    };
  }

  async function setup(
    response: ParentChildSchedule | Observable<ParentChildSchedule> = week(),
    lang = 'tr'
  ): Promise<HTMLElement> {
    service = jasmine.createSpyObj<ParentDashboardService>('ParentDashboardService', ['getChildSchedule']);
    service.getChildSchedule.and.returnValue(isObservable(response) ? response : of(response));

    TestBed.configureTestingModule({
      imports: [
        HostComponent,
        NoopAnimationsModule,
        translocoTestingModule({
          langs: { 'parent-dashboard/tr': parentDashboardTr, 'parent-dashboard/en': parentDashboardEn },
        }),
      ],
      providers: [
        { provide: ParentDashboardService, useValue: service },
        { provide: LocaleService, useValue: { localeDefinition: signal({ angularLocale: lang === 'tr' ? 'tr-TR' : 'en-US' }) } },
      ],
    });
    TestBed.inject(TranslocoService).setActiveLang(lang);
    fixture = TestBed.createComponent(HostComponent);
    await settle();
    return fixture.nativeElement as HTMLElement;
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const text = (el: Element | null) => el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const q = (el: HTMLElement, test: string) => el.querySelector(`[data-test="${test}"]`);
  const click = (el: HTMLElement, test: string) => (q(el, test) as HTMLButtonElement).click();

  it('firstLoad_AsksForTheServersCurrentWeek_AndListsSevenDays', async () => {
    const el = await setup();

    expect(service.getChildSchedule).toHaveBeenCalledOnceWith(11, undefined);
    expect(text(q(el, 'schedule-range'))).toBe('5 Ekim – 11 Ekim 2026');
    const days = Array.from(el.querySelectorAll('[data-test="schedule-day"]'));
    expect(days.length).toBe(7);
    expect(text(days[0].querySelector('[data-test="day-label"]'))).toBe('5 Ekim Pazartesi');
    // Salı: bir ders + bir plan; saat Türkiye saatiyle.
    expect(text(days[1].querySelector('[data-test="lesson-row"]'))).toContain('Zeynep Hoca ile ders');
    expect(text(days[1].querySelector('[data-test="lesson-time"]'))).toBe('14:00–15:00');
    expect(text(days[1].querySelector('[data-test="lesson-status"]'))).toBe('Onaylandı');
    expect(text(days[1].querySelector('[data-test="plan-title"]'))).toBe('Kesirler');
    // Öğretmen adı çözülemeyen ders.
    expect(text(days[4].querySelector('[data-test="lesson-row"]'))).toContain('Ders');
    expect(text(days[4].querySelector('[data-test="lesson-time"]'))).toBe('09:00–10:00');
    expect(text(days[4].querySelector('[data-test="lesson-status"]'))).toBe('Onay bekliyor');
    expect(q(el, 'schedule-this-week')).toBeNull();
  });

  it('groupsLessonsByTheServersIstanbulDay_IncludingOneOverlappingTheRangeStart', async () => {
    const el = await setup(
      week({
        plans: [],
        lessons: [
          // Pazar 23:30–00:30 (İstanbul): aralığa taşar → sunucu startsOn = Pazartesi.
          { teacherName: 'Gece Hoca', startsOn: '2026-10-05', startAt: '2026-10-04T20:30:00Z', endAt: '2026-10-04T21:30:00Z', status: 'pending' },
          // 21:30Z = İstanbul Çarşamba 00:30 (UTC'de hâlâ Salı).
          { teacherName: 'Geç Hoca', startsOn: '2026-10-07', startAt: '2026-10-06T21:30:00Z', endAt: '2026-10-06T22:30:00Z', status: 'approved' },
        ],
      })
    );

    const days = Array.from(el.querySelectorAll('[data-test="schedule-day"]'));
    expect(text(days[0].querySelector('[data-test="lesson-row"]'))).toContain('Gece Hoca');
    expect(text(days[0].querySelector('[data-test="lesson-time"]'))).toBe('23:30–00:30');
    expect(days[1].querySelector('[data-test="lesson-row"]')).toBeNull();
    expect(text(days[2].querySelector('[data-test="lesson-row"]'))).toContain('Geç Hoca');
    expect(text(days[2].querySelector('[data-test="lesson-time"]'))).toBe('00:30–01:30');
  });

  it('rendersNoMeetingLinks', async () => {
    const el = await setup();
    expect(el.querySelectorAll('a').length).toBe(0);
    expect(el.innerHTML).not.toContain('http');
  });

  it('previousAndNextWeek_RequestShiftedRanges_AndThisWeekReturns', async () => {
    const el = await setup();
    service.getChildSchedule.and.callFake((_id: number, range?: { from: string; to: string }) =>
      of(week({ from: range!.from, to: range!.to, plans: [], lessons: [] }))
    );

    click(el, 'schedule-prev');
    await settle();
    expect(service.getChildSchedule).toHaveBeenCalledWith(11, { from: '2026-09-28', to: '2026-10-04' });
    expect(text(q(el, 'schedule-range'))).toBe('28 Eylül – 4 Ekim 2026');
    expect(q(el, 'schedule-this-week')).not.toBeNull();
    expect(text(q(el, 'schedule-empty'))).toBe(parentDashboardTr.schedule.empty);

    click(el, 'schedule-next');
    await settle();
    click(el, 'schedule-next');
    await settle();
    expect(service.getChildSchedule).toHaveBeenCalledWith(11, { from: '2026-10-12', to: '2026-10-18' });

    click(el, 'schedule-this-week');
    await settle();
    expect(service.getChildSchedule.calls.mostRecent().args).toEqual([11, { from: '2026-10-05', to: '2026-10-11' }]);
    expect(q(el, 'schedule-this-week')).toBeNull();
  });

  it('showsLoadingWhileTheRequestIsPending_AndDisablesNavigation', async () => {
    const pending = new Subject<ParentChildSchedule>();
    const el = await setup(pending);
    expect(q(el, 'schedule-loading')).not.toBeNull();
    expect((q(el, 'schedule-prev') as HTMLButtonElement).disabled).toBeTrue();

    pending.next(week());
    pending.complete();
    await settle();
    expect(q(el, 'schedule-loading')).toBeNull();
    expect((q(el, 'schedule-prev') as HTMLButtonElement).disabled).toBeFalse();
  });

  it('error_ShowsRetry_AndRetryRepeatsTheSameRequest', async () => {
    const el = await setup(throwError(() => new HttpErrorResponse({ status: 500 })));
    expect(text(q(el, 'schedule-error'))).toContain(parentDashboardTr.schedule.loadError);

    service.getChildSchedule.and.returnValue(of(week()));
    (q(el, 'schedule-error')!.querySelector('button') as HTMLButtonElement).click();
    await settle();
    expect(service.getChildSchedule.calls.allArgs()).toEqual([
      [11, undefined],
      [11, undefined],
    ]);
    expect(el.querySelectorAll('[data-test="schedule-day"]').length).toBe(7);
  });

  it('rangeOutsideTheServerLimit_ShowsTheRangeError', async () => {
    const el = await setup(throwError(() => new HttpErrorResponse({ status: 400 })));
    expect(text(q(el, 'schedule-error'))).toContain(parentDashboardTr.schedule.rangeError);
  });

  it('notFound_EmitsToTheParent', async () => {
    const el = await setup(throwError(() => new HttpErrorResponse({ status: 404 })));
    expect(fixture.componentInstance.notFoundCount).toBe(1);
    expect(text(q(el, 'schedule-error'))).toContain(parentDashboardTr.summaryNotFound);
  });

  it('childChange_ReturnsToTheCurrentWeek', async () => {
    const el = await setup();
    service.getChildSchedule.and.callFake((id: number, range?: { from: string; to: string }) =>
      of(week({ studentId: id, ...(range ?? {}) }))
    );
    click(el, 'schedule-prev');
    await settle();

    fixture.componentInstance.studentId.set(12);
    await settle();
    expect(service.getChildSchedule.calls.mostRecent().args).toEqual([12, undefined]);
    expect(text(q(el, 'schedule-range'))).toBe('5 Ekim – 11 Ekim 2026');
  });

  it('english_RendersEnglishTexts', async () => {
    const el = await setup(week(), 'en');
    expect(text(el.querySelector('h2'))).toBe(parentDashboardEn.schedule.title);
    expect(text(q(el, 'schedule-range'))).toBe('October 5 – October 11, 2026');
    expect(text(el.querySelector('[data-test="lesson-status"]'))).toBe('Approved');
  });

  it('helpers_ShiftWeeks_PlaceItemsOnServerDays_AndFormatInIstanbulTime', () => {
    expect(shiftWeek({ from: '2026-12-28', to: '2027-01-03' }, 7)).toEqual({ from: '2027-01-04', to: '2027-01-10' });
    expect(shiftWeek({ from: '2026-03-02', to: '2026-03-08' }, -7)).toEqual({ from: '2026-02-23', to: '2026-03-01' });
    expect(shiftWeek({ from: 'bogus', to: 'x' }, 7)).toBeNull();

    const days = buildDays(week(), '2026-10-06');
    expect(days.map((d) => d.date)).toEqual([
      '2026-10-05', '2026-10-06', '2026-10-07', '2026-10-08', '2026-10-09', '2026-10-10', '2026-10-11',
    ]);
    expect(days[1].isToday).toBeTrue();
    expect(days[1].lessons.length).toBe(1);
    expect(days[1].plans.map((p) => p.title)).toEqual(['Kesirler']);
    expect(days[3].plans.map((p) => p.title)).toEqual(['Oran Orantı']);
    expect(days[4].lessons[0].status).toBe('pending');

    // Tarayıcı saat dilimi ne olursa olsun Istanbul (UTC+3).
    expect(formatTime('2026-10-06T21:30:00Z', 'tr-TR')).toBe('00:30');
    expect(dayInTimeZone(new Date('2026-10-06T21:30:00Z'))).toBe('2026-10-07');
    expect(dayInTimeZone(new Date('2026-10-06T20:59:00Z'))).toBe('2026-10-06');
  });
});
