// Issue #583: öğretmenin açık uçlu cevap değerlendirme kuyruğu — liste, filtre, değerlendir / düzelt, 409 akışı, XSS.
import type { Mock } from 'vitest';
import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { ActivatedRoute, ParamMap, Router, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, of, throwError } from 'rxjs';

import { OpenTextReviewItem, OpenTextReviewPage } from '../../models/open-text';
import { OpenTextReviewService } from '../../services/open-text-review.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import scopeTr from '../../../../public/i18n/open-text-reviews/tr.json';
import { OpenTextReviewsComponent } from './open-text-reviews.component';

function item(overrides: Partial<OpenTextReviewItem> = {}): OpenTextReviewItem {
  return {
    instanceQuestionId: 501,
    instanceId: 42,
    worksheetId: 9,
    worksheetName: 'Okuma etkinliği',
    questionId: 7,
    itemOrder: 7,
    itemNumber: 2,
    questionText: null,
    questionImageUrl: 'https://cdn.example.test/q7.webp?sig=abc',
    studentId: 3,
    studentUserId: 33,
    studentNumber: '1203',
    gradeName: '5-A',
    text: 'Birinci satır\nİkinci satır',
    answerHash: 'hash-1',
    wordCount: 4,
    minWords: 3,
    maxWords: 50,
    sampleAnswer: 'Örnek cevap',
    rubric: 'İki neden',
    answeredAt: '2026-10-27T09:00:00Z',
    status: 'pending',
    review: null,
    ...overrides,
  };
}

function page(items: OpenTextReviewItem[], totalCount = items.length): OpenTextReviewPage {
  return { pageNumber: 1, pageSize: 20, totalCount, items };
}

