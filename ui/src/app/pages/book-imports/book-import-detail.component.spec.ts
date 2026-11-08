import type { MockedObject } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { NEVER, Observable, of, throwError } from 'rxjs';

import { BookImportDetailComponent } from './book-import-detail.component';
import { BOOK_IMPORTS_POLL_MS } from './book-imports.component';
import { BookImportJobDetail, BookImportJobTest } from '../../models/book-import';
import { BookImportService } from '../../services/book-import.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import tr from '../../../../public/i18n/book-imports/tr.json';

function detail(overrides: Partial<BookImportJobDetail> = {}): BookImportJobDetail {
  return {
    id: 'a1', bookName: 'Puan BTS 4 Matematik', gradeId: 4, subjectId: null, bookId: null,
    status: 'Running', stage: 'Setup', fileName: 'bts.pdf', fileSizeBytes: 1000, pageCount: 120,
    stageTotal: 4, stageProcessed: 1, message: null, error: null, createdAt: '2026-11-08T08:00:00Z',
    startedAt: '2026-11-08T08:00:05Z', finishedAt: null, ownerId: 3, resumeAt: null, aiCostUsd: null,
    costCeilingUsd: null, tests: [], costs: [], ...overrides,
  };
}

function test(overrides: Partial<BookImportJobTest> = {}): BookImportJobTest {
  return {
    id: 1, order: 1, unit: 1, testNo: 1, title: null, status: 'Done', questionsFound: 0, keyAnswers: 0, flags: [],
    questionsSaved: 0, duplicatesSkipped: 0, reviewFlags: [], worksheetId: null, fileFrom: null, fileTo: null, answerless: 0,
    costUsd: null, error: null, ...overrides,
  };
}

