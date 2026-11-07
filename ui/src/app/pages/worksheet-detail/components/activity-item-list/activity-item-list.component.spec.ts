import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { Subject, of, throwError } from 'rxjs';
import { MockedObject, vi } from 'vitest';

import { ActivityItemListComponent, toRows } from './activity-item-list.component';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../../../models/locale';
import { Question } from '../../../../models/question';
import { QuestionService } from '../../../../services/question.service';
import rootTr from '../../../../../../public/i18n/tr.json';
import worksheetDetailTr from '../../../../../../public/i18n/worksheet-detail/tr.json';

const translocoTesting = TranslocoTestingModule.forRoot({
  langs: { tr: { ...rootTr, 'worksheet-detail': worksheetDetailTr }, 'worksheet-detail/tr': worksheetDetailTr },
  translocoConfig: {
    availableLangs: [...SUPPORTED_LOCALE_CODES],
    defaultLang: DEFAULT_LOCALE,
    scopes: { keepCasing: true },
  },
  preloadLangs: true,
});

const ORDER_PLAN = JSON.stringify({
  version: 1,
  type: 'dragPlace',
  mode: 'order',
  targets: [
    { id: 't1', label: '1', capacity: 1 },
    { id: 't2', label: '2', capacity: 1 },
  ],
  cards: [
    { id: 'c1', label: 'Tohum' },
    { id: 'c2', label: 'Fidan' },
  ],
});
const ORDER_SOLUTION = JSON.stringify({ t1: ['c1'], t2: ['c2'] });

function question(overrides: Partial<Question>): Question {
  return { id: 1, text: '', imageUrl: '', answers: [], isExample: false, answerColCount: 0, ...overrides } as Question;
}

const textItem = question({
  id: 21,
  interactionType: 'dragPlace',
  interactionPlan: ORDER_PLAN,
  interactionSolution: ORDER_SOLUTION,
  passage: { instruction: 'Büyüme sırasına diziniz.' } as Question['passage'],
});
const visualItem = question({
  id: 22,
  imageUrl: 'https://img/q.png',
  interactionType: 'regionPaint',
  interactionPlan: '{"version":1,"type":"regionPaint","mode":"regions"}',
  text: 'Boyayınız',
});
const mcqItem = question({ id: 23, imageUrl: 'https://img/m.png', text: '2 + 2 = ?' });

describe('toRows (#684)', () => {
  it('numbers by position, resolves type keys and splits form-editable / visual items', () => {
    const rows = toRows([textItem, visualItem, mcqItem]);

    expect(rows.map((r) => r.number)).toEqual([1, 2, 3]);
    expect(rows.map((r) => r.typeKey)).toEqual(['dragPlace', 'regionPaint', 'mcq']);
    expect(rows[0]).toMatchObject({
      formEditable: true,
      visual: false,
      preview: 'Büyüme sırasına diziniz.',
      imageUrl: null,
    });
    expect(rows[1]).toMatchObject({ formEditable: false, visual: true, preview: 'Boyayınız' });
    expect(rows[2]).toMatchObject({ formEditable: false, visual: true, preview: '2 + 2 = ?' });
  });
});

