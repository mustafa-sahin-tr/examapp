import type { MockedObject } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';

import { BookImportDetailComponent } from './book-import-detail.component';
import { BOOK_IMPORTS_POLL_MS } from './book-imports.component';
import { BookImportJobDetail } from '../../models/book-import';
import { BookImportService } from '../../services/book-import.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import tr from '../../../../public/i18n/book-imports/tr.json';

function detail(overrides: Partial<BookImportJobDetail> = {}): BookImportJobDetail {
  return {
    id: 'a1', bookName: 'Puan BTS 4 Matematik', gradeId: 4, subjectId: null, bookId: null,
    status: 'Running', stage: 'Setup', fileName: 'bts.pdf', fileSizeBytes: 1000, pageCount: 120,
    stageTotal: 4, stageProcessed: 1, message: null, error: null, createdAt: '2026-11-08T08:00:00Z',
    startedAt: '2026-11-08T08:00:05Z', finishedAt: null, ownerId: 3, tests: [], ...overrides,
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

  it('awaitingConfirmation_MarksStepWaitingAndShowsBanner', () => {
    response = of(detail({ status: 'AwaitingConfirmation', stage: 'SetupConfirmation', stageTotal: 0, stageProcessed: 0 }));
    setup();

    expect(steps()[3].classList).toContain('bid-step--waiting');
    expect(text()).toContain(tr.detail.awaitingConfirmation);
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
        { order: 1, unit: 1, testNo: 1, title: null, status: 'Done', questionsFound: 10, keyAnswers: 10, flags: [], error: null },
        { order: 2, unit: 1, testNo: 2, title: null, status: 'NeedsReview', questionsFound: 9, keyAnswers: 10, flags: ['keyMismatch'], error: null },
        { order: 3, unit: 2, testNo: 1, title: null, status: 'Failed', questionsFound: 0, keyAnswers: 0, flags: [], error: 'Sayfa okunamadı' },
      ],
    }));
    setup();

    const cells = el().querySelectorAll('.bid-grid__cell');
    expect(cells.length).toBe(3);
    expect(cells[1].getAttribute('aria-label')).toBe('Ünite 1, test 2: Kontrol gerekli');
    expect(cells[2].classList).toContain('bid-grid__cell--failed');
    const attention = el().querySelector('.bid-attention')!.textContent ?? '';
    expect(attention).toContain('Ünite 1 · Test 2');
    expect(attention).toContain(tr.flags.keyMismatch);
    expect(attention).toContain('Sayfa okunamadı');
    expect(attention).not.toContain('Ünite 1 · Test 1');
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
