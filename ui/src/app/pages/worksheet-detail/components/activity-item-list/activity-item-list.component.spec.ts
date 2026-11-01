import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of, throwError } from 'rxjs';
import { MockedObject, vi } from 'vitest';

import { ActivityItemListComponent, toRows } from './activity-item-list.component';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../../../models/locale';
import { Question } from '../../../../models/question';
import { QuestionService } from '../../../../services/question.service';
import rootTr from '../../../../../../public/i18n/tr.json';
import worksheetDetailTr from '../../../../../../public/i18n/worksheet-detail/tr.json';

const translocoTesting = TranslocoTestingModule.forRoot({
  langs: { tr: { ...rootTr, 'worksheet-detail': worksheetDetailTr }, 'worksheet-detail/tr': worksheetDetailTr },
  translocoConfig: { availableLangs: [...SUPPORTED_LOCALE_CODES], defaultLang: DEFAULT_LOCALE, scopes: { keepCasing: true } },
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
    expect(rows[0]).toMatchObject({ formEditable: true, visual: false, preview: 'Büyüme sırasına diziniz.', imageUrl: null });
    expect(rows[1]).toMatchObject({ formEditable: false, visual: true, preview: 'Boyayınız' });
    expect(rows[2]).toMatchObject({ formEditable: false, visual: true, preview: '2 + 2 = ?' });
  });
});

describe('ActivityItemListComponent (#684)', () => {
  let fixture: ComponentFixture<ActivityItemListComponent>;
  let questions: MockedObject<QuestionService>;
  let dialogResult: boolean | undefined;
  const navigate = vi.fn();

  const el = () => fixture.nativeElement as HTMLElement;
  const rows = () => Array.from(el().querySelectorAll('[data-testid="activity-item-row"]'));

  async function setup(list: Question[] | 'error', canEdit = true): Promise<void> {
    questions = {
      getAll: vi.fn().mockReturnValue(list === 'error' ? throwError(() => new Error('x')) : of(list)),
      removeQuestionFromTest: vi.fn().mockReturnValue(of({ success: true })),
    } as unknown as MockedObject<QuestionService>;
    dialogResult = true;
    TestBed.configureTestingModule({
      imports: [ActivityItemListComponent, translocoTesting],
      providers: [
        { provide: QuestionService, useValue: questions },
        { provide: MatDialog, useValue: { open: vi.fn(() => ({ afterClosed: () => of(dialogResult) })) } },
        { provide: MatSnackBar, useValue: { open: vi.fn() } },
        { provide: Router, useValue: { navigate } },
      ],
    });
    fixture = TestBed.createComponent(ActivityItemListComponent);
    fixture.componentRef.setInput('worksheetId', 7);
    fixture.componentRef.setInput('gradeId', 5);
    fixture.componentRef.setInput('canEdit', canEdit);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('lists items with type badge, visual hint and edit only on form-editable items', async () => {
    await setup([textItem, visualItem]);

    expect(rows().length).toBe(2);
    expect(rows()[0].textContent).toContain('Sürükle-yerleştir');
    expect(rows()[0].querySelector('[data-testid="activity-item-edit"]')).not.toBeNull();
    expect(rows()[1].textContent).toContain('Görselli');
    expect(rows()[1].querySelector('[data-testid="activity-item-edit"]')).toBeNull();
    expect(rows()[1].querySelector('[data-testid="activity-item-visual-hint"]')?.textContent).toContain('Görselli, düzenlenemez');
    expect(rows()[0].querySelector('[data-testid="activity-item-remove"]')?.getAttribute('aria-label')).toBe("Madde 1'i kaldır");
  });

  it('is read-only without edit permission', async () => {
    await setup([textItem, visualItem], false);

    expect(el().querySelector('[data-testid="activity-item-edit"]')).toBeNull();
    expect(el().querySelector('[data-testid="activity-item-remove"]')).toBeNull();
    expect(el().querySelector('[data-testid="activity-item-visual-hint"]')).toBeNull();
  });

  it('edit opens the text-item editor for that question', async () => {
    await setup([textItem]);

    (el().querySelector('[data-testid="activity-item-edit"]') as HTMLButtonElement).click();

    expect(navigate).toHaveBeenCalledWith(['/activity-editor'], { queryParams: { testId: 7, gradeId: 5, questionId: 21 } });
  });

  it('remove after confirm deletes, renumbers and emits', async () => {
    await setup([textItem, visualItem, mcqItem]);
    const removed = vi.fn();
    fixture.componentInstance.removed.subscribe(removed);

    (rows()[0].querySelector('[data-testid="activity-item-remove"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(questions.removeQuestionFromTest).toHaveBeenCalledWith(7, 21);
    expect(removed).toHaveBeenCalledWith(21);
    expect(rows().map((r) => r.querySelector('.ail__no')?.textContent?.trim())).toEqual(['1', '2']);
    expect(rows()[0].textContent).toContain('Boyama');
  });

  it('cancelled confirm does not remove', async () => {
    await setup([textItem]);
    dialogResult = false;

    (el().querySelector('[data-testid="activity-item-remove"]') as HTMLButtonElement).click();

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
});