describe('OpenTextReviewsComponent (#583)', () => {
  let fixture: ComponentFixture<OpenTextReviewsComponent>;
  let service: { list: Mock; review: Mock };
  let queryParams: BehaviorSubject<ParamMap>;
  let router: Router;
  let snackOpen: Mock;

  const el = () => fixture.nativeElement as HTMLElement;
  const q = (id: string, root: ParentNode = el()) => root.querySelector(`[data-testid="${id}"]`) as HTMLElement | null;
  const cards = () => Array.from(el().querySelectorAll<HTMLElement>('[data-testid="otq-item"]'));

  async function create(
    first: OpenTextReviewPage | { error: unknown } = page([item()]),
    params: Record<string, string> = {}
  ): Promise<void> {
    service = {
      list: vi.fn().mockReturnValue('error' in first ? throwError(() => first.error) : of(first)),
      review: vi.fn(),
    };
    queryParams = new BehaviorSubject(convertToParamMap(params));
    TestBed.configureTestingModule({
      imports: [
        OpenTextReviewsComponent,
        NoopAnimationsModule,
        translocoTestingModule({ langs: { 'open-text-reviews/tr': scopeTr } }),
      ],
      providers: [
        provideRouter([]),
        { provide: OpenTextReviewService, useValue: service },
        { provide: ActivatedRoute, useValue: { queryParamMap: queryParams.asObservable() } },
      ],
    });
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    snackOpen = vi.spyOn(TestBed.inject(MatSnackBar), 'open').mockReturnValue(undefined as never) as unknown as Mock;
    fixture = TestBed.createComponent(OpenTextReviewsComponent);
    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function chooseEvaluation(card: HTMLElement, evaluation: string) {
    const toggle = card.querySelector(`mat-button-toggle[data-evaluation="${evaluation}"] button`) as HTMLButtonElement;
    toggle.click();
    fixture.detectChanges();
  }

  function typeFeedback(card: HTMLElement, text: string) {
    const field = q('otq-feedback', card) as HTMLTextAreaElement;
    field.value = text;
    field.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  afterEach(() => vi.restoreAllMocks());

  it('load_ListsPendingAnswersWithMetaImageTextWordsAndSolution', async () => {
    await create();

    expect(service.list).toHaveBeenCalledWith({ status: 'pending', worksheetId: null, page: 1, pageSize: 20 });
    const [card] = cards();
    expect(card.textContent).toContain('1203');
    expect(card.textContent).toContain('5-A');
    expect(q('otq-activity', card)?.textContent).toBe('Okuma etkinliği · Madde 2');
    const img = card.querySelector('img')!;
    expect(img.getAttribute('src')).toBe('https://cdn.example.test/q7.webp?sig=abc');
    expect(img.getAttribute('alt')).toBe('Madde 2 görseli');
    expect(fixture.componentInstance.imageRefresh).toEqual(expect.any(Function));
    expect(q('otq-rubric', card)).not.toBeNull();
    expect(q('otq-text', card)?.textContent).toBe('Birinci satır\nİkinci satır');
    expect(q('otq-words', card)?.textContent).toContain('4 sözcük');
    expect(q('otq-words', card)?.textContent).toContain('en az 3, en çok 50');
    expect(card.textContent).toContain('Örnek cevap ve rubrik');
    expect((q('otq-save', card) as HTMLButtonElement).disabled).toBe(true);
    expect(card.textContent).not.toMatch(/%|puan|XP/i);
  });

  it('xss_StudentTextAndFeedbackRenderAsPlainText', async () => {
    const payload = '<script>window.__xss = 1</script><img src=x onerror="window.__xss=2">';
    await create(
      page([
        item({
          text: payload,
          status: 'outdated',
          review: { evaluation: 'partial', feedback: '<b>kalın</b>', source: 'teacher', reviewedByUserId: 5, reviewedAt: '2026-10-27T08:00:00Z', stale: true },
        }),
      ])
    );

    const card = cards()[0];
    expect(q('otq-text', card)?.textContent).toBe(payload);
    expect(card.querySelector('script')).toBeNull();
    expect(card.querySelector('img[src="x"]')).toBeNull();
    expect(q('ot-feedback', card)?.textContent).toContain('<b>kalın</b>');
    expect(q('ot-feedback', card)?.querySelector('b')).toBeNull();
    expect((window as unknown as { __xss?: number }).__xss).toBeUndefined();
    // Eski metne ait değerlendirme öğretmene notla gösterilir.
    expect(q('ot-outdated', card)?.textContent).toContain('önceki metnine ait');
  });

  it('emptyState', async () => {
    await create(page([]));
    expect(q('otq-empty')?.textContent).toContain('Değerlendirme bekleyen cevap yok.');
  });

  it('errorState_RetryReloads', async () => {
    await create({ error: new HttpErrorResponse({ status: 500, error: { message: 'Sunucu hatası' } }) });
    expect(q('otq-error')?.textContent).toContain('Sunucu hatası');
    service.list.mockReturnValue(of(page([item()])));
    (q('otq-error')!.querySelector('button') as HTMLButtonElement).click();
    await settle();
    expect(cards()).toHaveLength(1);
  });

  it('filter_StatusAndWorksheetFromQueryParams', async () => {
    await create(page([item()]), { worksheetId: '9' });
    expect(service.list).toHaveBeenLastCalledWith({ status: 'pending', worksheetId: 9, page: 1, pageSize: 20 });
    expect(q('otq-worksheet-filter')?.textContent).toContain('Etkinlik: Okuma etkinliği');

    (el().querySelector('[data-testid="otq-status-reviewed"] button') as HTMLButtonElement).click();
    expect(router.navigate).toHaveBeenCalledWith([], expect.objectContaining({ queryParams: { status: 'reviewed' }, queryParamsHandling: 'merge' }));

    queryParams.next(convertToParamMap({ worksheetId: '9', status: 'reviewed' }));
    await settle();
    expect(service.list).toHaveBeenLastCalledWith({ status: 'reviewed', worksheetId: 9, page: 1, pageSize: 20 });
    expect(q('otq-empty')).toBeNull();

    (q('otq-worksheet-filter')!.querySelector('button') as HTMLButtonElement).click();
    expect(router.navigate).toHaveBeenLastCalledWith([], expect.objectContaining({ queryParams: { worksheetId: null } }));
  });

  it('noItemNumber_ShowsWorksheetNameOnly_NullRubricHidden', async () => {
    await create(page([item({ itemNumber: undefined, rubric: null })]));
    const card = cards()[0];
    expect(q('otq-activity', card)?.textContent).toBe('Okuma etkinliği');
    expect(q('otq-rubric', card)).toBeNull();
    expect(q('otq-sample', card)).not.toBeNull();
  });

  it('review_SendsEvaluationFeedbackAndHash_ThenReloadsCurrentPage', async () => {
    await create(page([item(), item({ instanceQuestionId: 502, answerHash: 'hash-2' })], 25));
    fixture.componentInstance.onPage({ pageIndex: 1, pageSize: 20, length: 25 });
    await settle();
    service.review.mockReturnValue(
      of({ success: true, message: '', item: item({ status: 'reviewed', review: { evaluation: 'sufficient', feedback: 'Güzel', source: 'teacher', reviewedByUserId: 5, reviewedAt: '2026-10-27T10:00:00Z', stale: false } }) })
    );
    service.list.mockReturnValue(of(page([item({ instanceQuestionId: 502, answerHash: 'hash-2' }), item({ instanceQuestionId: 503 })], 24)));
    const card = cards()[0];
    chooseEvaluation(card, 'sufficient');
    typeFeedback(card, '  Güzel  ');
    (q('otq-save', card) as HTMLButtonElement).click();
    await settle();

    expect(service.review).toHaveBeenCalledWith(501, { evaluation: 'sufficient', feedback: 'Güzel', answerHash: 'hash-1' });
    expect(snackOpen).toHaveBeenCalledWith('Değerlendirme kaydedildi.', 'Tamam', expect.any(Object));
    // Sayfa kaymasın diye satır yerelde çıkarılmaz: aynı sayfa sunucudan yeniden okunur.
    expect(service.list).toHaveBeenLastCalledWith({ status: 'pending', worksheetId: null, page: 2, pageSize: 20 });
    expect(cards().map((c) => c.getAttribute('data-id'))).toEqual(['502', '503']);
    expect(fixture.componentInstance.totalCount()).toBe(24);
  });

  it('reviewedRow_FormPrefilled_UpdateEnabledOnlyAfterChange_RowReplaced', async () => {
    const reviewed = item({
      status: 'reviewed',
      review: { evaluation: 'partial', feedback: 'Eksik nokta var', source: 'teacher', reviewedByUserId: 5, reviewedAt: '2026-10-27T10:00:00Z', stale: false },
    });
    await create(page([reviewed]), { status: 'reviewed' });
    const card = cards()[0];
    expect(q('ot-evaluation', card)?.textContent).toContain('Kısmen');
    expect((q('otq-feedback', card) as HTMLTextAreaElement).value).toBe('Eksik nokta var');
    const save = () => q('otq-save', cards()[0]) as HTMLButtonElement;
    expect(save().disabled).toBe(true);
    expect(save().textContent).toContain('Değerlendirmeyi güncelle');

    chooseEvaluation(card, 'sufficient');
    expect(save().disabled).toBe(false);
    service.review.mockReturnValue(
      of({ success: true, message: '', item: { ...reviewed, review: { ...reviewed.review!, evaluation: 'sufficient' } } })
    );
    service.list.mockReturnValue(of(page([{ ...reviewed, review: { ...reviewed.review!, evaluation: 'sufficient' } }])));
    save().click();
    await settle();

    expect(service.review).toHaveBeenCalledWith(501, { evaluation: 'sufficient', feedback: 'Eksik nokta var', answerHash: 'hash-1' });
    expect(service.list).toHaveBeenCalledTimes(2);
    expect(cards()).toHaveLength(1);
    expect(q('ot-evaluation', cards()[0])?.textContent).toContain('Yeterli');
  });

  it('answerChanged409_ShowsMessageAndReloads', async () => {
    await create();
    service.review.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { success: false, errorCode: 'OpenTextAnswerChanged', message: 'Cevap değişti.', conflict: true },
          })
      )
    );
    service.list.mockReturnValue(of(page([item({ text: 'Yeni metin', answerHash: 'hash-9' })])));
    const card = cards()[0];
    chooseEvaluation(card, 'insufficient');
    (q('otq-save', card) as HTMLButtonElement).click();
    await settle();

    expect(snackOpen).toHaveBeenCalledWith('Öğrencinin cevabı değişti; liste yenilendi.', 'Tamam', expect.any(Object));
    expect(service.list).toHaveBeenCalledTimes(2);
    expect(q('otq-text', cards()[0])?.textContent).toBe('Yeni metin');
    // Öğretmenin seçimi korunur; yeni özetle yeniden gönderilebilir.
    expect((q('otq-save', cards()[0]) as HTMLButtonElement).disabled).toBe(false);
  });

  it('otherErrors_ShowServerMessage', async () => {
    await create();
    service.review.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { success: false, errorCode: 'OpenTextInvalidReview', message: 'Geri bildirim en çok 1000 karakter.', invalidInput: true },
          })
      )
    );
    const card = cards()[0];
    chooseEvaluation(card, 'partial');
    (q('otq-save', card) as HTMLButtonElement).click();
    await settle();

    expect(snackOpen).toHaveBeenCalledWith('Geri bildirim en çok 1000 karakter.', 'Tamam', expect.any(Object));
    expect(service.list).toHaveBeenCalledTimes(1);
    expect(cards()).toHaveLength(1);
  });

  it('canEditFalse_FormReadOnlyWithNote_NoSaveButton', async () => {
    await create(
      page([
        item({
          status: 'reviewed',
          canEdit: false,
          review: { evaluation: 'partial', feedback: 'Başka öğretmen', source: 'teacher', reviewedByUserId: 9, reviewedAt: '2026-10-27T10:00:00Z', stale: false },
        }),
      ]),
      { status: 'reviewed' }
    );
    const card = cards()[0];
    expect(q('otq-locked', card)?.textContent).toContain('Başka bir öğretmen değerlendirdi');
    expect(q('otq-save', card)).toBeNull();
    expect((q('otq-feedback', card) as HTMLTextAreaElement).readOnly).toBe(true);
    expect((card.querySelector('mat-button-toggle[data-evaluation="sufficient"] button') as HTMLButtonElement).disabled).toBe(true);
    fixture.componentInstance.save(fixture.componentInstance.items()[0]);
    expect(service.review).not.toHaveBeenCalled();
  });

  it('forbidden403_ShowsMessageAndReloads', async () => {
    await create();
    service.review.mockReturnValue(
      throwError(
        () => new HttpErrorResponse({ status: 403, error: { success: false, errorCode: 'OpenTextReviewForbidden', message: 'Yetkin yok.' } })
      )
    );
    chooseEvaluation(cards()[0], 'partial');
    (q('otq-save', cards()[0]) as HTMLButtonElement).click();
    await settle();

    expect(snackOpen).toHaveBeenCalledWith('Yetkin yok.', 'Tamam', expect.any(Object));
    expect(service.list).toHaveBeenCalledTimes(2);
  });

  it('list429_ShowsSnackbar_KeepsCurrentList', async () => {
    await create();
    service.list.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 429, error: { success: false, errorCode: 'RateLimited', message: 'Çok sık istek.' } }))
    );
    (q('otq-refresh') as HTMLButtonElement).click();
    await settle();

    expect(snackOpen).toHaveBeenCalledWith('Çok sık istek.', 'Tamam', expect.any(Object));
    expect(q('otq-error')).toBeNull();
    expect(cards()).toHaveLength(1);
  });

  it('feedback_StripsBidiAndC1Characters', async () => {
    await create();
    const card = cards()[0];
    typeFeedback(card, 'iyi\u202E\u2066 iş\u0085');
    expect(fixture.componentInstance.draftOf(fixture.componentInstance.items()[0]).feedback).toBe('iyi iş');
    expect((q('otq-feedback', card) as HTMLTextAreaElement).value).toBe('iyi iş');
  });

  it('feedback_LimitedTo1000Chars', async () => {
    await create();
    const card = cards()[0];
    expect(q('otq-feedback', card)?.getAttribute('maxlength')).toBe('1000');
    typeFeedback(card, 'x'.repeat(1200));
    expect(fixture.componentInstance.draftOf(fixture.componentInstance.items()[0]).feedback).toHaveLength(1000);
  });
});
