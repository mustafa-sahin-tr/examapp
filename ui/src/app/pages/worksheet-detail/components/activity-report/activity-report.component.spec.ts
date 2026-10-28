import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of, throwError } from 'rxjs';
import { MockedObject, vi } from 'vitest';

import { ActivityReportComponent } from './activity-report.component';
import { ActivityReport } from '../../../../models/activity-report';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../../../models/locale';
import { TestService } from '../../../../services/test.service';
import rootTr from '../../../../../../public/i18n/tr.json';
import worksheetDetailTr from '../../../../../../public/i18n/worksheet-detail/tr.json';

const translocoTesting = TranslocoTestingModule.forRoot({
  langs: { tr: { ...rootTr, 'worksheet-detail': worksheetDetailTr }, 'worksheet-detail/tr': worksheetDetailTr },
  translocoConfig: { availableLangs: [...SUPPORTED_LOCALE_CODES], defaultLang: DEFAULT_LOCALE, scopes: { keepCasing: true } },
  preloadLangs: true,
});

function report(overrides: Partial<ActivityReport> = {}): ActivityReport {
  return {
    worksheetId: 7,
    worksheetName: 'Vücudumuz',
    retrievedAt: '2026-10-28T09:00:00Z',
    studentCount: 4,
    analyzedCount: 2,
    retakingCount: 1,
    questions: [
      {
        worksheetQuestionId: 11,
        number: 1,
        interactionType: 'dragPlace',
        gradable: true,
        fullyCorrectCount: 1,
        answeredCount: 2,
        items: [
          {
            itemId: 'c1',
            label: 'Kemik',
            distractor: false,
            correctCount: 1,
            emptyCount: 0,
            studentCount: 2,
            wrongAnswers: [{ label: 'Kaslar', count: 1 }],
          },
          {
            itemId: 'c3',
            label: 'Çeldirici',
            distractor: true,
            correctCount: 2,
            emptyCount: 0,
            studentCount: 2,
            wrongAnswers: [],
          },
        ],
      },
      {
        worksheetQuestionId: 12,
        number: 2,
        interactionType: 'openText',
        gradable: false,
        fullyCorrectCount: 0,
        answeredCount: 1,
        items: [],
      },
    ],
    students: [
      {
        studentId: 1,
        userId: 1,
        studentNumber: '101',
        gradeName: '5-A',
        cells: [
          { answered: true, correctCount: 2, totalCount: 2 },
          { answered: true, correctCount: 0, totalCount: 0 },
        ],
      },
      {
        studentId: 2,
        userId: 2,
        studentNumber: '102',
        gradeName: null,
        cells: [
          { answered: true, correctCount: 0, totalCount: 2 },
          { answered: false, correctCount: 0, totalCount: 0 },
        ],
      },
    ],
    ...overrides,
  };
}

describe('ActivityReportComponent (#584)', () => {
  let fixture: ComponentFixture<ActivityReportComponent>;
  let testService: MockedObject<Pick<TestService, 'getActivityReport'>>;
  const el = () => fixture.nativeElement as HTMLElement;
  const q = (selector: string) => el().querySelector(selector) as HTMLElement | null;
  const all = (selector: string) => Array.from(el().querySelectorAll(selector)) as HTMLElement[];

  function setup(): void {
    testService = { getActivityReport: vi.fn() } as unknown as MockedObject<Pick<TestService, 'getActivityReport'>>;
    TestBed.configureTestingModule({
      imports: [ActivityReportComponent, translocoTesting],
      providers: [{ provide: TestService, useValue: testService }],
    });
    fixture = TestBed.createComponent(ActivityReportComponent);
    fixture.componentRef.setInput('worksheetId', 7);
    fixture.detectChanges();
  }

  function loadWith(value: ActivityReport): void {
    testService.getActivityReport.mockReturnValue(of(value));
    q('[data-testid="activity-report-load"]')!.click();
    fixture.detectChanges();
  }

  it('idle_DoesNotLoadUntilAsked_NoPercentShown', () => {
    setup();

    expect(testService.getActivityReport).not.toHaveBeenCalled();
    expect(q('[data-testid="activity-report-heatmap"]')).toBeNull();
    expect(el().textContent).not.toMatch(/%\d/);
  });

  it('load_ShowsCoverageAndHeatmapCellsWithTones', () => {
    setup();
    loadWith(report());

    expect(testService.getActivityReport).toHaveBeenCalledWith(7);
    expect(q('[data-testid="activity-report-coverage"]')!.textContent).toContain('4 öğrenciden 2');
    expect(q('[data-testid="activity-report-coverage"]')!.textContent).toContain('1 öğrenci yeniden çözüyor');

    const cells = all('[data-testid="heat-cell"]');
    expect(cells.map((c) => c.textContent!.trim())).toEqual(['2/2', '·', '0/2', '·']);
    expect(cells[0].classList).toContain('ar__cell--good');
    expect(cells[2].classList).toContain('ar__cell--low');
    expect(cells[1].classList).toContain('ar__cell--none');
  });

  it('items_ShowCorrectRateAndMostCommonWrongAnswer_DistractorTagged', () => {
    setup();
    loadWith(report());

    const items = all('[data-testid="activity-report-item"]');
    expect(items[0].textContent).toContain('Kemik');
    expect(items[0].textContent).toContain('%50 doğru');
    expect(items[0].querySelector('[data-testid="activity-report-wrong"]')!.textContent).toContain('%50 → Kaslar');
    expect(items[1].textContent).toContain('çeldirici');
    expect(items[1].textContent).toContain('%100 doğru');
    expect(items[1].querySelector('[data-testid="activity-report-wrong"]')).toBeNull();
  });

  it('openText_ShowsReviewQueueNote', () => {
    setup();
    loadWith(report());

    const panels = all('[data-testid="activity-report-question"]');
    expect(panels).toHaveLength(2);
    expect(panels[1].textContent).toContain('Açık uçlu');
    expect(panels[1].textContent).toContain('2 öğrenciden 1 cevapladı');
  });

  it('noCompletedStudents_ShowsEmptyMessage', () => {
    setup();
    loadWith(report({ analyzedCount: 0, students: [] }));

    expect(q('[data-testid="activity-report-empty"]')).not.toBeNull();
    expect(q('[data-testid="activity-report-heatmap"]')).toBeNull();
  });

  it('error_ShowsRetry', () => {
    setup();
    testService.getActivityReport.mockReturnValue(throwError(() => new Error('boom')));
    q('[data-testid="activity-report-load"]')!.click();
    fixture.detectChanges();

    expect(el().textContent).toContain('Analiz getirilemedi.');
  });
});
