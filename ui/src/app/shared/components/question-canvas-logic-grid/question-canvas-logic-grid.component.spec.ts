// Issue #586: mantık tablosu oynatıcısı — işaret döngüsü, otomatik ✗, çakışma uyarısı, yankı, geri bildirim, çözüm.
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { QuestionRegion } from '../../../models/draws';
import { LogicGridFeedback } from '../../../models/logic-grid';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { QuestionCanvasLogicGridComponent } from './question-canvas-logic-grid.component';

const LOGIC_PLAN = {
  version: 1,
  type: 'logicGrid',
  subject: {
    label: 'Çocuk',
    items: [
      { id: 'p1', label: 'Ali' },
      { id: 'p2', label: 'Ayşe' },
      { id: 'p3', label: 'Can' },
    ],
  },
  categories: [
    {
      id: 'c1',
      label: 'Renk',
      items: [
        { id: 'o1', label: 'Mavi' },
        { id: 'o2', label: 'Kırmızı' },
        { id: 'o3', label: 'Yeşil' },
      ],
    },
  ],
  clues: [{ id: 'q1', text: 'Ali maviyi sevmez.' }],
};

const SOLUTION = { assign: { p1: { c1: 'o3' }, p2: { c1: 'o2' }, p3: { c1: 'o1' } } };

function region(): QuestionRegion {
  return { imageUrl: '', width: 0, height: 0, passage: null } as unknown as QuestionRegion;
}

const cells = (...list: [string, string, 'yes' | 'no'][]) =>
  JSON.stringify({ version: 1, type: 'logicGrid', cells: list.map(([row, col, mark]) => ({ row, col, mark })) });

describe('QuestionCanvasLogicGridComponent (#586)', () => {
  let fixture: ComponentFixture<QuestionCanvasLogicGridComponent>;
  let emitted: string[];

  const el = () => fixture.nativeElement as HTMLElement;
  const cell = (row: string, col: string) => el().querySelector(`[data-cell="${row}:${col}"]`) as HTMLButtonElement;
  const q = (testId: string) => el().querySelector(`[data-testid="${testId}"]`) as HTMLElement | null;

  function setup(
    inputs: { answer?: string | null; feedback?: LogicGridFeedback | null; showSolution?: boolean } = {}
  ): void {
    TestBed.configureTestingModule({ imports: [QuestionCanvasLogicGridComponent, translocoTestingModule()] });
    fixture = TestBed.createComponent(QuestionCanvasLogicGridComponent);
    emitted = [];
    fixture.componentInstance.answerPayloadChange.subscribe((p) => emitted.push(p));
    fixture.componentRef.setInput('questionRegion', region());
    fixture.componentRef.setInput('interactionPlanJson', JSON.stringify(LOGIC_PLAN));
    fixture.componentRef.setInput('interactionSolutionJson', JSON.stringify(SOLUTION));
    fixture.componentRef.setInput('answerPayloadJson', inputs.answer ?? null);
    fixture.componentRef.setInput('feedback', inputs.feedback ?? null);
    fixture.componentRef.setInput('showSolution', !!inputs.showSolution);
    fixture.componentRef.setInput('activityMode', true);
    fixture.detectChanges();
  }

  function click(row: string, col: string) {
    cell(row, col).click();
    fixture.detectChanges();
  }

  it('rendersTableWithCluesAndCells', () => {
    setup();
    expect(el().querySelectorAll('[data-cell]').length).toBe(9);
    expect(el().querySelector('.lg-clues')?.textContent).toContain('Ali maviyi sevmez.');
  });

  it('cycle_TickAutoCrosses_ThenCrossThenEmpty', () => {
    setup();
    click('p1', 'o3');
    expect(cell('p1', 'o3').classList).toContain('is-yes');
    expect(cell('p1', 'o1').classList).toContain('is-no');
    expect(cell('p2', 'o3').classList).toContain('is-no');
    expect(emitted.at(-1)).toBe(cells(['p1', 'o1', 'no'], ['p1', 'o2', 'no'], ['p1', 'o3', 'yes'], ['p2', 'o3', 'no'], ['p3', 'o3', 'no']));

    click('p1', 'o3');
    expect(cell('p1', 'o3').classList).toContain('is-no');
    click('p1', 'o3');
    expect(cell('p1', 'o3').classList).not.toContain('is-no');
  });

  it('autoCrossOff_And_ConflictWarning', () => {
    setup();
    fixture.componentInstance.autoCross.set(false);
    click('p1', 'o1');
    click('p1', 'o2');
    expect(cell('p1', 'o1').classList).toContain('is-conflict');
    expect(q('lg-conflict')).not.toBeNull();
  });

  it('echoOfOwnPayload_DoesNotResetMarks', () => {
    setup();
    click('p2', 'o2');
    fixture.componentRef.setInput('answerPayloadJson', emitted.at(-1)!);
    fixture.detectChanges();
    expect(cell('p2', 'o2').classList).toContain('is-yes');
  });

  it('feedback_MarksPairs', () => {
    setup({
      answer: cells(['p1', 'o3', 'yes'], ['p2', 'o1', 'yes']),
      feedback: { rows: [{ id: 'p1:c1', correct: true }, { id: 'p2:c1', correct: false }, { id: 'p3:c1', correct: false }] },
    });
    expect(cell('p1', 'o3').parentElement!.classList).toContain('is-pair-ok');
    expect(cell('p2', 'o1').parentElement!.classList).toContain('is-pair-bad');
    expect(q('lg-result')?.classList).toContain('is-bad');
  });

  it('showSolution_ShowsCorrectTicks_ReadOnly', () => {
    setup({ answer: cells(['p1', 'o1', 'yes']), showSolution: true });
    expect(cell('p1', 'o3').classList).toContain('is-yes');
    expect(cell('p1', 'o1').classList).not.toContain('is-yes');
    expect(cell('p1', 'o1').disabled).toBe(true);
    expect(q('lg-reset')).toBeNull();
  });
});
