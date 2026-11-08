import type { MockedObject } from 'vitest';
import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';

import { QUICK_ADD_DETAIL_POLL_MS, QuickAddDetailComponent } from './quick-add-detail.component';
import { BookImportJobDetail, BookImportJobTest } from '../../models/book-import';
import { QuickAddService } from '../../services/quick-add.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import tr from '../../../../public/i18n/quick-add/tr.json';

function photo(order: number, overrides: Partial<BookImportJobTest> = {}): BookImportJobTest {
  return {
    id: 100 + order, order, unit: 0, testNo: order, title: `Fotoğraf ${order}`, status: 'Done', questionsFound: 3,
    keyAnswers: 0, flags: [], questionsSaved: 3, duplicatesSkipped: 0, reviewFlags: [], worksheetId: 9, fileFrom: order,
    fileTo: order, answerless: 3, costUsd: null, error: null, ...overrides,
  };
}

function detail(overrides: Partial<BookImportJobDetail> = {}): BookImportJobDetail {
  return {
    id: 'q1', bookName: 'Hızlı ekleme', gradeId: 4, subjectId: null, bookId: null,
    status: 'ReadyForReview', stage: 'Review', fileName: null, fileSizeBytes: 1000, pageCount: 2,
    stageTotal: 2, stageProcessed: 2, message: null, error: null, createdAt: '2026-11-08T08:00:00Z',
    startedAt: '2026-11-08T08:00:05Z', finishedAt: null, ownerId: 3, resumeAt: null, stalled: false, aiCostUsd: null,
    costCeilingUsd: null, tests: [photo(1), photo(2)], costs: [], ...overrides,
  };
}

describe('QuickAddDetailComponent (#1053)', () => {
  let fixture: ComponentFixture<QuickAddDetailComponent>;
  let component: QuickAddDetailComponent;
  let api: MockedObject<QuickAddService>;
  let response: Observable<BookImportJobDetail>;

  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => el().textContent ?? '';

  function setup(): void {
    api = {
      get: vi.fn().mockName('get').mockImplementation(() => response),
      photo: vi.fn().mockName('photo').mockReturnValue(of(new Blob(['x'], { type: 'image/jpeg' }))),
      retry: vi.fn().mockName('retry').mockReturnValue(of(undefined)),
    } as unknown as MockedObject<QuickAddService>;
    TestBed.configureTestingModule({
      imports: [QuickAddDetailComponent, translocoTestingModule({ langs: { 'quick-add/tr': tr } })],
      providers: [
        provideNoopAnimations(),
        provideRouter([]),
        { provide: QuickAddService, useValue: api },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: 'q1' }) } } },
      ],
    });
    fixture = TestBed.createComponent(QuickAddDetailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(() => (response = of(detail())));
  afterEach(() => vi.useRealTimers());

  it('ready_ShowsSavedTotalAndPerPhotoResults', () => {
    setup();

    expect(api.get).toHaveBeenCalledWith('q1');
    expect(el().querySelector('.qa-status')?.textContent).toContain(tr.phase.ready);
    expect(text()).toContain('6 soru taslak olarak kaydedildi');
    const rows = el().querySelectorAll('.qa-result');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('3 soru bulundu');
    expect(api.photo).toHaveBeenCalledWith('q1', 1);
    expect(api.photo).toHaveBeenCalledWith('q1', 2);
    expect(el().querySelector('.qa-result img')).not.toBeNull();
    // Bitmiş işte yeniden deneme yok; yeni fotoğraf bağlantısı var.
    expect(text()).not.toContain(tr.detail.retry);
    expect(text()).toContain(tr.detail.another);
  });

  it('countMismatch_ShowsFlagWithCounts', () => {
    response = of(detail({
      tests: [photo(1, {
        reviewFlags: [
          { code: 'countMismatch', pages: [1], numbers: null, found: 4, expected: 5 },
          { code: 'noAnswer', pages: [1], numbers: [1, 2], found: null, expected: null },
          { code: 'somethingNew', pages: [1], numbers: null, found: null, expected: null },
        ],
      })],
    }));
    setup();

    const flags = el().querySelectorAll('.qa-result__flag');
    expect(flags[0].textContent).toContain('cevap sayısı (5) bulunan soru sayısıyla (4)');
    expect(flags[1].textContent).toContain(tr.flags.noAnswer);
    expect(flags[2].textContent).toContain(tr.flags.other);
  });

  it('processing_ShowsProgressAndPollsUntilReady', async () => {
    vi.useFakeTimers();
    response = of(detail({
      status: 'Running', stage: 'Questions', stageProcessed: 1,
      tests: [photo(1), photo(2, { status: 'Running', questionsFound: 0, questionsSaved: 0 })],
    }));
    setup();

    expect(el().querySelector('.qa-status')?.textContent).toContain(tr.phase.processing);
    expect(text()).toContain('1/2 fotoğraf');
    expect(el().querySelectorAll('.qa-result')[1].textContent).toContain(tr.detail.pending);
    expect(el().querySelector('mat-progress-bar')).not.toBeNull();

    response = of(detail());
    await vi.advanceTimersByTimeAsync(QUICK_ADD_DETAIL_POLL_MS);
    fixture.detectChanges();
    expect(api.get).toHaveBeenCalledTimes(2);
    expect(el().querySelector('.qa-status')?.textContent).toContain(tr.phase.ready);
    // Fotoğraflar bir kez çekilir.
    expect(api.photo).toHaveBeenCalledTimes(2);

    await vi.advanceTimersByTimeAsync(QUICK_ADD_DETAIL_POLL_MS * 2);
    expect(api.get).toHaveBeenCalledTimes(2);
  });

  it('failed_RetryRequeuesAndResumesPolling', async () => {
    vi.useFakeTimers();
    response = of(detail({ status: 'Failed', tests: [photo(1, { status: 'Failed' })] }));
    setup();

    expect(el().querySelector('.qa-status')?.textContent).toContain(tr.phase.failed);
    expect(text()).toContain(tr.detail.photoFailed);
    const retry = Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.includes(tr.detail.retry))!;
    retry.click();
    fixture.detectChanges();

    expect(api.retry).toHaveBeenCalledWith('q1');
    expect(el().querySelector('.qa-status')?.textContent).toContain(tr.phase.queued);
    response = of(detail());
    await vi.advanceTimersByTimeAsync(QUICK_ADD_DETAIL_POLL_MS);
    expect(api.get).toHaveBeenCalledTimes(2);
  });

  it('stalled_ShowsRetryWithoutPolling', async () => {
    vi.useFakeTimers();
    response = of(detail({ status: 'Running', stalled: true }));
    setup();

    expect(text()).toContain(tr.detail.stalled);
    expect(text()).toContain(tr.detail.retry);
    await vi.advanceTimersByTimeAsync(QUICK_ADD_DETAIL_POLL_MS * 2);
    expect(api.get).toHaveBeenCalledTimes(1);
  });

  it('notFound_ShowsMessage', () => {
    response = throwError(() => new HttpErrorResponse({ status: 404 }));
    setup();

    expect(text()).toContain(tr.detail.notFound);
    expect(component.job()).toBeNull();
  });
});
