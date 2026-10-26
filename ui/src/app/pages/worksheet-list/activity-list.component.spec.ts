import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { TranslocoTestingModule } from '@jsverse/transloco';

import { ActivityListComponent } from './activity-list.component';
import { BadgeService } from '../../services/badge.service';
import { TestService } from '../../services/test.service';
import { Test } from '../../models/test-instance';
import { AssignedWorksheet } from '../../models/assignment';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../models/locale';
import rootTr from '../../../../public/i18n/tr.json';
import worksheetListTr from '../../../../public/i18n/worksheet-list/tr.json';

const translocoTesting = TranslocoTestingModule.forRoot({
  langs: { tr: { ...rootTr, 'worksheet-list': worksheetListTr }, 'worksheet-list/tr': worksheetListTr },
  translocoConfig: { availableLangs: [...SUPPORTED_LOCALE_CODES], defaultLang: DEFAULT_LOCALE, scopes: { keepCasing: true } },
  preloadLangs: true,
});

const paged = (items: Partial<Test>[]) => of({ items: items as Test[], totalCount: items.length, pageNumber: 1, pageSize: 50 });

describe('ActivityListComponent (#573)', () => {
  let fixture: ComponentFixture<ActivityListComponent>;
  let testService: { getActiveAssignments: Mock; listWorksheets: Mock };
  let badgeService: { getActivitySummary: Mock };
  const el = () => fixture.nativeElement as HTMLElement;

  function setup(): void {
    TestBed.configureTestingModule({
      imports: [ActivityListComponent, translocoTesting],
      providers: [provideRouter([]), { provide: TestService, useValue: testService }, { provide: BadgeService, useValue: badgeService }],
    });
    fixture = TestBed.createComponent(ActivityListComponent);
    fixture.detectChanges();
  }

  afterEach(() => localStorage.removeItem('user'));

  beforeEach(() => {
    localStorage.setItem('user', JSON.stringify({ id: 77 }));
    badgeService = { getActivitySummary: vi.fn().mockReturnValue(of({ today: { activityCount: 0, activeMinutes: 0, streakDays: 0 }, badges: [] })) };
    testService = {
      getActiveAssignments: vi.fn().mockReturnValue(of([])),
      listWorksheets: vi.fn().mockImplementation((f: { statuses: number[] }) =>
        f.statuses[0] === 0 ? paged([]) : paged([])
      ),
    };
  });

  it('empty_ShowsEmptyState', () => {
    setup();

    expect(el().querySelector('[data-testid="activity-empty"]')?.textContent).toContain('Henüz etkinlik yok');
  });

  it('allSourcesFail_ShowsAlertWithRetry', () => {
    testService.getActiveAssignments.mockReturnValue(throwError(() => new Error('x')));
    testService.listWorksheets.mockReturnValue(throwError(() => new Error('x')));
    setup();

    const alert = el().querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('Etkinlikler yüklenemedi.');
    testService.getActiveAssignments.mockReturnValue(of([]));
    testService.listWorksheets.mockReturnValue(paged([]));
    (alert?.querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el().querySelector('[role="alert"]')).toBeNull();
  });

  it('sections_NewInProgressCompleted_ActivityCardsWithoutScore', () => {
    testService.getActiveAssignments.mockReturnValue(
      of([
        { worksheetId: 1, name: 'Yeni etkinlik', kind: 1, hasStarted: false, isCompleted: false, questionCount: 5 },
        { worksheetId: 2, name: 'Başlanmış', kind: 1, hasStarted: true, isCompleted: false, questionCount: 4 },
      ] as AssignedWorksheet[])
    );
    testService.listWorksheets.mockImplementation((f: { statuses: number[] }) =>
      f.statuses[0] === 0
        ? paged([{ id: 2, name: 'Başlanmış', kind: 1, questionCount: 4, instance: { status: 0, answeredQuestions: 2, totalQuestions: 4 } as Test['instance'] }])
        : paged([{ id: 3, name: 'Biten', kind: 1, questionCount: 10, instance: { status: 1, score: 0, correctAnswers: 0, wrongAnswers: 0, firstCheckCorrectCount: 8, firstCheckTotalCount: 10 } as Test['instance'] }])
    );
    setup();

    const titles = Array.from(el().querySelectorAll('.al__section-title')).map((h) => h.textContent?.trim());
    expect(titles).toEqual(['Yeni', 'Devam ediyor', 'Tamamlandı']);
    const cards = Array.from(el().querySelectorAll('app-worksheet-list-view-card'));
    expect(cards.length).toBe(3);
    expect(cards[0].textContent).toContain('5 madde');
    expect(cards[0].querySelector('[data-testid="activity-tag"]')).not.toBeNull();
    // Devam eden: mini ilerleme answeredQuestions'tan (2/4).
    expect((cards[1].querySelector('.wcard__progress span') as HTMLElement).style.width).toBe('50%');
    // Tamamlanan: skor halkası/yüzde yok.
    expect(cards[2].querySelector('.wcard__ring')).toBeNull();
    expect(cards[2].textContent).toContain('8 / 10 doğru');
    expect(el().textContent).not.toMatch(/%|puan|XP/i);
  });

  it('partialFailure_ShowsNonBlockingWarningAndStillRendersLoadedSections', () => {
    testService.getActiveAssignments.mockReturnValue(throwError(() => new Error('x')));
    testService.listWorksheets.mockImplementation((f: { statuses: number[] }) =>
      f.statuses[0] === 0 ? paged([{ id: 2, name: 'Başlanmış', kind: 1, instance: { status: 0 } as Test['instance'] }]) : paged([])
    );
    setup();

    const warning = el().querySelector('[data-testid="activity-partial-error"]');
    expect(warning?.getAttribute('role')).toBe('status');
    expect(warning?.textContent).toContain('Bazı etkinlikler yüklenemedi');
    expect(el().querySelector('[role="alert"]')).toBeNull();
    expect(el().querySelectorAll('app-worksheet-list-view-card').length).toBe(1);
  });

  it('i572_ShowsTodayAndBadgeProgressAboveList', () => {
    badgeService.getActivitySummary.mockReturnValue(
      of({
        today: { activityCount: 2, activeMinutes: 14, streakDays: 3 },
        badges: [{ code: 'activity-count-1', name: 'Kâşif', current: 8, target: 10, earned: false }],
      })
    );
    setup();

    expect(badgeService.getActivitySummary).toHaveBeenCalledWith(77);
    const strip = el().querySelector('[data-testid="activity-list-progress"]');
    expect(strip?.textContent).toContain('2 etkinlik · 14 dk etkin süre · 3 gün üst üste');
    expect(strip?.textContent).toContain('Kâşif rozeti: 8/10 etkinlik');
  });

  it('i572_SummaryFailureOrNothingToShow_HidesStrip', () => {
    badgeService.getActivitySummary.mockReturnValue(throwError(() => new Error('500')));
    setup();

    expect(el().querySelector('[data-testid="activity-list-progress"]')).toBeNull();
    expect(el().querySelector('[data-testid="activity-empty"]')).not.toBeNull();
  });
});

