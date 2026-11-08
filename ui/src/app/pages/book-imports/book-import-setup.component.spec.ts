import type { MockedObject } from 'vitest';
import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { BookImportSetupComponent } from './book-import-setup.component';
import { BookImportSetup } from '../../models/book-import';
import { BookImportService } from '../../services/book-import.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import tr from '../../../../public/i18n/book-imports/tr.json';

function setupData(overrides: Partial<BookImportSetup> = {}): BookImportSetup {
  return {
    jobId: 'a1', bookName: 'Puan BTS 5 Matematik', pageCount: 240, offset: 0,
    answerKeyRanges: [{ fileFrom: 237, fileTo: 237 }], selfAssessmentPages: [236],
    units: [{ unit: 1, title: 'Doğal Sayılar', fileFrom: 7, fileTo: 62, topicId: 5, topicName: 'Doğal Sayılar' }],
    orphanKeys: [], noAnswerKey: false, aiCostUsd: 0.31, schemaRevision: 1, existingBook: null,
    tests: [
      { order: 1, unit: 1, testNo: 1, title: 'Milyonlar - I', minutes: 16, fileFrom: 7, fileTo: 8, expectedQuestions: 3,
        answerKey: 'ABC', keyConflicts: '.D.', flags: ['keyConflicts'] },
      { order: 2, unit: 1, testNo: 2, title: 'Milyonlar - II', minutes: 12, fileFrom: 9, fileTo: 9, expectedQuestions: 2,
        answerKey: 'C?', keyConflicts: '..', flags: [] },
    ],
    ...overrides,
  };
}

describe('BookImportSetupComponent (#1021)', () => {
  let fixture: ComponentFixture<BookImportSetupComponent>;
  let api: MockedObject<BookImportService>;

  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => el().textContent ?? '';
  const cell = (row: number, col: number) => el().querySelector<HTMLInputElement>(`[data-cell="${row}-${col}"]`)!;
  const confirmButton = () =>
    Array.from(el().querySelectorAll<HTMLButtonElement>('.bis-actions button')).find((b) => b.textContent?.includes(tr.setup.confirm))!;

  function press(target: HTMLElement, key: string): void {
    target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
    fixture.detectChanges();
  }

  function create(data: BookImportSetup = setupData()): void {
    api = {
      setup: vi.fn().mockReturnValue(of(data)),
      pageImage: vi.fn().mockReturnValue(of(new Blob(['x'], { type: 'image/webp' }))),
      confirmSetup: vi.fn().mockReturnValue(of({})),
      cancel: vi.fn().mockReturnValue(of(undefined)),
    } as unknown as MockedObject<BookImportService>;
    TestBed.configureTestingModule({
      imports: [BookImportSetupComponent, translocoTestingModule({ langs: { 'book-imports/tr': tr } })],
      providers: [provideNoopAnimations(), { provide: BookImportService, useValue: api }],
    });
    fixture = TestBed.createComponent(BookImportSetupComponent);
    fixture.componentRef.setInput('jobId', 'a1');
    fixture.detectChanges();
  }

  it('init_ShowsSummaryKeyPageAndMarkedCells', () => {
    create();

    expect(api.setup).toHaveBeenCalledWith('a1');
    expect(api.pageImage).toHaveBeenCalledWith('a1', 237);
    expect(el().querySelector('.bis-keypage img')).not.toBeNull();
    expect(text()).toContain('Doğal Sayılar');
    expect(cell(0, 1).closest('label')!.classList).toContain('bis-key__cell--conflict');
    expect(cell(0, 1).closest('label')!.getAttribute('title')).toBe('Diğer okuma: D');
    expect(cell(1, 1).closest('label')!.classList).toContain('bis-key__cell--unreadable');
    expect(text()).toContain('1 hücrede okumalar farklı.');
    expect(confirmButton().disabled).toBe(true);
  });

  it('keyboard_TypingFixesCellsAndMovesFocus', () => {
    create();

    cell(0, 1).focus();
    press(cell(0, 1), 'd');
    expect(cell(0, 1).value).toBe('D');
    expect(document.activeElement).toBe(cell(0, 2));

    press(cell(0, 2), 'ArrowLeft');
    expect(document.activeElement).toBe(cell(0, 1));
    press(cell(0, 1), 'ArrowDown');
    expect(document.activeElement).toBe(cell(1, 1));
    press(cell(1, 1), 'ArrowUp');
    expect(document.activeElement).toBe(cell(0, 1));

    press(cell(0, 1), 'x'); // yalnız A–E
    expect(cell(0, 1).value).toBe('D');

    cell(1, 1).focus();
    press(cell(1, 1), 'B');
    expect(text()).toContain('2 test aktarılmaya hazır.');
    expect(confirmButton().disabled).toBe(false);
  });

  it('confirm_SendsEditedTestsAndEmitsChanged', () => {
    create();
    const changed = vi.fn();
    fixture.componentInstance.changed.subscribe(changed);
    press(cell(0, 1), 'D');
    press(cell(1, 1), 'A');
    // İkinci testi çıkar.
    const row = fixture.componentInstance.rows()[1];
    fixture.componentInstance.update(row, { include: false });
    fixture.detectChanges();

    confirmButton().click();

    expect(api.confirmSetup).toHaveBeenCalledWith('a1', {
      bookName: 'Puan BTS 5 Matematik',
      existingBookId: null,
      tests: [{ order: 1, title: 'Milyonlar - I', minutes: 16, fileFrom: 7, fileTo: 8, answerKey: 'ADC' }],
    });
    expect(changed).toHaveBeenCalled();
  });

  it('noKey_RequiresContinueOrCancel', () => {
    create(setupData({
      noAnswerKey: true, answerKeyRanges: [],
      tests: [{ order: 1, unit: 1, testNo: 1, title: 'T', minutes: null, fileFrom: 7, fileTo: 8, expectedQuestions: 3,
        answerKey: null, keyConflicts: null, flags: [] }],
    }));
    const changed = vi.fn();
    fixture.componentInstance.changed.subscribe(changed);

    expect(text()).toContain(tr.setup.noKey);
    expect(text()).toContain(tr.setup.noKeyForTest);
    expect(confirmButton().disabled).toBe(true);

    const continueButton = Array.from(el().querySelectorAll<HTMLButtonElement>('.bis-banner button'))
      .find((b) => b.textContent?.includes(tr.setup.continueWithoutKey))!;
    continueButton.click();
    fixture.detectChanges();
    expect(confirmButton().disabled).toBe(false);

    const cancel = Array.from(el().querySelectorAll<HTMLButtonElement>('.bis-actions button'))
      .find((b) => b.textContent?.includes(tr.setup.cancelImport))!;
    cancel.click();
    expect(api.cancel).toHaveBeenCalledWith('a1');
    expect(changed).toHaveBeenCalled();
  });

  it('nameTaken_ShowsExistingBookChoiceAndSendsItsId', () => {
    create(setupData({ tests: [setupData().tests[0]] }));
    api.confirmSetup.mockReturnValueOnce(throwError(() => new HttpErrorResponse({
      status: 409, error: { code: 'bookImport.bookNameTaken', existingBook: { id: 9, name: 'Puan BTS 5 Matematik' }, message: 'Var' },
    })));
    press(cell(0, 1), 'B');

    confirmButton().click();
    fixture.detectChanges();

    expect(text()).toContain('“Puan BTS 5 Matematik” adlı bir kitap zaten var.');
    expect(text()).toContain('Var');
    confirmButton().click();
    expect(api.confirmSetup).toHaveBeenLastCalledWith('a1', expect.objectContaining({ existingBookId: 9 }));
  });
});
