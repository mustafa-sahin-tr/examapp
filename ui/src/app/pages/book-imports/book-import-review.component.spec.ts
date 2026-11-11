import type { MockedObject } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';

import { BookImportReviewComponent } from './book-import-review.component';
import { BookImportJobTest, BookImportReviewApi, BookImportReviewQuestion } from '../../models/book-import';
import { BookImportService } from '../../services/book-import.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import tr from '../../../../public/i18n/book-imports/tr.json';

function test(overrides: Partial<BookImportJobTest> = {}): BookImportJobTest {
  return {
    id: 1, order: 1, unit: 1, testNo: 1, title: 'Kesirler', status: 'Done', questionsFound: 4, keyAnswers: 4, flags: [],
    questionsSaved: 4, duplicatesSkipped: 0, reviewFlags: [], worksheetId: 100, fileFrom: 3, fileTo: 4, answerless: 0,
    costUsd: null, error: null, ...overrides,
  };
}

function question(id: number, answerIndex: number | null): BookImportReviewQuestion {
  return { id, number: String(id), filePage: 3, optionCount: 4, imageUrl: `data:image/gif;base64,R0lGODlhAQABAAAAACw=#${id}`, answerIndex };
}

describe('BookImportReviewComponent (#1023)', () => {
  let fixture: ComponentFixture<BookImportReviewComponent>;
  let api: MockedObject<BookImportService>;
  let changed: number;

  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => el().textContent ?? '';
  const rows = () => Array.from(el().querySelectorAll('.bir-row'));
  const button = (root: Element, label: string) =>
    Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.includes(label)) as HTMLButtonElement;

  function setup(tests: BookImportJobTest[], editable = true): void {
    api = {
      reviewQuestions: vi.fn().mockReturnValue(of([question(1, 0), question(2, null)])),
      setAnswer: vi.fn().mockReturnValue(of(undefined)),
      approveTest: vi.fn().mockReturnValue(of(undefined)),
      approveAll: vi.fn().mockReturnValue(of({ approved: [1], skipped: [{ id: 2, reason: 'answerless' }] })),
      removeTest: vi.fn().mockReturnValue(of(undefined)),
    } as unknown as MockedObject<BookImportService>;
    TestBed.configureTestingModule({
      imports: [BookImportReviewComponent, translocoTestingModule({ langs: { 'book-imports/tr': tr } })],
      providers: [provideNoopAnimations(), provideRouter([]), { provide: BookImportService, useValue: api }],
    });
    fixture = TestBed.createComponent(BookImportReviewComponent);
    fixture.componentRef.setInput('jobId', 'a1');
    fixture.componentRef.setInput('tests', tests);
    fixture.componentRef.setInput('editable', editable);
    changed = 0;
    fixture.componentInstance.changed.subscribe(() => changed++);
    fixture.detectChanges();
  }

  const mixed = () => [
    test(),
    test({ id: 2, order: 2, testNo: 2, title: null, status: 'NeedsReview', answerless: 1,
      reviewFlags: [{ code: 'noAnswer', pages: [5], numbers: [2], found: null, expected: null }] }),
    test({ id: 3, order: 3, testNo: 3, title: null, status: 'Approved' }),
  ];

  it('filter_OnByDefault_ShowsOnlyTestsThatNeedALook', () => {
    setup(mixed());

    expect(rows().length).toBe(1);
    expect(rows()[0].textContent).toContain('1-02');
    expect(rows()[0].textContent).toContain('1 cevapsız');
    expect(text()).toContain('2 test inceleme bekliyor · 1 onaylandı');

    fixture.componentInstance.onlyAttention.set(false);
    fixture.detectChanges();
    expect(rows().length).toBe(2);
  });

  it('approve_IsDisabledWhileAnswersAreMissing', () => {
    setup(mixed());
    fixture.componentInstance.onlyAttention.set(false);
    fixture.detectChanges();

    expect(button(rows()[0], 'Onayla').disabled).toBe(false);
    expect(button(rows()[1], 'Onayla').disabled).toBe(true);

    button(rows()[0], 'Onayla').click();
    expect(api.approveTest).toHaveBeenCalledWith('a1', 1);
    expect(changed).toBe(1);
  });

  it('preview_ListsQuestionsWithCounterAndPicksAnAnswer', () => {
    setup(mixed());
    button(rows()[0], 'Önizle').click();
    fixture.detectChanges();

    expect(api.reviewQuestions).toHaveBeenCalledWith('a1', 2);
    expect(text()).toContain('Cevaplanan 1/2');
    const missing = el().querySelector('.bir-q--missing')!;
    const c = Array.from(missing.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'C')!;
    c.click();
    fixture.detectChanges();

    expect(api.setAnswer).toHaveBeenCalledWith('a1', 2, 2, 2);
    expect(text()).toContain('Cevaplanan 2/2');
    expect(c.getAttribute('aria-pressed')).toBe('true');
    expect(changed).toBe(1);
  });

  it('pick_Failure_RestoresThePreviousAnswerAndShowsTheError', () => {
    setup(mixed());
    api.setAnswer.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409, error: { message: 'Bu test şu anda onaylanamaz.' } })));
    button(rows()[0], 'Önizle').click();
    fixture.detectChanges();
    Array.from(el().querySelectorAll('.bir-q--missing button')).find((b) => b.textContent?.trim() === 'A')!
      .dispatchEvent(new Event('click'));
    fixture.detectChanges();

    expect(text()).toContain('Cevaplanan 1/2');
    expect(text()).toContain('Bu test şu anda onaylanamaz.');
  });

  it('approveAll_AsksFirstThenReportsSkippedTests', () => {
    setup(mixed());
    const bulk = button(el(), 'Sorunsuz testleri onayla');
    expect(bulk.textContent).toContain('(1/2)');
    bulk.click();
    fixture.detectChanges();
    expect(api.approveAll).not.toHaveBeenCalled();
    expect(el().querySelector('[role="alertdialog"]')).not.toBeNull();

    button(el().querySelector('.bir-confirm')!, 'Onayla').click();
    fixture.detectChanges();

    expect(api.approveAll).toHaveBeenCalledWith('a1');
    expect(text()).toContain('1 test onaylandı.');
    expect(text()).toContain('1-02 atlandı: cevapsız soru var');
    expect(changed).toBe(1);
  });

  it('remove_NeedsASecondClick', () => {
    setup(mixed());
    (rows()[0].querySelector('button[aria-label^="1-02"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(api.removeTest).not.toHaveBeenCalled();

    button(rows()[0], 'Testi sil').click();
    expect(api.removeTest).toHaveBeenCalledWith('a1', 2);
  });

  it('fixInLabeling_OpensTheCanvasAtTheFirstFlaggedPage', () => {
    setup([test({ status: 'NeedsReview', reviewFlags: [{ code: 'missingNumbers', pages: [4], numbers: [3], found: null, expected: null }] })]);

    const link = Array.from(rows()[0].querySelectorAll('a')).find((a) => a.textContent?.includes('İşaretlemede düzelt'));
    expect(link?.getAttribute('href')).toBe('/labeling?bookImport=a1&test=1&page=4');
  });

  it('flags_OpenPerTestWithCompactPagesAndNumbers', () => {
    setup([test({
      status: 'NeedsReview', flags: ['keyMismatch'], duplicatesSkipped: 1,
      reviewFlags: [{ code: 'extraQuestions', pages: [69, 70, 71, 72, 75], numbers: [114, 116], found: null, expected: null }],
    })]);
    expect(rows()[0].querySelector('.bir-flags')).toBeNull();

    button(rows()[0], 'Uyarılar (3)').click();
    fixture.detectChanges();

    const flags = rows()[0].querySelector('.bir-flags')!.textContent ?? '';
    expect(flags).toContain(tr.flags.keyMismatch);
    expect(flags).toContain('1 tekrar atlandı');
    expect(flags).toContain('sayfa 69–72, 75 · soru 114, 116');
  });

  it('flags_GroupByPageAndQuestionOnce', () => {
    setup([test({
      status: 'NeedsReview',
      reviewFlags: [
        { code: 'optionCount', pages: [9, 69], numbers: [4, 112, 114], found: null, expected: null, byPage: { 9: [4], 69: [112, 114] } },
        { code: 'extraQuestions', pages: [69], numbers: [114], found: null, expected: null, byPage: { 69: [114] } },
        { code: 'notSaved', pages: [69], numbers: [112, 114], found: null, expected: null, byPage: { 69: [112, 114] } },
        { code: 'countMismatch', pages: [8, 9, 10], numbers: null, found: 155, expected: 154 },
      ],
    })]);
    button(rows()[0], 'Uyarılar').click();
    fixture.detectChanges();

    const pages = Array.from(rows()[0].querySelectorAll('.bir-flags__page')).map((p) =>
      Array.from(p.children).map((c) => c.textContent!.replace(/\s+/g, ' ').trim()));
    expect(pages).toEqual([
      ['Sayfa 9', '4: şık sayısı 4 değil'],
      ['Sayfa 69', '112: kaydedilmedi (ikiden az şık)', '114: fazla kutu, kaydedilmedi (ikiden az şık)'],
    ]);
    const flags = rows()[0].querySelector('.bir-flags')!.textContent ?? '';
    expect(flags).toContain('155 soru bulundu, anahtarda 154');
    expect(flags).not.toContain('sayfa 8–10');
  });

  it('preview_ZoomsAQuestionAndWalksWithArrows', () => {
    setup(mixed());
    button(rows()[0], 'Önizle').click();
    fixture.detectChanges();

    (el().querySelector('.bir-q__zoom') as HTMLButtonElement).click();
    fixture.detectChanges();
    const zoom = () => el().querySelector('[data-testid="review-zoom"]') as HTMLElement | null;
    expect(zoom()!.textContent).toContain('1 / 2');
    expect(zoom()!.querySelector('img')!.getAttribute('src')).toBe(question(1, 0).imageUrl);

    zoom()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    fixture.detectChanges();
    expect(zoom()!.textContent).toContain('2 / 2');
    const c = Array.from(zoom()!.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'C')!;
    c.click();
    fixture.detectChanges();
    expect(api.setAnswer).toHaveBeenCalledWith('a1', 2, 2, 2);

    zoom()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(zoom()).toBeNull();
  });

  it('locked_ShowsActionsDisabledUntilTheImportEnds', () => {
    setup([test({ status: 'NeedsReview', reviewFlags: [{ code: 'noAnswer', pages: [3], numbers: [1], found: null, expected: null }] })], false);
    fixture.componentRef.setInput('locked', true);
    fixture.detectChanges();

    expect(button(rows()[0], 'Onayla').disabled).toBe(true);
    expect(button(rows()[0], 'Önizle').disabled).toBe(false);
  });

  it('completedJob_IsReadOnly', () => {
    setup([test({ status: 'Approved' })], false);

    expect(text()).toContain('İncelenecek test kalmadı.');
    expect(button(el(), 'Sorunsuz testleri onayla')).toBeUndefined();
    expect(el().textContent).not.toContain('İşaretlemede düzelt');
  });

  // Issue #1054: hızlı ekleme aynı ekranı kullanır.
  describe('quick add', () => {
    let quick: { [K in keyof BookImportReviewApi]: ReturnType<typeof vi.fn> };
    const photo = (overrides: Partial<BookImportJobTest> = {}) =>
      test({ unit: 0, title: 'Fotoğraf 1', fileFrom: 1, fileTo: 1, ...overrides });

    function setupQuick(tests: BookImportJobTest[], canFix: boolean): void {
      quick = {
        reviewQuestions: vi.fn().mockReturnValue(of([question(1, 2)])),
        setAnswer: vi.fn().mockReturnValue(of(undefined)),
        approveTest: vi.fn().mockReturnValue(of(undefined)),
        approveAll: vi.fn().mockReturnValue(of({ approved: [1], skipped: [] })),
        removeTest: vi.fn().mockReturnValue(of(undefined)),
      };
      TestBed.configureTestingModule({
        imports: [BookImportReviewComponent, translocoTestingModule({ langs: { 'book-imports/tr': tr } })],
        providers: [provideNoopAnimations(), provideRouter([]), { provide: BookImportService, useValue: {} }],
      });
      fixture = TestBed.createComponent(BookImportReviewComponent);
      fixture.componentRef.setInput('jobId', 'q1');
      fixture.componentRef.setInput('tests', tests);
      fixture.componentRef.setInput('kind', 'QuickAdd');
      fixture.componentRef.setInput('reviewApi', quick);
      fixture.componentRef.setInput('canFix', canFix);
      fixture.componentRef.setInput('thumbs', { 1: 'blob:thumb' });
      fixture.detectChanges();
    }

    it('lists every photo with its thumbnail and photo wording', () => {
      setupQuick([photo()], false);

      expect(rows().length).toBe(1);
      expect(text()).toContain('Fotoğraf 1');
      expect(text()).toContain(tr.reviewQuickAdd.footer);
      expect(text()).not.toContain('sayfa 1–1');
      expect(el().querySelector('img.bir-row__thumb')?.getAttribute('src')).toBe('blob:thumb');
    });

    it('uses the quick add endpoints', () => {
      setupQuick([photo()], false);

      button(rows()[0], tr.review.approve).click();

      expect(quick.approveTest).toHaveBeenCalledWith('q1', 1);
    });

    it('shows answers from the key as selected and lets them be changed', () => {
      setupQuick([photo()], false);
      button(rows()[0], tr.review.preview).click();
      fixture.detectChanges();

      const answers = () => Array.from(el().querySelectorAll<HTMLButtonElement>('.bir-q__answer'));
      expect(answers()[2].getAttribute('aria-pressed')).toBe('true');

      answers()[0].click();
      fixture.detectChanges();

      expect(quick.setAnswer).toHaveBeenCalledWith('q1', 1, 1, 0);
      expect(answers()[0].getAttribute('aria-pressed')).toBe('true');
    });

    it('hides the labeling fix from teachers', () => {
      setupQuick([photo()], false);
      expect(text()).not.toContain(tr.review.fix);
    });

    it('offers the labeling fix to admins with the quick add kind', () => {
      setupQuick([photo()], true);
      expect(text()).toContain(tr.review.fix);
      expect(fixture.componentInstance.fixParams(1, 1)).toEqual({ bookImport: 'q1', test: 1, page: 1, kind: 'QuickAdd' });
    });
  });

  it('keeps book import labeling links without a kind', () => {
    setup([test()]);
    expect(fixture.componentInstance.fixParams(1, 3)).toEqual({ bookImport: 'a1', test: 1, page: 3 });
  });
});