describe('BookImportDetailComponent (#1018)', () => {
  let fixture: ComponentFixture<BookImportDetailComponent>;
  let api: MockedObject<BookImportService>;
  let response: Observable<BookImportJobDetail>;

  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => el().textContent ?? '';
  const steps = () => Array.from(el().querySelectorAll('.bid-step'));

  function setup(): void {
    api = {
      list: vi.fn(),
      quota: vi.fn(),
      start: vi.fn(),
      get: vi.fn().mockName('get').mockImplementation(() => response),
      setup: vi.fn().mockReturnValue(NEVER),
      continue: vi.fn().mockReturnValue(of(undefined)),
    } as unknown as MockedObject<BookImportService>;
    TestBed.configureTestingModule({
      imports: [BookImportDetailComponent, translocoTestingModule({ langs: { 'book-imports/tr': tr } })],
      providers: [
        provideNoopAnimations(),
        provideRouter([]),
        { provide: BookImportService, useValue: api },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'a1' }) } } },
      ],
    });
    fixture = TestBed.createComponent(BookImportDetailComponent);
    fixture.detectChanges();
  }

  beforeEach(() => {
    response = of(detail());
  });

  afterEach(() => vi.useRealTimers());

  it('init_ShowsStepperWithCurrentStageAndProgress', () => {
    setup();

    expect(api.get).toHaveBeenCalledWith('a1');
    expect(steps().length).toBe(8);
    expect(steps()[0].classList).toContain('bid-step--done');
    expect(steps()[2].classList).toContain('bid-step--current');
    expect(steps()[2].getAttribute('aria-current')).toBe('step');
    expect(steps()[3].classList).toContain('bid-step--todo');
    expect(text()).toContain('Geçerli aşama: Kurulum');
    expect(text()).toContain('1 / 4');
    expect(text()).toContain(tr.detail.noTestsYet);
  });

  it('awaitingConfirmation_MarksStepWaitingAndShowsTheSetupScreen', () => {
    response = of(detail({ status: 'AwaitingConfirmation', stage: 'SetupConfirmation', stageTotal: 0, stageProcessed: 0 }));
    setup();

    expect(steps()[3].classList).toContain('bid-step--waiting');
    expect(text()).toContain(tr.detail.awaitingConfirmation);
    // #1021: onay ekranı kurulumu yükler.
    expect(el().querySelector('app-book-import-setup')).not.toBeNull();
    expect(api.setup).toHaveBeenCalledWith('a1');
  });

  it('awaitingBudget_ShowsWaitingStepAndResumeBanner_WithoutCostForNonAdmins', () => {
    response = of(detail({ status: 'AwaitingBudget', stage: 'Questions', resumeAt: '2026-11-09T08:00:00Z' }));
    setup();

    expect(steps()[4].classList).toContain('bid-step--waiting');
    expect(el().querySelector('.bid-banner--warning')!.textContent).toContain('kendiliğinden devam');
    expect(el().querySelector('.bid-cost')).toBeNull();
    expect(el().querySelector('.bid-banner__action')).toBeNull();
  });

  it('awaitingCostApproval_AdminSeesCostBreakdownAndContinues', () => {
    response = of(detail({
      status: 'AwaitingCostApproval', stage: 'Questions', aiCostUsd: 5.0123, costCeilingUsd: 5,
      costs: [
        { stage: 'Setup', modelId: 'claude-sonnet-5-5', calls: 4, inputTokens: 12000, outputTokens: 3000, costUsd: 0.12 },
        { stage: 'Questions', modelId: 'claude-haiku-5-5', calls: 300, inputTokens: 900000, outputTokens: 90000, costUsd: 4.8923 },
      ],
    }));
    setup();

    expect(el().querySelector('.bid-banner--warning')!.textContent).toContain('$5.0123');
    const rows = Array.from(el().querySelectorAll('.bid-cost__table tbody tr')).map((r) => r.textContent ?? '');
    expect(rows.length).toBe(2);
    expect(rows[0]).toContain('Kurulum');
    expect(rows[1]).toContain('claude-haiku-5-5');
    expect(rows[1]).toContain('$4.8923');

    (el().querySelector('.bid-banner__action') as HTMLButtonElement).click();
    expect(api.continue).toHaveBeenCalledWith('a1');
    expect(api.get).toHaveBeenCalledTimes(2);
  });

  it('failed_ShowsFailedStepAndError', () => {
    response = of(detail({ status: 'Failed', stage: 'Pages', error: 'PDF bozuk' }));
    setup();

    expect(steps()[1].classList).toContain('bid-step--failed');
    expect(el().querySelector('.bid-banner--danger')!.textContent).toContain('PDF bozuk');
  });

  it('tests_RenderGridWithLabelsAndAttentionList', () => {
    response = of(detail({
      stage: 'Questions',
      tests: [
        test({ order: 1, status: 'Done', questionsFound: 10, keyAnswers: 10, questionsSaved: 10 }),
        test({
          order: 2, testNo: 2, status: 'NeedsReview', questionsFound: 9, keyAnswers: 10, questionsSaved: 8, duplicatesSkipped: 1,
          flags: ['keyMismatch'],
          reviewFlags: [
            { code: 'missingNumbers', pages: [12], numbers: [7], found: null, expected: null },
            { code: 'countMismatch', pages: [12, 13], numbers: null, found: 9, expected: 10 },
          ],
        }),
        test({ order: 3, unit: 2, status: 'Failed', error: 'Sayfa okunamadı' }),
        test({ order: 4, unit: 2, testNo: 2, status: 'Pending' }),
      ],
    }));
    setup();

    const cells = el().querySelectorAll('.bid-grid__cell');
    expect(cells.length).toBe(4);
    expect(cells[1].getAttribute('aria-label')).toBe('Ünite 1, test 2: Kontrol gerekli · 9 soru bulundu, anahtarda 10');
    expect(cells[3].getAttribute('aria-label')).toBe('Ünite 2, test 2: Bekliyor');
    expect(cells[2].classList).toContain('bid-grid__cell--failed');
    const attention = el().querySelector('.bid-attention')!.textContent ?? '';
    expect(attention).toContain('Ünite 1 · Test 2');
    expect(attention).toContain(tr.flags.keyMismatch);
    expect(attention).toContain('Sayfa okunamadı');
    expect(attention).not.toContain('Ünite 1 · Test 1');
    expect(attention).toContain('9 / 10 soru · 8 kaydedildi');
    expect(attention).toContain('1 tekrar atlandı');
    expect(attention).toContain(tr.reviewFlags.missingNumbers);
    expect(attention).toContain('sayfa 12 · soru 7');
    expect(attention).toContain('9 soru bulundu, anahtarda 10');
    expect(attention).toContain('sayfa 12, 13');
  });

  it('notFound_ShowsMessage', () => {
    response = throwError(() => ({ status: 404 }));
    setup();

    expect(text()).toContain(tr.detail.notFound);
  });

  it('poll_ReloadsWhileRunning_StopsAtCheckpoint', async () => {
    vi.useFakeTimers();
    setup();
    expect(api.get).toHaveBeenCalledTimes(1);

    response = of(detail({ status: 'AwaitingConfirmation', stage: 'SetupConfirmation' }));
    await vi.advanceTimersByTimeAsync(BOOK_IMPORTS_POLL_MS);
    expect(api.get).toHaveBeenCalledTimes(2);

    await vi.advanceTimersByTimeAsync(BOOK_IMPORTS_POLL_MS * 3);
    expect(api.get).toHaveBeenCalledTimes(2);
  });
});
