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

  function setup(tests: BookImportJobTest[], editable = true, extra: Record<string, unknown> = {}): void {
    api = {
      ...extra,
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

  it('preview_ActivityDraft_ShowsItsTypeWithoutAnswerButtons_AndIsNotCountedAsAnswerless (#1134)', () => {
    setup([test({ id: 5, order: 2, unit: 1, testNo: 1, title: null, flags: ['activity'], status: 'Done' })], true);
    api.reviewQuestions.mockReturnValue(of([question(1, 0), { ...question(2, null), number: 'E1', optionCount: 0, interactionType: 'slotFill' }]));
    fixture.componentInstance.onlyAttention.set(false);
    fixture.detectChanges();

    // Etkinlik bölümü "1-E01 · Etkinlikler" adıyla; tür işareti uyarı sayılmaz.
    expect(text()).toContain('1-E01 · Etkinlikler');
    expect(text()).not.toContain('Uyarılar (1)');
    button(rows()[0], 'Önizle').click();
    fixture.detectChanges();

    expect(text()).toContain('Cevaplanan 1/1');
    const draft = el().querySelector('[data-testid="activity-draft"]')!;
    expect(draft.textContent).toContain('Boşluk doldurma');
    expect(el().querySelectorAll('.bir-q--missing').length).toBe(0);
    expect(draft.closest('.bir-q')!.querySelectorAll('.bir-q__answer').length).toBe(0);
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

  it('approveAll_SkipsFlaggedTests_WhichStayApprovableOneByOne', () => {
    setup([test({ id: 1 }), test({ id: 2, order: 2, testNo: 2, status: 'NeedsReview' })]);
    expect(button(el(), 'Sorunsuz testleri onayla').textContent).toContain('(1/2)');
    // Filtre açık: yalnız işaretli test listede; tek başına onaylanabilir.
    expect(rows().length).toBe(1);
    expect(button(rows()[0], 'Onayla').disabled).toBe(false);
  });

  it('approveAll_LooseImages_CanCollectTheQuestionsIntoANewTest (#1144)', () => {
    setup([test({ title: 'Görsel 1', unit: 0, testNo: 1, fileFrom: 1, fileTo: 1 })]);
    api.approveAll.mockReturnValue(of({ approved: [1], skipped: [], collectedTestId: 77 }));
    fixture.componentRef.setInput('loose', true);
    fixture.componentRef.setInput('bookName', 'Karışık sorular');
    fixture.componentInstance.onlyAttention.set(false);
    fixture.detectChanges();
    expect(text()).toContain('Görsel 1');
    expect(text()).not.toContain('sayfa 1');

    button(el(), 'Hazır görselleri onayla').click();
    fixture.detectChanges();
    (el().querySelector('[data-testid="collect-toggle"] input') as HTMLInputElement).click();
    fixture.detectChanges();
    const name = el().querySelector('[data-testid="collect-name"]') as HTMLInputElement;
    expect(name.value).toBe('Karışık sorular');
    name.value = '  Deneme 1  ';
    name.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    button(el().querySelector('.bir-confirm')!, 'Onayla').click();
    fixture.detectChanges();

    expect(api.approveAll).toHaveBeenCalledWith('a1', 'Deneme 1');
    expect(text()).toContain(tr.reviewLoose.approvedAll.replace('{{count}}', '1'));
    expect(el().querySelector('[data-testid="collected-test"]')?.getAttribute('href')).toBe('/test/77');
  });

  it('approveAll_LooseImages_WithoutCollect_SendsNoName (#1144)', () => {
    setup([test({ title: 'Görsel 1' })]);
    fixture.componentRef.setInput('loose', true);
    fixture.detectChanges();
    button(el(), 'Hazır görselleri onayla').click();
    fixture.detectChanges();
    button(el().querySelector('.bir-confirm')!, 'Onayla').click();

    expect(api.approveAll).toHaveBeenCalledWith('a1');
  });

  it('remove_NeedsASecondClick', () => {
    setup(mixed());
    (rows()[0].querySelector('button[aria-label^="1-02"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(api.removeTest).not.toHaveBeenCalled();

    button(rows()[0], 'Testi sil').click();
    expect(api.removeTest).toHaveBeenCalledWith('a1', 2);
  });

  it('removeQuestion_AsksFirstThenDropsTheQuestion (#1169)', () => {
    setup(mixed());
    api.removeQuestion = vi.fn().mockReturnValue(of(undefined));
    button(rows()[0], 'Önizle').click();
    fixture.detectChanges();

    const removes = () => el().querySelectorAll('[data-testid="remove-question"]');
    expect(removes().length).toBe(2);
    (removes()[1] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(api.removeQuestion).not.toHaveBeenCalled();

    (el().querySelector('[data-testid="remove-question-confirm"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(api.removeQuestion).toHaveBeenCalledWith('a1', 2, 2);
    expect(el().querySelectorAll('.bir-q').length).toBe(1);
    expect(text()).toContain('Cevaplanan 1/1');
    expect(changed).toBe(1);
    // Son soru çıkarılamaz: düğme kalkar.
    expect(removes().length).toBe(0);
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

  it('flags_ViewPageOpensTheTestPagesWithoutLeaving (#1190, #1263)', () => {
    const pageImage = vi.fn().mockReturnValue(of(new Blob(['x'], { type: 'image/webp' })));
    setup([test({
      status: 'NeedsReview', fileFrom: 9, fileTo: 11,
      reviewFlags: [
        { code: 'optionCount', pages: [9, 11], numbers: [4, 112], found: null, expected: null, byPage: { 9: [4], 11: [112] } },
      ],
    })], true, { pageImage });
    button(rows()[0], 'Uyarılar').click();
    fixture.detectChanges();

    const viewButtons = () => Array.from(el().querySelectorAll<HTMLButtonElement>('[data-testid="view-page"]'));
    expect(viewButtons().length).toBe(2);
    viewButtons()[0].click();
    fixture.detectChanges();
    const view = () => el().querySelector('[data-testid="page-view"]') as HTMLElement | null;
    const viewText = () => view()!.textContent!.replace(/\s+/g, ' ');
    expect(pageImage).toHaveBeenCalledWith('a1', 9, false);
    expect(view()!.textContent).toContain('Sayfa 9');
    expect(viewText()).toContain('4: şık sayısı 4 değil');
    // #1263: uyarısız sayfa da gezilir.
    expect(view()!.textContent).toContain('1 / 3');
    expect(view()!.querySelector('img')!.getAttribute('src')).toMatch(/^blob:/);
    expect(view()!.querySelector('a[href*="labeling"]')).not.toBeNull();

    view()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    fixture.detectChanges();
    expect(pageImage).toHaveBeenLastCalledWith('a1', 10, false);
    expect(viewText()).not.toContain('112:');
    view()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    fixture.detectChanges();
    expect(pageImage).toHaveBeenLastCalledWith('a1', 11, false);
    expect(view()!.textContent).toContain('112:');

    view()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(view()).toBeNull();
  });

  // #1263: göz düğmesi testin sayfalarını açar; temizlenmiş kitapta orijinal / temiz arasında geçilir.
  it('eye_OpensTheTestPages_AndSwitchesBetweenOriginalAndClean (#1263)', () => {
    const pageImage = vi.fn().mockReturnValue(of(new Blob(['x'], { type: 'image/webp' })));
    setup([test({ status: 'NeedsReview', fileFrom: 3, fileTo: 4 })], true, { pageImage });
    fixture.componentRef.setInput('hasOriginal', true);
    fixture.detectChanges();

    rows()[0].querySelector<HTMLButtonElement>('[data-testid="view-test-pages"]')!.click();
    fixture.detectChanges();
    const view = () => el().querySelector('[data-testid="page-view"]') as HTMLElement | null;
    expect(pageImage).toHaveBeenLastCalledWith('a1', 3, false);
    expect(view()!.textContent).toContain('1 / 2');

    const toggle = () => view()!.querySelector<HTMLButtonElement>('[data-testid="toggle-original"]')!;
    expect(toggle().textContent).toContain(tr.review.showOriginal);
    toggle().click();
    fixture.detectChanges();
    expect(pageImage).toHaveBeenLastCalledWith('a1', 3, true);
    expect(toggle().textContent).toContain(tr.review.showClean);

    view()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    fixture.detectChanges();
    expect(pageImage).toHaveBeenLastCalledWith('a1', 4, true);

    view()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    rows()[0].querySelector<HTMLButtonElement>('[data-testid="view-test-pages"]')!.click();
    fixture.detectChanges();
    expect(pageImage).toHaveBeenLastCalledWith('a1', 3, false);
  });

  it('eye_HiddenWithoutPageImages (#1263)', () => {
    setup([test({ status: 'NeedsReview' })]);
    expect(rows()[0].querySelector('[data-testid="view-test-pages"]')).toBeNull();
  });

  it('browseBook_WalksEveryPageWithTheTestsAndFlagsOfThePage (#1229)', () => {
    const pageImage = vi.fn().mockReturnValue(of(new Blob(['x'], { type: 'image/webp' })));
    setup([
      test({ id: 1, fileFrom: 3, fileTo: 4 }),
      test({ id: 2, order: 2, testNo: 2, title: 'Oranlar', fileFrom: 4, fileTo: 5, status: 'NeedsReview',
        reviewFlags: [{ code: 'optionCount', pages: [5], numbers: [7], found: null, expected: null, byPage: { 5: [7] } }] }),
    ], true, { pageImage });
    fixture.componentRef.setInput('pageCount', 6);
    fixture.detectChanges();

    (el().querySelector('[data-testid="browse-book"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    const view = () => el().querySelector('[data-testid="page-view"]') as HTMLElement | null;
    const viewText = () => view()!.textContent!.replace(/\s+/g, ' ');
    expect(pageImage).toHaveBeenCalledWith('a1', 1, false);
    expect(viewText()).toContain('bu sayfada test yok');
    expect(view()!.querySelector('a[href*="labeling"]')).toBeNull();

    const jump = view()!.querySelector('[data-testid="page-jump"]') as HTMLInputElement;
    jump.value = '4';
    jump.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(pageImage).toHaveBeenLastCalledWith('a1', 4, false);
    expect(viewText()).toContain('1-01 · Kesirler, 1-02 · Oranlar');

    view()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    fixture.detectChanges();
    expect(pageImage).toHaveBeenLastCalledWith('a1', 5, false);
    expect(viewText()).toContain('7: şık sayısı 4 değil');
    expect(view()!.querySelector('a[href*="labeling"]')).not.toBeNull();

    // Aralık dışı numara ve sayfa numarası alanındaki oklar sayfayı değiştirmez.
    jump.value = '99';
    jump.dispatchEvent(new Event('change'));
    jump.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    fixture.detectChanges();
    expect(pageImage).toHaveBeenLastCalledWith('a1', 5, false);

    view()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    view()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    fixture.detectChanges();
    expect(pageImage).toHaveBeenLastCalledWith('a1', 6, false);
    expect((view()!.querySelector('[aria-label="Sonraki sayfa"]') as HTMLButtonElement).disabled).toBe(true);
  });

  it('browseBook_HiddenWithoutPageImagesOrPageCount (#1229)', () => {
    setup([test()]);
    fixture.componentRef.setInput('pageCount', 6);
    fixture.detectChanges();
    expect(el().querySelector('[data-testid="browse-book"]')).toBeNull();
  });

  it('defer_SetsTheRestAsideFromTheHeadAndAfterApproveAll (#1231)', () => {
    setup([test(), test({ id: 2, order: 2, testNo: 2, status: 'NeedsReview' })]);
    const deferred: boolean[] = [];
    fixture.componentInstance.deferChange.subscribe((v) => deferred.push(v));
    expect(el().querySelector('[data-testid="defer"]')).toBeNull();

    fixture.componentRef.setInput('deferrable', true);
    fixture.detectChanges();
    (el().querySelector('[data-testid="defer"]') as HTMLButtonElement).click();
    expect(deferred).toEqual([true]);

    button(el(), 'Sorunsuz testleri onayla').click();
    fixture.detectChanges();
    button(el(), 'Onayla').click();
    fixture.detectChanges();
    (el().querySelector('[data-testid="defer-rest"]') as HTMLButtonElement).click();
    expect(deferred).toEqual([true, true]);
  });

  it('defer_DeferredShowsANoteWithUndo (#1231)', () => {
    setup([test({ status: 'NeedsReview' })]);
    fixture.componentRef.setInput('deferrable', true);
    fixture.componentRef.setInput('deferred', true);
    fixture.detectChanges();
    const deferred: boolean[] = [];
    fixture.componentInstance.deferChange.subscribe((v) => deferred.push(v));

    expect(el().querySelector('[data-testid="defer"]')).toBeNull();
    const note = el().querySelector('[data-testid="deferred-note"]') as HTMLElement;
    expect(note.textContent).toContain('sonraya bırakıldı');
    expect(note.querySelector('a')!.getAttribute('href')).toBe('/book-imports/later');
    (note.querySelector('[data-testid="undefer"]') as HTMLButtonElement).click();
    expect(deferred).toEqual([false]);
  });

  it('focusTest_ShowsTheTestEvenIfCleanAndOpensItsFlags (#1231)', () => {
    setup([
      test({ id: 1 }),
      test({ id: 2, order: 2, testNo: 2, status: 'NeedsReview',
        reviewFlags: [{ code: 'optionCount', pages: [5], numbers: [7], found: null, expected: null, byPage: { 5: [7] } }] }),
    ]);
    fixture.componentRef.setInput('focusTest', 1);
    fixture.detectChanges();
    expect(rows().length).toBe(2);
    expect(fixture.componentInstance.onlyAttention()).toBe(false);

    fixture.componentRef.setInput('focusTest', 2);
    fixture.detectChanges();
    // Yalnız ilk açılışta odaklanır.
    expect(fixture.componentInstance.flagsId()).toBeNull();
  });

  it('focusTest_OpensTheFlagsOfAFlaggedTest (#1231)', () => {
    setup([test({ id: 2, status: 'NeedsReview',
      reviewFlags: [{ code: 'optionCount', pages: [5], numbers: [7], found: null, expected: null, byPage: { 5: [7] } }] })]);
    fixture.componentRef.setInput('focusTest', 2);
    fixture.detectChanges();
    expect(fixture.componentInstance.flagsId()).toBe(2);
    expect(el().querySelector('[data-test-row="2"] .bir-flags')).not.toBeNull();
  });

  it('flags_NoViewPageWithoutPageImages', () => {
    setup([test({
      status: 'NeedsReview',
      reviewFlags: [{ code: 'optionCount', pages: [9], numbers: [4], found: null, expected: null, byPage: { 9: [4] } }],
    })]);
    button(rows()[0], 'Uyarılar').click();
    fixture.detectChanges();
    expect(el().querySelector('[data-testid="view-page"]')).toBeNull();
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
