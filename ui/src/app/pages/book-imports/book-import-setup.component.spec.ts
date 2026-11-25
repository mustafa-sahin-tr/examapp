import type { MockedObject } from 'vitest';
import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { BookImportSetupComponent } from './book-import-setup.component';
import { BookImportSetup } from '../../models/book-import';
import { BookImportService } from '../../services/book-import.service';
import { AdminService } from '../../services/admin.service';
import { WorksheetTeacherSharing } from '../../models/test-instance';
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
      findKeyWithAi: vi.fn().mockReturnValue(of({ match: null, aiCostUsd: 0 })),
      startQrKeys: vi.fn().mockReturnValue(of({ running: true, total: 2, done: 0, failed: 0 })),
      startCleaning: vi.fn().mockReturnValue(of(undefined)),
      setCleaning: vi.fn().mockReturnValue(of(undefined)),
      cancel: vi.fn().mockReturnValue(of(undefined)),
      rerunSetup: vi.fn().mockReturnValue(of(undefined)),
    } as unknown as MockedObject<BookImportService>;
    TestBed.configureTestingModule({
      imports: [BookImportSetupComponent, translocoTestingModule({ langs: { 'book-imports/tr': tr } })],
      providers: [
        provideNoopAnimations(),
        { provide: BookImportService, useValue: api },
        { provide: AdminService, useValue: { getTeachers: vi.fn() } },
      ],
    });
    fixture = TestBed.createComponent(BookImportSetupComponent);
    fixture.componentRef.setInput('jobId', 'a1');
    fixture.detectChanges();
  }

  it('init_ShowsSummaryKeyPageAndMarkedCells', () => {
    create();

    expect(api.setup).toHaveBeenCalledWith('a1');
    expect(api.pageImage).toHaveBeenCalledWith('a1', 237, false, 320);
    expect(el().querySelector('.bis-keypages img')).not.toBeNull();
    expect(text()).toContain('Doğal Sayılar');
    expect(cell(0, 1).closest('label')!.classList).toContain('bis-key__cell--conflict');
    expect(cell(0, 1).closest('label')!.getAttribute('title')).toBe('Diğer okuma: D');
    expect(cell(1, 1).closest('label')!.classList).toContain('bis-key__cell--unreadable');
    expect(text()).toContain('1 hücrede okumalar farklı — ilkine git');
    expect(confirmButton().disabled).toBe(true);
  });

  it('conflict_EnterKeepsCurrentLetter_AndOtherReadCanBeTaken (#1188)', () => {
    create(setupData({
      tests: [
        { order: 1, unit: 1, testNo: 1, title: 'Milyonlar - I', minutes: 16, fileFrom: 7, fileTo: 8, expectedQuestions: 3,
          answerKey: 'ABC', keyConflicts: '.DA', flags: ['keyConflicts'] },
      ],
    }));
    const other = (col: number) => cell(0, col).closest('label')!.querySelector<HTMLButtonElement>('.bis-key__other');

    expect(other(1)!.textContent!.trim()).toBe('D');
    el().querySelector<HTMLButtonElement>('.bis-status__link')!.click();
    expect(document.activeElement).toBe(cell(0, 1));

    press(cell(0, 1), 'Enter');
    expect(cell(0, 1).value).toBe('B');
    expect(other(1)).toBeNull();
    expect(document.activeElement).toBe(cell(0, 2));

    other(2)!.click();
    fixture.detectChanges();
    expect(cell(0, 2).value).toBe('A');
    expect(other(2)).toBeNull();
    expect(confirmButton().disabled).toBe(false);
  });

  it('conflict_MissingInOtherRead_DropsTheBox (#1192)', () => {
    create(setupData({
      tests: [
        { order: 1, unit: 1, testNo: 1, title: 'Milyonlar - I', minutes: 16, fileFrom: 7, fileTo: 8, expectedQuestions: 3,
          answerKey: 'ABC', keyConflicts: '..-', flags: ['keyConflicts'] },
      ],
    }));
    const other = cell(0, 2).closest('label')!.querySelector<HTMLButtonElement>('.bis-key__other')!;
    expect(other.getAttribute('aria-label')).toBe(tr.setup.dropMissingRead);

    other.click();
    fixture.detectChanges();
    expect(cell(0, 2)).toBeNull();
    expect(cell(0, 1).value).toBe('B');
    expect(confirmButton().disabled).toBe(false);
  });

  it('testPages_OpenFullSize_WithPrevNext (#1192)', () => {
    create();
    const view = () => el().querySelector<HTMLElement>('[data-testid="test-pages"]');
    const navButton = (label: string) =>
      Array.from(view()!.querySelectorAll<HTMLButtonElement>('.bis-view__nav button')).find((b) => b.textContent?.includes(label))!;

    el().querySelectorAll<HTMLButtonElement>('[data-testid="view-test-pages"]')[0].click();
    fixture.detectChanges();
    expect(api.pageImage).toHaveBeenLastCalledWith('a1', 7, false);
    expect(view()!.textContent).toContain('1-01');
    expect(view()!.querySelector('.bis-view__cell--conflict')!.textContent!.replace(/\s+/g, '')).toBe('2B/D');
    expect(view()!.textContent).toContain('1 / 2');
    expect(navButton(tr.setup.prevPage).disabled).toBe(true);

    navButton(tr.setup.nextPage).click();
    fixture.detectChanges();
    expect(api.pageImage).toHaveBeenLastCalledWith('a1', 8, false);
    expect(navButton(tr.setup.nextPage).disabled).toBe(true);

    view()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    fixture.detectChanges();
    expect(api.pageImage).toHaveBeenLastCalledWith('a1', 7, false);

    view()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(view()).toBeNull();
  });

  it('browseBook_WalksEveryPage_ShowingTheTestsOfThePage (#1229)', () => {
    create();
    const view = () => el().querySelector<HTMLElement>('[data-testid="test-pages"]');
    const viewText = () => view()!.textContent!.replace(/\s+/g, ' ');
    const navButton = (label: string) =>
      Array.from(view()!.querySelectorAll<HTMLButtonElement>('.bis-view__nav button')).find((b) => b.textContent?.includes(label))!;

    el().querySelector<HTMLButtonElement>('[data-testid="browse-book"]')!.click();
    fixture.detectChanges();
    expect(api.pageImage).toHaveBeenLastCalledWith('a1', 1, false);
    expect(viewText()).toContain('bu sayfada test yok');
    expect(viewText()).toContain('/ 240');
    expect(navButton(tr.setup.prevPage).disabled).toBe(true);

    const jump = view()!.querySelector<HTMLInputElement>('[data-testid="page-jump"]')!;
    jump.value = '8';
    jump.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(api.pageImage).toHaveBeenLastCalledWith('a1', 8, false);
    expect(viewText()).toContain('1-01 Milyonlar - I');

    navButton(tr.setup.nextPage).click();
    fixture.detectChanges();
    expect(api.pageImage).toHaveBeenLastCalledWith('a1', 9, false);
    expect(viewText()).toContain('1-02 Milyonlar - II');

    jump.value = '0';
    jump.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(api.pageImage).toHaveBeenLastCalledWith('a1', 9, false);

    view()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(view()).toBeNull();
  });

  it('keyPage_ClickOpensItFullSize (#1192)', () => {
    create();
    el().querySelector<HTMLButtonElement>('[data-testid="key-open"]')!.click();
    fixture.detectChanges();

    const view = el().querySelector<HTMLElement>('[data-testid="test-pages"]')!;
    expect(view.textContent).toContain(tr.setup.keyPages);
    expect(view.textContent).toContain('1 / 1');
    expect(view.querySelector('.bis-view__key')).toBeNull();
    expect(api.pageImage).toHaveBeenLastCalledWith('a1', 237, false);
  });

  it('keyPages_AreAllShownSideBySide_AndEachOpensItself', () => {
    create(setupData({ answerKeyRanges: [{ fileFrom: 172, fileTo: 172 }, { fileFrom: 223, fileTo: 224 }] }));

    const items = el().querySelectorAll<HTMLButtonElement>('[data-testid="key-open"]');
    expect(Array.from(items, (b) => b.textContent!.trim())).toEqual(['s. 172', 's. 223', 's. 224']);
    expect(api.pageImage.mock.calls.filter((c: unknown[]) => c[3] === 320).map((c: unknown[]) => c[1])).toEqual([172, 223, 224]);
    expect(el().querySelectorAll('.bis-keypages img').length).toBe(3);

    items[2].click();
    fixture.detectChanges();
    expect(api.pageImage).toHaveBeenLastCalledWith('a1', 224, false);
    expect(el().querySelector('[data-testid="test-pages"]')!.textContent).toContain('3 / 3');
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

  it('deletingTrailingCells_ShortensTheKey_issue1167', () => {
    create(setupData({
      tests: [
        { order: 1, unit: 1, testNo: 3, title: 'Beceri', minutes: null, fileFrom: 7, fileTo: 8, expectedQuestions: 6,
          answerKey: 'CDAB??', keyConflicts: '....--', flags: ['keyMismatch', 'keyConflicts'] },
      ],
    }));
    expect(confirmButton().disabled).toBe(true);

    cell(0, 5).focus();
    press(cell(0, 5), 'Backspace');
    press(cell(0, 4), 'Backspace');

    expect(cell(0, 4)).toBeNull();
    expect(cell(0, 3).value).toBe('B');
    expect(confirmButton().disabled).toBe(false);

    press(cell(0, 1), 'Delete'); // aradaki hücre: okunamadı olarak kalır
    expect(cell(0, 1).closest('label')!.classList).toContain('bis-key__cell--unreadable');
    expect(confirmButton().disabled).toBe(true);
  });

  it('enterAnswers_OpensTheKeyPageDockedBelow_WhichCanBeSwitchedAndClosed', () => {
    create(setupData({
      answerKeyRanges: [{ fileFrom: 223, fileTo: 224 }],
      tests: [{ order: 1, unit: 1, testNo: 8, title: 'A', minutes: null, fileFrom: 7, fileTo: 8, expectedQuestions: 2,
        answerKey: null, keyConflicts: null, flags: [] }],
    }));
    const dock = () => el().querySelector<HTMLElement>('[data-testid="key-dock"]');
    expect(dock()).toBeNull();

    el().querySelector<HTMLButtonElement>('[data-testid="enter-key"]')!.click();
    fixture.detectChanges();
    expect(dock()!.querySelector('img')!.getAttribute('alt')).toContain('223');

    Array.from(dock()!.querySelectorAll<HTMLButtonElement>('button')).find((b) => b.textContent!.includes('s. 224'))!.click();
    fixture.detectChanges();
    expect(dock()!.querySelector('img')!.getAttribute('alt')).toContain('224');

    dock()!.querySelector<HTMLButtonElement>('[data-testid="close-dock"]')!.click();
    fixture.detectChanges();
    expect(dock()).toBeNull();
    el().querySelector<HTMLButtonElement>('[data-testid="open-dock"]')!.click();
    fixture.detectChanges();
    expect(dock()).not.toBeNull();
  });

  it('activitySection_ShowsItsCodeAndNoKeyActions (#1134)', () => {
    create(setupData({
      tests: [{ order: 1, unit: 2, testNo: 1, title: 'Kesirler', minutes: null, fileFrom: 7, fileTo: 8, expectedQuestions: null,
        answerKey: null, keyConflicts: null, flags: ['activity'] }],
    }));

    expect(el().querySelector('.bis-code')!.textContent).toContain('2-E01');
    expect(el().querySelector('[data-testid="activity-section"]')!.textContent).toContain(tr.flags.activity);
    expect(el().querySelector('[data-testid="enter-key"]')).toBeNull();
    expect(el().querySelector('[data-testid="ai-find-key"]')).toBeNull();
    expect(text()).not.toContain(tr.setup.noKeyForTest);
  });

  it('enterKey_OpensEmptyCellsForAKeylessTest_AndSendsTypedKey', () => {
    create(setupData({
      tests: [{ order: 1, unit: 1, testNo: 8, title: 'T', minutes: null, fileFrom: 7, fileTo: 8, expectedQuestions: 3,
        answerKey: null, keyConflicts: null, flags: ['noKey'] }],
    }));
    expect(text()).toContain(tr.setup.noKeyForTest);

    el().querySelector<HTMLButtonElement>('[data-testid="enter-key"]')!.click();
    fixture.detectChanges();
    expect(cell(0, 2)).not.toBeNull();
    expect(cell(0, 3)).toBeNull();
    expect(confirmButton().disabled).toBe(true); // boş hücre okunamadı sayılır

    press(cell(0, 0), 'A');
    expect(cell(0, 2)).not.toBeNull(); // yazarken sondaki boş hücreler kaybolmaz
    press(cell(0, 1), 'B');
    press(cell(0, 2), 'C');
    el().querySelector<HTMLButtonElement>('[data-testid="add-key-cell"]')!.click();
    fixture.detectChanges();
    press(cell(0, 3), 'D');
    expect(confirmButton().disabled).toBe(false);

    confirmButton().click();
    expect(api.confirmSetup).toHaveBeenCalledWith('a1', expect.objectContaining({
      tests: [expect.objectContaining({ order: 1, answerKey: 'ABCD' })],
    }));
  });

  const keyless = (order: number, testNo: number, expected: number | null) => ({
    order, unit: 1, testNo, title: 'T' + testNo, minutes: null, fileFrom: 6 + order, fileTo: 6 + order, expectedQuestions: expected,
    answerKey: null, keyConflicts: null, flags: [],
  });

  it('aiFind_FillsTheSuggestedKeyWithAnAiChip_OrSaysItWasNotFound', async () => {
    create(setupData({ tests: [keyless(1, 36, 2), keyless(2, 37, 1)] }));
    const changed = vi.fn();
    fixture.componentInstance.changed.subscribe(changed);
    api.findKeyWithAi
      .mockReturnValueOnce(of({ match: { order: 1, label: '5-36', answerKey: 'CB', keyConflicts: null }, aiCostUsd: 0.4 }))
      .mockReturnValueOnce(of({ match: null, aiCostUsd: 0.5 }));

    el().querySelectorAll<HTMLButtonElement>('[data-testid="ai-find-key"]')[0].click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.findKeyWithAi).toHaveBeenCalledWith('a1', 1);
    expect(changed).toHaveBeenCalledTimes(1); // sayfanın maliyet kartı yenilenir
    expect(cell(0, 0).value).toBe('C');
    expect(cell(0, 1).value).toBe('B');
    expect(el().querySelector('[data-testid="ai-key-chip"]')!.textContent).toContain('AI: 5-36');
    expect(text()).toContain('$0.40');

    el().querySelector<HTMLButtonElement>('[data-testid="ai-find-key"]')!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text()).toContain('AI bulamadı');
    expect(el().querySelector('[data-testid="ai-find-key"]')).toBeNull();
    expect(el().querySelector('[data-testid="enter-key"]')).not.toBeNull(); // elle girmek hâlâ mümkün
  });

  it('findAll_AsksEachKeylessTestInTurn_AndKeepsTypedKeys', async () => {
    create(setupData({ tests: [keyless(1, 1, null), keyless(2, 2, null), keyless(3, 3, null)] }));
    api.findKeyWithAi.mockImplementation((_: string, order: number) =>
      of({ match: order === 3 ? null : { order, label: null, answerKey: 'AB', keyConflicts: null }, aiCostUsd: 1 }));
    // 2. teste öğretmen anahtarı elle girdi: toplu arama onu sormaz.
    el().querySelectorAll<HTMLButtonElement>('[data-testid="enter-key"]')[1].click();
    fixture.detectChanges();
    press(cell(1, 0), 'D');
    expect(text()).toContain('2 testin anahtarı yok.');

    el().querySelector<HTMLButtonElement>('[data-testid="find-all-keys"]')!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.findKeyWithAi.mock.calls.map((c: unknown[]) => c[1])).toEqual([1, 3]);
    expect(cell(0, 0).value).toBe('A');
    expect(cell(1, 0).value).toBe('D');
    expect(el().querySelector('[data-testid="ai-key-chip"]')!.textContent!.trim()).toBe('AI');
    expect(text()).toContain('AI bulamadı');
    expect(el().querySelector('[data-testid="find-all-keys"]')).toBeNull(); // sorulmamış anahtarsız test kalmadı
  });

  it('deletingEveryCell_LeavesTheTestWithoutKey', () => {
    create(setupData({
      tests: [{ order: 1, unit: 1, testNo: 1, title: 'T', minutes: null, fileFrom: 7, fileTo: 8, expectedQuestions: 1,
        answerKey: 'A', keyConflicts: '.', flags: [] }],
    }));

    press(cell(0, 0), 'Backspace');

    expect(cell(0, 0)).toBeNull();
    expect(text()).toContain(tr.setup.noKeyForTest);
  });

  it('includeAll_ClearsAndSelectsEveryRow_Issue1358', () => {
    create();
    const box = () => fixture.nativeElement.querySelector('[data-testid="setup-include-all"] input') as HTMLInputElement;
    const c = fixture.componentInstance;
    expect(box().checked).toBe(true);

    box().click();
    fixture.detectChanges();
    expect(c.rows().every((r) => !r.include)).toBe(true);
    expect(box().checked).toBe(false);

    c.update(c.rows()[0], { include: true });
    fixture.detectChanges();
    expect(box().indeterminate).toBe(true);

    box().click();
    fixture.detectChanges();
    expect(c.rows().every((r) => r.include)).toBe(true);
  });

  it('rowThumbs_LoadFirstPageSmallAndOpenViewer_Issue1358', () => {
    create();
    fixture.detectChanges();

    expect(api.pageImage).toHaveBeenCalledWith('a1', 7, false, 128);
    expect(api.pageImage).toHaveBeenCalledWith('a1', 9, false, 128);
    const thumbs = fixture.nativeElement.querySelectorAll('[data-testid="row-thumb"]') as NodeListOf<HTMLButtonElement>;
    expect(thumbs.length).toBe(fixture.componentInstance.rows().length);
    expect(thumbs[0].querySelector('img')).not.toBeNull();

    thumbs[1].click();
    fixture.detectChanges();
    expect(fixture.componentInstance.viewedTest()).toBe(2);
    expect(api.pageImage).toHaveBeenLastCalledWith('a1', 9, false);

    // Aralık değişince yeni ilk sayfa küçük resmi yüklenir.
    const c = fixture.componentInstance;
    c.update(c.rows()[0], { fileFrom: 6 });
    fixture.detectChanges();
    expect(api.pageImage).toHaveBeenCalledWith('a1', 6, false, 128);
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
      tests: [{ order: 1, title: 'Milyonlar - I', minutes: 16, fileFrom: 7, fileTo: 8, answerKey: 'ADC', keySource: 'Book' }],
      teacherSharing: WorksheetTeacherSharing.Private,
    });
    expect(changed).toHaveBeenCalled();
  });

  // #1259: paylaşım onayla gider; "Yalnız okulum" yalnız okulu olana, "Seçili okul ve öğretmenler" yalnız admine.
  it('sharing_OptionsFollowTheSetup_AndBookAudienceNeedsSomeoneBeforeConfirm (#1259)', () => {
    create(setupData({ canShareWithSchool: false, canShareWithAudience: true,
      tests: [{ ...setupData().tests[0], answerKey: 'ADC', keyConflicts: '...', flags: [] }] }));

    expect(el().querySelector('[data-testid="book-sharing-schoolOnly"]')).toBeNull();
    expect(el().querySelector('[data-testid="book-sharing-bookAudience"]')).not.toBeNull();
    expect(confirmButton().disabled).toBe(false);

    const c = fixture.componentInstance;
    c.sharing.set(WorksheetTeacherSharing.BookAudience);
    fixture.detectChanges();
    expect(confirmButton().disabled).toBe(true);
    expect(text()).toContain(tr.sharing.emptyAudience);

    c.audienceSchools.set([{ id: 3, name: 'Atatürk O.O.' }]);
    c.audienceTeachers.set([{ id: 41, name: 'Ayşe Öğretmen', schoolName: null }]);
    fixture.detectChanges();
    confirmButton().click();

    expect(api.confirmSetup).toHaveBeenCalledWith('a1', expect.objectContaining({
      teacherSharing: WorksheetTeacherSharing.BookAudience,
      audience: { schoolIds: [3], teacherIds: [41] },
    }));
  });

  it('qrFind_ReadsInTheBackground_FillsTheKeysWithAQrChip_AndSendsTheirSource (#1219)', async () => {
    vi.useFakeTimers();
    const qr = (order: number, testNo: number, expected: number) => ({
      ...keyless(order, testNo, expected), qrLink: `http://fi.frns.in/fen501-00${order}`, qrReadable: true,
    });
    const data = setupData({ noAnswerKey: true, answerKeyRanges: [], qrKeysAvailable: true, tests: [qr(1, 1, 3), qr(2, 2, 2)] });
    create(data);
    const read = (key: string) => ({ answerKey: key, keySource: 'Qr' as const });
    api.setup
      .mockReturnValueOnce(of({ ...data, qrRun: { running: true, total: 2, done: 1, failed: 0 },
        tests: [{ ...data.tests[0], ...read('B?A') }, data.tests[1]] }))
      .mockReturnValueOnce(of({ ...data, qrRun: { running: false, total: 2, done: 2, failed: 0 },
        tests: [{ ...data.tests[0], ...read('B?A') }, { ...data.tests[1], ...read('DCA') }] }));

    expect(text()).toContain(tr.setup.noKeyQrHint);
    expect(el().querySelector<HTMLAnchorElement>('[data-testid="qr-open"]')!.href).toBe('http://fi.frns.in/fen501-001');
    el().querySelector<HTMLButtonElement>('[data-testid="qr-find-all-keys"]')!.click();
    fixture.detectChanges();

    expect(api.startQrKeys).toHaveBeenCalledWith('a1', undefined);
    expect(el().querySelector('[data-testid="qr-find-all-progress"]')).not.toBeNull();
    // Okuma sürerken onaylanmaz (okuma test satırlarına yazar).
    expect(confirmButton().disabled).toBe(true);

    await vi.advanceTimersByTimeAsync(4000);
    fixture.detectChanges();
    expect([cell(0, 0).value, cell(0, 1).value, cell(0, 2).value]).toEqual(['B', '?', 'A']);
    expect(el().querySelectorAll('[data-testid="qr-key-chip"]').length).toBe(1);

    await vi.advanceTimersByTimeAsync(4000);
    fixture.detectChanges();
    vi.useRealTimers();
    expect(api.setup).toHaveBeenCalledTimes(3); // bitti: yeniden okuma durdu
    expect(el().querySelector('[data-testid="qr-find-all-progress"]')).toBeNull();
    expect(el().querySelectorAll('[data-testid="qr-key-chip"]').length).toBe(2);
    // QR'da 3 çözüm var, sayfada 2 soru görüldü: onaydan önce bakılmalı.
    expect(el().querySelectorAll('[data-testid="qr-count-differs"]').length).toBe(1);
    // Anahtar geldi: "cevapsız devam" sorulmaz; okunamayan harf girilene kadar onay kapalı.
    expect(text()).not.toContain(tr.setup.continueWithoutKey);
    expect(confirmButton().disabled).toBe(true);

    press(cell(0, 1), 'C');
    confirmButton().click();

    expect(api.confirmSetup).toHaveBeenCalledWith('a1', expect.objectContaining({
      tests: [
        expect.objectContaining({ order: 1, answerKey: 'BCA', keySource: 'Qr' }),
        expect.objectContaining({ order: 2, answerKey: 'DCA', keySource: 'Qr' }),
      ],
    }));
  });

  it('qrFind_IsHiddenWithoutTheTranscriber_ButTheLinkCanBeOpened (#1219)', () => {
    create(setupData({
      qrKeysAvailable: false,
      tests: [{ ...keyless(1, 1, 2), qrLink: 'http://fi.frns.in/fen501-001', qrReadable: true }],
    }));

    expect(el().querySelector('[data-testid="qr-find-key"]')).toBeNull();
    expect(el().querySelector('[data-testid="qr-find-all-keys"]')).toBeNull();
    expect(el().querySelector('[data-testid="qr-open"]')).not.toBeNull();
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

  it('rerunSetup_AsksOnceThenQueuesTheSetupAgain', () => {
    create();
    const changed = vi.fn();
    fixture.componentInstance.changed.subscribe(changed);
    const button = () => el().querySelector<HTMLButtonElement>('[data-testid="setup-rerun"]')!;
    expect(button().textContent).toContain(tr.setup.rerun);

    button().click();
    fixture.detectChanges();
    expect(api.rerunSetup).not.toHaveBeenCalled();
    expect(button().textContent).toContain(tr.setup.rerunConfirm);

    button().click();
    expect(api.rerunSetup).toHaveBeenCalledWith('a1');
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

  describe('page cleaning (#1234)', () => {
    const cleaned = {
      status: 'Done' as const, running: false, watermark: true, markColour: [182, 23, 113], colourPicked: false,
      marks: 131, pages: 80, processed: 80, useOriginal: false,
    };
    const testId = (id: string) => el().querySelector<HTMLElement>(`[data-testid="${id}"]`);

    it('done_ShowsResult_UseOriginalAndCompareOriginal', () => {
      create(setupData({ cleaning: cleaned, cleaningAvailable: true }));

      expect(testId('cleaning-done')!.textContent).toContain('filigran ve 131 cevap işareti silindi');
      testId('cleaning-use-original')!.querySelector('input')!.click();
      fixture.detectChanges();
      expect(api.setCleaning).toHaveBeenCalledWith('a1', true);

      testId('cleaning-compare')!.click();
      fixture.detectChanges();
      expect(api.pageImage).toHaveBeenLastCalledWith('a1', 1, false);
      testId('toggle-original')!.click();
      fixture.detectChanges();
      expect(api.pageImage).toHaveBeenLastCalledWith('a1', 1, true);
      expect(testId('toggle-original')!.getAttribute('aria-pressed')).toBe('true');
    });

    it('start_RunsInBackground_AndBlocksConfirmUntilDone', () => {
      vi.useFakeTimers();
      try {
        create(setupData({ cleaning: null, cleaningAvailable: true, tests: [] }));
        expect(text()).toContain(tr.setup.cleaningIntro);

        testId('cleaning-start')!.click();
        fixture.detectChanges();
        expect(api.startCleaning).toHaveBeenCalledWith('a1', undefined);
        expect(testId('cleaning-progress')).not.toBeNull();
        expect(fixture.componentInstance.canConfirm()).toBe(false);

        api.setup.mockReturnValue(of(setupData({ cleaning: cleaned, cleaningAvailable: true })));
        vi.advanceTimersByTime(4000);
        fixture.detectChanges();
        expect(testId('cleaning-progress')).toBeNull();
        expect(testId('cleaning-done')).not.toBeNull();
      } finally {
        vi.useRealTimers();
      }
    });

    it('pickColour_OpensOriginalPages_AndIgnoresGreyClicks', () => {
      create(setupData({ cleaning: { ...cleaned, status: 'NothingFound', watermark: false, markColour: null, marks: 0 }, cleaningAvailable: true }));
      expect(testId('cleaning-nothing')).not.toBeNull();

      testId('cleaning-pick')!.click();
      fixture.detectChanges();
      expect(testId('cleaning-pick-hint')).not.toBeNull();
      expect(api.pageImage).toHaveBeenLastCalledWith('a1', 1, true);

      // Okunamayan görsel / renksiz nokta: temizlik başlamaz, uyarı görünür.
      el().querySelector<HTMLImageElement>('.bis-view__body img')!.click();
      fixture.detectChanges();
      expect(api.startCleaning).not.toHaveBeenCalled();
      expect(testId('cleaning-pick-hint')!.textContent).toContain(tr.setup.cleaningPickMissed);
    });

    it('pickColour_ClickOnAPinkBox_StartsCleaningWithThatColour', async () => {
      const canvas = document.createElement('canvas');
      canvas.width = 200;
      canvas.height = 200;
      const ctx = canvas.getContext('2d')!;
      ctx.fillStyle = '#fff';
      ctx.fillRect(0, 0, 200, 200);
      ctx.fillStyle = 'rgb(182, 23, 113)';
      ctx.fillRect(50, 50, 40, 30);
      const blob = await new Promise<Blob>((resolve) => canvas.toBlob((b) => resolve(b!), 'image/png'));
      create(setupData({ cleaning: null, cleaningAvailable: true }));
      api.pageImage.mockReturnValue(of(blob));

      testId('cleaning-pick')!.click();
      fixture.detectChanges();
      const img = el().querySelector<HTMLImageElement>('.bis-view__body img')!;
      await img.decode();
      const rect = img.getBoundingClientRect();
      const scale = rect.width / 200;
      img.dispatchEvent(new MouseEvent('click', { bubbles: true, clientX: rect.left + 70 * scale, clientY: rect.top + 65 * scale }));
      fixture.detectChanges();

      expect(api.startCleaning).toHaveBeenCalledWith('a1', [182, 23, 113]);
      expect(testId('test-pages')).toBeNull();
      expect(testId('cleaning-progress')!.textContent).toContain(tr.setup.cleaningPickedColour);
    });

    it('cleaningDone_FillsKeysFromMarks_AndShowsMarkConflictsOnBookKeys (#1235)', () => {
      vi.useFakeTimers();
      try {
        const base = setupData().tests;
        create(setupData({
          cleaning: null, cleaningAvailable: true,
          tests: [{ ...base[0], answerKey: 'ABC', keyConflicts: '...', flags: [] }, { ...base[1], answerKey: null, keyConflicts: null }],
        }));
        testId('cleaning-start')!.click();
        fixture.detectChanges();

        api.setup.mockReturnValue(of(setupData({
          cleaning: cleaned, cleaningAvailable: true,
          tests: [
            { ...base[0], answerKey: 'ABC', keyConflicts: '.D.', flags: [] },
            { ...base[1], answerKey: 'CD', keyConflicts: '..', keySource: 'Marks' },
          ],
        })));
        vi.advanceTimersByTime(4000);
        fixture.detectChanges();

        expect(cell(0, 1).closest('label')!.classList).toContain('bis-key__cell--conflict');
        expect(cell(1, 0).value).toBe('C');
        expect(cell(1, 1).value).toBe('D');
        expect(testId('marks-key-chip')!.textContent).toContain(tr.setup.marksFound);
      } finally {
        vi.useRealTimers();
      }
    });

    it('demoOrUnavailable_HidesCleaning', () => {
      create(setupData({ isDemo: true, cleaningAvailable: true }));
      expect(testId('cleaning')).toBeNull();
    });
  });
});