describe('ActivityItemListComponent (#684)', () => {
  let fixture: ComponentFixture<ActivityItemListComponent>;
  let questions: MockedObject<QuestionService>;
  let dialogResult: unknown;
  let snackBar: { open: ReturnType<typeof vi.fn> };
  let snackAction: Subject<void>;
  const navigate = vi.fn();

  const el = () => fixture.nativeElement as HTMLElement;
  const rows = () => Array.from(el().querySelectorAll('[data-testid="activity-item-row"]'));

  async function setup(list: Question[] | 'error', canEdit = true, mode: 'activity' | 'test' = 'activity'): Promise<void> {
    questions = {
      getAll: vi.fn().mockReturnValue(list === 'error' ? throwError(() => new Error('x')) : of(list)),
      removeQuestionFromTest: vi.fn().mockReturnValue(of({ success: true })),
      reorderTestQuestions: vi.fn().mockReturnValue(of({ success: true })),
      updateCorrectAnswer: vi.fn().mockReturnValue(of({})),
    } as unknown as MockedObject<QuestionService>;
    snackAction = new Subject<void>();
    snackBar = { open: vi.fn(() => ({ onAction: () => snackAction })) };
    dialogResult = true;
    TestBed.configureTestingModule({
      imports: [ActivityItemListComponent, translocoTesting],
      providers: [
        { provide: QuestionService, useValue: questions },
        { provide: MatDialog, useValue: { open: vi.fn(() => ({ afterClosed: () => of(dialogResult) })) } },
        { provide: MatSnackBar, useValue: snackBar },
        { provide: Router, useValue: { navigate } },
      ],
    });
    fixture = TestBed.createComponent(ActivityItemListComponent);
    fixture.componentRef.setInput('worksheetId', 7);
    fixture.componentRef.setInput('gradeId', 5);
    fixture.componentRef.setInput('canEdit', canEdit);
    fixture.componentRef.setInput('mode', mode);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  // #838: taşı / kaldır satırın taşma menüsünde; menü overlay'de (document.body) açılır.
  const moreButton = (index: number) =>
    rows()[index].querySelector('[data-testid="activity-item-more"]') as HTMLButtonElement;
  function menuItem(index: number, id: string): HTMLButtonElement {
    moreButton(index).click();
    fixture.detectChanges();
    const items = document.querySelectorAll<HTMLButtonElement>(`.mat-mdc-menu-panel [data-testid="${id}"]`);
    return items[items.length - 1];
  }

  it('lists items with type badge, visual hint and edit only on form-editable items', async () => {
    await setup([textItem, visualItem]);

    expect(rows().length).toBe(2);
    expect(rows()[0].textContent).toContain('Sürükle-yerleştir');
    expect(rows()[0].querySelector('[data-testid="activity-item-edit"]')).not.toBeNull();
    expect(rows()[1].textContent).toContain('Görselli');
    expect(rows()[1].querySelector('[data-testid="activity-item-edit"]')).toBeNull();
    expect(rows()[1].querySelector('[data-testid="activity-item-visual-hint"]')?.textContent).toContain(
      'Görselli, düzenlenemez'
    );
    expect(menuItem(0, 'activity-item-remove').getAttribute('aria-label')).toBe(
      "Madde 1'i kaldır"
    );
  });

  it('is read-only without edit permission', async () => {
    await setup([textItem, visualItem], false);

    expect(el().querySelector('[data-testid="activity-item-edit"]')).toBeNull();
    expect(el().querySelector('[data-testid="activity-item-more"]')).toBeNull();
    expect(el().querySelector('[data-testid="activity-item-visual-hint"]')).toBeNull();
  });

  it('edit opens the text-item editor for that question', async () => {
    await setup([textItem]);

    (el().querySelector('[data-testid="activity-item-edit"]') as HTMLButtonElement).click();

    expect(navigate).toHaveBeenCalledWith(['/activity-editor'], {
      queryParams: { testId: 7, gradeId: 5, questionId: 21 },
    });
  });

  it('remove after confirm deletes, renumbers and emits', async () => {
    await setup([textItem, visualItem, mcqItem]);
    const removed = vi.fn();
    fixture.componentInstance.removed.subscribe(removed);

    menuItem(0, 'activity-item-remove').click();
    fixture.detectChanges();

    expect(questions.removeQuestionFromTest).toHaveBeenCalledWith(7, 21);
    expect(removed).toHaveBeenCalledWith(21);
    expect(rows().map((r) => r.querySelector('.ail__no')?.textContent?.trim())).toEqual(['1', '2']);
    expect(rows()[0].textContent).toContain('Boyama');
  });

  it('cancelled confirm does not remove', async () => {
    await setup([textItem]);
    dialogResult = false;

    menuItem(0, 'activity-item-remove').click();

    expect(questions.removeQuestionFromTest).not.toHaveBeenCalled();
    fixture.detectChanges();
    expect(rows().length).toBe(1);
  });

  it('shows empty state with both add actions', async () => {
    await setup([]);

    expect(el().querySelector('[data-testid="activity-items-empty"]')).not.toBeNull();
    expect(el().querySelector('[data-testid="activity-items-add-text"]')).not.toBeNull();
    expect(el().querySelector('[data-testid="activity-items-add-bank"]')).not.toBeNull();
  });

  it('shows an alert with retry on error', async () => {
    await setup('error');

    const alert = el().querySelector('[data-testid="activity-items-error"]');
    expect(alert?.getAttribute('role')).toBe('alert');
    questions.getAll.mockReturnValue(of([textItem]));
    (alert?.querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(rows().length).toBe(1);
  });

  // ---------------------------------------------------------------- #685: yeniden sıralama

  const numbers = () => rows().map((r) => r.querySelector('.ail__no')?.textContent?.trim());
  const ids = () => rows().map((r) => Number(r.getAttribute('data-question-id')));
  const button = (index: number, id: string) => menuItem(index, id);

  it('↓ moves the item, saves the full order, announces it and returns focus to the row menu', async () => {
    await setup([textItem, visualItem, mcqItem]);

    button(0, 'activity-item-move-down').click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(ids()).toEqual([22, 21, 23]);
    expect(numbers()).toEqual(['1', '2', '3']);
    expect(questions.reorderTestQuestions).toHaveBeenCalledWith(7, [22, 21, 23]);
    expect(el().querySelector('[data-testid="activity-items-live"]')?.textContent).toContain(
      'Madde 1, 2. sıraya taşındı'
    );
    expect(document.activeElement).toBe(moreButton(1));
    expect(moreButton(1).getAttribute('aria-label')).toBe('Madde 2 için diğer işlemler');
  });

  it('first ↑ and last ↓ are disabled in the menu', async () => {
    await setup([textItem, visualItem]);

    expect(button(0, 'activity-item-move-up').disabled).toBe(true);
    expect(button(1, 'activity-item-move-down').disabled).toBe(true);

    button(1, 'activity-item-move-up').click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(ids()).toEqual([22, 21]);
  });

  it('shows the started-sessions note only on the first change', async () => {
    await setup([textItem, visualItem, mcqItem]);

    button(0, 'activity-item-move-down').click();
    fixture.detectChanges();
    button(2, 'activity-item-move-up').click();
    fixture.detectChanges();

    const notes = snackBar.open.mock.calls.filter((c) => String(c[0]).startsWith('Bu değişiklik yalnız'));
    expect(notes.length).toBe(1);
  });

  it('while saving the list is busy and the arrows are inactive', async () => {
    await setup([textItem, visualItem, mcqItem]);
    const pending = new Subject<{ success: boolean }>();
    questions.reorderTestQuestions.mockReturnValue(pending);

    button(0, 'activity-item-move-down').click();
    fixture.detectChanges();

    expect(el().querySelector('ol')?.getAttribute('aria-busy')).toBe('true');
    expect(moreButton(1).getAttribute('aria-disabled')).toBe('true');
    const moveUp = button(1, 'activity-item-move-up');
    expect(moveUp.disabled).toBe(true);
    moveUp.click();
    expect(questions.reorderTestQuestions).toHaveBeenCalledTimes(1);

    pending.next({ success: true });
    pending.complete();
    fixture.detectChanges();
    expect(el().querySelector('ol')?.getAttribute('aria-busy')).toBe('false');
  });

  it('on error restores the old order and retries from the snackbar', async () => {
    await setup([textItem, visualItem]);
    questions.reorderTestQuestions.mockReturnValue(throwError(() => new Error('x')));

    button(0, 'activity-item-move-down').click();
    fixture.detectChanges();

    expect(ids()).toEqual([21, 22]);
    expect(snackBar.open).toHaveBeenCalledWith('Sıra kaydedilemedi', 'Tekrar dene', expect.anything());

    questions.reorderTestQuestions.mockReturnValue(of({ success: true }));
    snackAction.next();
    fixture.detectChanges();

    expect(ids()).toEqual([22, 21]);
    expect(questions.reorderTestQuestions).toHaveBeenLastCalledWith(7, [22, 21]);
  });

  it('read-only users get no handle or arrows', async () => {
    await setup([textItem, visualItem], false);

    expect(el().querySelector('[data-testid="activity-item-drag"]')).toBeNull();
    expect(el().querySelector('[data-testid="activity-item-more"]')).toBeNull();
  });

  // #857: normal test — soru listesi cevap harfi, "Cevap yok" uyarısı ve doğru cevap diyaloğu.
  const answers = [{ id: 501 }, { id: 502 }, { id: 503 }] as Question['answers'];
  const answered = question({ id: 31, imageUrl: 'https://img/a.png', answers, correctAnswerId: 502, categoryName: 'Matematik' });
  const unanswered = question({ id: 32, imageUrl: 'https://img/b.png', answers });

  it('test mode shows answer letters, a no-answer warning and edit on every row', async () => {
    await setup([answered, unanswered], true, 'test');

    expect(rows()[0].querySelector('[data-testid="test-item-answer"]')?.textContent).toContain('B');
    expect(rows()[1].querySelector('[data-testid="test-item-no-answer"]')).not.toBeNull();
    expect(el().querySelector('[data-testid="test-items-warnings"]')?.textContent).toContain('1');
    expect(rows().every((r) => r.querySelector('[data-testid="activity-item-edit"]'))).toBe(true);
    expect(rows()[0].textContent).toContain('Matematik');
  });

  it('test mode edit saves the picked answer and clears the warning', async () => {
    await setup([answered, unanswered], true, 'test');
    dialogResult = 503;

    (rows()[1].querySelector('[data-testid="activity-item-edit"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(questions.updateCorrectAnswer).toHaveBeenCalledWith(32, 503, 1);
    expect(rows()[1].querySelector('[data-testid="test-item-answer"]')?.textContent).toContain('C');
    expect(el().querySelector('[data-testid="test-items-warnings"]')).toBeNull();
    expect(navigate).not.toHaveBeenCalledWith(['/question', 32]);
  });

  it('test mode edit cancelled saves nothing', async () => {
    await setup([unanswered], true, 'test');
    dialogResult = undefined;

    (rows()[0].querySelector('[data-testid="activity-item-edit"]') as HTMLButtonElement).click();

    expect(questions.updateCorrectAnswer).not.toHaveBeenCalled();
  });
});
