import { HttpErrorResponse } from '@angular/common/http';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { TranslocoService } from '@jsverse/transloco';
import { Observable, Subject, isObservable, of, throwError } from 'rxjs';

import { DEFAULT_BADGE_ICON, ParentChildProgressComponent } from './parent-child-progress.component';
import { ParentDashboardService } from '../../../services/parent-dashboard.service';
import { LocaleService } from '../../../services/locale.service';
import { ParentChildProgress } from '../../../models/parent-dashboard.model';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import parentDashboardTr from '../../../../../public/i18n/parent-dashboard/tr.json';
import parentDashboardEn from '../../../../../public/i18n/parent-dashboard/en.json';

@Component({
  standalone: true,
  imports: [ParentChildProgressComponent],
  template: `<app-parent-child-progress [studentId]="studentId()" (notFound)="notFoundCount = notFoundCount + 1" />`,
})
class HostComponent {
  readonly studentId = signal(11);
  notFoundCount = 0;
}

/** Issue #422: veli "Puan ve rozetler" kartı — seviye/puan/haftalık, kendi sırası, rozet ızgarası, boş/yükleniyor/hata. */
describe('ParentChildProgressComponent (issue #422)', () => {
  let service: jasmine.SpyObj<ParentDashboardService>;
  let fixture: ComponentFixture<HostComponent>;

  function progress(overrides: Partial<ParentChildProgress> = {}): ParentChildProgress {
    return {
      studentId: 11,
      totalXp: 2500,
      level: 8,
      weekStart: '2026-10-05',
      weeklyXp: 140,
      badges: [
        { name: 'Seri Ustası', icon: null, earnedOn: '2026-10-06' },
        { name: 'İlk Adım', icon: 'rocket_launch', earnedOn: '2026-09-01' },
      ],
      ranks: [
        { scope: 'global', rank: 4, totalCount: 1200 },
        { scope: 'school', rank: 2, totalCount: 85 },
      ],
      ...overrides,
    };
  }

  async function setup(
    response: ParentChildProgress | Observable<ParentChildProgress> = progress(),
    lang = 'tr'
  ): Promise<HTMLElement> {
    service = jasmine.createSpyObj<ParentDashboardService>('ParentDashboardService', ['getChildProgress']);
    service.getChildProgress.and.returnValue(isObservable(response) ? response : of(response));

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

  it('rendersLevelPointsWeeklyAndOwnRanks_SchoolFirst', async () => {
    const el = await setup();

    expect(service.getChildProgress).toHaveBeenCalledOnceWith(11);
    expect(text(q(el, 'progress-level'))).toContain('8');
    expect(text(q(el, 'progress-xp'))).toContain('2.500');
    expect(text(q(el, 'progress-weekly'))).toContain('+140');
    const ranks = Array.from(el.querySelectorAll('[data-test^="rank-"]')).map((r) => r.getAttribute('data-test'));
    expect(ranks).toEqual(['rank-school', 'rank-global']);
    expect(text(q(el, 'rank-school'))).toContain('Okulunda 2. sırada (85 öğrenci)');
    expect(text(q(el, 'rank-global'))).toContain('Genel sıralamada 4. (1.200 öğrenci)');
  });

  it('rendersBadgeGrid_WithDefaultIconAndDayOnlyDates', async () => {
    const el = await setup();

    const badges = Array.from(el.querySelectorAll('[data-test="badge"]'));
    expect(badges.length).toBe(2);
    expect(text(badges[0].querySelector('[data-test="badge-name"]'))).toBe('Seri Ustası');
    expect(text(badges[0].querySelector('mat-icon'))).toBe(DEFAULT_BADGE_ICON);
    expect(text(badges[1].querySelector('mat-icon'))).toBe('rocket_launch');
    // Gün kayması yok, saat yok.
    expect(text(badges[0].querySelector('[data-test="badge-date"]'))).toBe('6 Eki 2026');
  });

  it('noBadges_ShowsEmptyState', async () => {
    const el = await setup(progress({ badges: [] }));
    expect(q(el, 'badges-empty')).not.toBeNull();
    expect(el.querySelectorAll('[data-test="badge"]').length).toBe(0);
  });

  it('formatsLargeNumbersPerLocale_WithoutRecreatingTheFormatter', async () => {
    const numberFormat = spyOn(Intl, 'NumberFormat').and.callThrough();
    const el = await setup(progress({ totalXp: 1234567, weeklyXp: 0 }));
    expect(text(q(el, 'progress-xp'))).toContain('1.234.567');
    expect(text(q(el, 'progress-weekly'))).toContain('+0');
    // Biçimlendirici dil başına önbellekte (test sırasına göre zaten kurulmuş olabilir) — her değer için yeniden kurulmaz.
    expect(numberFormat.calls.count()).toBeLessThanOrEqual(1);
  });

  it('childWithoutSchool_ShowsOnlyGlobalRank', async () => {
    const el = await setup(progress({ ranks: [{ scope: 'global', rank: 1, totalCount: 3 }] }));
    expect(q(el, 'rank-school')).toBeNull();
    expect(q(el, 'rank-global')).not.toBeNull();
  });

  it('showsLoadingWhileTheRequestIsPending', async () => {
    const pending = new Subject<ParentChildProgress>();
    const el = await setup(pending);
    expect(q(el, 'progress-loading')).not.toBeNull();

    pending.next(progress());
    pending.complete();
    await settle();
    expect(q(el, 'progress-loading')).toBeNull();
    expect(q(el, 'progress-level')).not.toBeNull();
  });

  it('error_ShowsRetry_AndRetryReloads', async () => {
    const el = await setup(throwError(() => new HttpErrorResponse({ status: 500 })));
    expect(text(q(el, 'progress-error'))).toContain(parentDashboardTr.progress.loadError);

    service.getChildProgress.and.returnValue(of(progress()));
    (q(el, 'progress-error')!.querySelector('button') as HTMLButtonElement).click();
    await settle();
    expect(service.getChildProgress).toHaveBeenCalledTimes(2);
    expect(q(el, 'progress-level')).not.toBeNull();
  });

  it('notFound_EmitsToTheParent', async () => {
    const el = await setup(throwError(() => new HttpErrorResponse({ status: 404 })));
    expect(fixture.componentInstance.notFoundCount).toBe(1);
    expect(text(q(el, 'progress-error'))).toContain(parentDashboardTr.summaryNotFound);
  });

  it('reloadsWhenTheChildChanges', async () => {
    await setup();
    fixture.componentInstance.studentId.set(12);
    await settle();
    expect(service.getChildProgress.calls.allArgs()).toEqual([[11], [12]]);
  });

  it('english_RendersEnglishTexts', async () => {
    const el = await setup(progress(), 'en');
    expect(text(el.querySelector('mat-card-title'))).toBe(parentDashboardEn.progress.title);
    expect(text(q(el, 'rank-school'))).toContain('#2 in their school (85 students)');
    expect(text(q(el, 'progress-xp'))).toContain('2,500');
  });
});
