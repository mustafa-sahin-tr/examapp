import type { MockedObject } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';

import { BookImportReviewComponent } from './book-import-review.component';
import { BookImportJobTest, BookImportReviewQuestion } from '../../models/book-import';
import { BookImportService } from '../../services/book-import.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import tr from '../../../../public/i18n/book-imports/tr.json';

function test(overrides: Partial<BookImportJobTest> = {}): BookImportJobTest {
  return {
    id: 1, order: 1, unit: 1, testNo: 1, title: 'Kesirler', status: 'Done', questionsFound: 4, keyAnswers: 4, flags: [],
    questionsSaved: 4, duplicatesSkipped: 0, reviewFlags: [], worksheetId: 100, fileFrom: 3, fileTo: 4, answerless: 0,
    error: null, ...overrides,
  };
}

function question(id: number, answerIndex: number | null): BookImportReviewQuestion {
  return { id, number: String(id), filePage: 3, optionCount: 4, imageUrl: null, answerIndex };
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

  it('fixInLabeling_ListsTheTestPagesMarksFlaggedOnesAndLinksToLabeling', async () => {
    setup([test({ status: 'NeedsReview', reviewFlags: [{ code: 'missingNumbers', pages: [4], numbers: [3], found: null, expected: null }] })]);

    button(rows()[0], 'İşaretlemede düzelt').click();
    fixture.detectChanges();
    await fixture.whenStable();

    const items = Array.from(document.querySelectorAll<HTMLAnchorElement>('.mat-mdc-menu-panel a[mat-menu-item]'));
    expect(items.map((a) => a.querySelector('span')?.textContent?.trim())).toEqual(['Sayfa 3', 'Sayfa 4 · işaretli']);
    expect(items.map((a) => a.querySelector('mat-icon')?.textContent?.trim())).toEqual(['description', 'flag']);
    expect(items[1].getAttribute('href')).toBe('/labeling?bookImport=a1&test=1&page=4');
  });

  it('completedJob_IsReadOnly', () => {
    setup([test({ status: 'Approved' })], false);

    expect(text()).toContain('İncelenecek test kalmadı.');
    expect(button(el(), 'Sorunsuz testleri onayla')).toBeUndefined();
    expect(button(el(), 'İşaretlemede düzelt')).toBeUndefined();
  });
});
