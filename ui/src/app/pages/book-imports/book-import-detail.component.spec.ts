import type { MockedObject } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { NEVER, Observable, of, throwError } from 'rxjs';

import { BookImportDetailComponent } from './book-import-detail.component';
import { BOOK_IMPORTS_POLL_MS } from './book-imports.component';
import { BookImportJobDetail, BookImportJobTest } from '../../models/book-import';
import { AuthService } from '../../services/auth.service';
import { BookImportService } from '../../services/book-import.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import tr from '../../../../public/i18n/book-imports/tr.json';

function detail(overrides: Partial<BookImportJobDetail> = {}): BookImportJobDetail {
  return {
    id: 'a1', bookName: 'Puan BTS 4 Matematik', gradeId: 4, subjectId: null, bookId: null,
    status: 'Running', stage: 'Setup', fileName: 'bts.pdf', fileSizeBytes: 1000, pageCount: 120,
    stageTotal: 4, stageProcessed: 1, message: null, error: null, createdAt: '2026-11-08T08:00:00Z',
    startedAt: '2026-11-08T08:00:05Z', finishedAt: null, ownerId: 3, resumeAt: null, stalled: false, aiCostUsd: null,
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
  let roles: string[] = ['Admin'];
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
      retry: vi.fn().mockReturnValue(of(undefined)),
      retryTest: vi.fn().mockReturnValue(of(undefined)),
      retryFailedTests: vi.fn().mockReturnValue(of(undefined)),
      cancel: vi.fn().mockReturnValue(of(undefined)),
    } as unknown as MockedObject<BookImportService>;
    TestBed.configureTestingModule({
      imports: [BookImportDetailComponent, translocoTestingModule({ langs: { 'book-imports/tr': tr } })],
      providers: [
        provideNoopAnimations(),
        provideRouter([]),
        { provide: BookImportService, useValue: api },
        { provide: AuthService, useValue: { hasRealmRole: (role: string) => roles.includes(role) } },
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

  it('failed_ExplainsInPlainLanguageAndRetriesTheJob', () => {
    response = of(detail({ status: 'Failed', stage: 'Questions', error: 'Claude request failed.' }));
    setup();

    const banner = el().querySelector('.bid-banner--danger')!.textContent ?? '';
    expect(banner).toContain(tr.detail.failedHelp.Questions);
    expect(banner).toContain('Teknik ayrıntı: Claude request failed.');
    (el().querySelector('.bid-banner--danger .bid-banner__action') as HTMLButtonElement).click();
    expect(api.retry).toHaveBeenCalledWith('a1');
    expect(api.get).toHaveBeenCalledTimes(2);
  });

  it('stalled_ShowsRetryBanner', () => {
    response = of(detail({ status: 'Running', stalled: true }));
    setup();

    expect(text()).toContain(tr.detail.stalled);
    (el().querySelector('.bid-banner--warning .bid-banner__action') as HTMLButtonElement).click();
    expect(api.retry).toHaveBeenCalledWith('a1');
  });

  it('failedTests_RetryOneOrAllAfterTheQuestionsStage', () => {
    response = of(detail({
      status: 'ReadyForReview', stage: 'Review',
      tests: [
        test({ id: 7, order: 1, status: 'Failed', error: 'connection reset' }),
        test({ id: 8, order: 2, testNo: 2, status: 'Failed' }),
      ],
    }));
    setup();

    expect(el().querySelector('.bid-attention')!.textContent).toContain(tr.detail.testFailed);
    (el().querySelector('.bid-attention__retry') as HTMLButtonElement).click();
    expect(api.retryTest).toHaveBeenCalledWith('a1', 7);
    const all = Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.includes('Hatalı testleri yeniden dene (2)'));
    all!.click();
    expect(api.retryFailedTests).toHaveBeenCalledWith('a1');
  });

  it('cancel_AsksToKeepOrDeleteDrafts', () => {
    response = of(detail({
      status: 'Running', stage: 'Questions',
      tests: [test({ id: 7, status: 'Done', worksheetId: 30 }), test({ id: 8, order: 2, status: 'Running' })],
    }));
    setup();
    const button = (label: string) =>
      Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim().endsWith(label)) as HTMLButtonElement;

    button(tr.detail.cancelImport).click();
    fixture.detectChanges();
    expect(el().querySelector('.bid-confirm')!.textContent).toContain(tr.detail.cancelConfirmDrafts);
    button(tr.detail.cancelDeleteDrafts).click();
    expect(api.cancel).toHaveBeenCalledWith('a1', true);
  });

  it('cancel_IsNotOfferedInReview', () => {
    response = of(detail({ status: 'ReadyForReview', stage: 'Review' }));
    setup();

    expect(text()).not.toContain(tr.detail.cancelImport);
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

  it('demo_ShowsBadgeNoRetryAndPlansAtTheEnd (#1032)', () => {
    roles = ['Teacher'];
    response = of(detail({ isDemo: true, status: 'Failed', error: 'x' }));
    setup();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain(tr.demo.badge);
    expect(el.textContent).not.toContain(tr.detail.retry);
    expect(el.textContent).not.toContain(tr.demo.setSource);
  });

  it('demo_ReadyForReview_OffersPlans (#1032)', () => {
    roles = ['Teacher'];
    response = of(detail({ isDemo: true, status: 'ReadyForReview', stage: 'Review' }));
    setup();
    expect((fixture.nativeElement as HTMLElement).querySelector('a[href="/plans"]')).not.toBeNull();
  });

  it('admin_CanMarkAFinishedImportAsDemoSource (#1032)', () => {
    roles = ['Admin'];
    response = of(detail({ status: 'ReadyForReview', stage: 'Review' }));
    setup();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(tr.demo.setSource);
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
