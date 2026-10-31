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
    inputs: { answer?: string | null; feedback?: LogicGridFeedback | null; showSolution?: boolean; plan?: object; solution?: object } = {}
  ): void {
    TestBed.configureTestingModule({ imports: [QuestionCanvasLogicGridComponent, translocoTestingModule()] });
    fixture = TestBed.createComponent(QuestionCanvasLogicGridComponent);
    emitted = [];
    fixture.componentInstance.answerPayloadChange.subscribe((p) => emitted.push(p));
    fixture.componentRef.setInput('questionRegion', region());
    fixture.componentRef.setInput('interactionPlanJson', JSON.stringify(inputs.plan ?? LOGIC_PLAN));
    fixture.componentRef.setInput('interactionSolutionJson', JSON.stringify(inputs.solution ?? SOLUTION));
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

  // İki kategori: merdiven tablo (Spor satırları × Renk sütunları karalama bloğu).
  const STAIR_PLAN = {
    ...LOGIC_PLAN,
    categories: [
      ...LOGIC_PLAN.categories,
      {
        id: 'c2',
        label: 'Spor',
        items: [
          { id: 'o4', label: 'Futbol' },
          { id: 'o5', label: 'Yüzme' },
          { id: 'o6', label: 'Tenis' },
        ],
      },
    ],
  };
  const STAIR_SOLUTION = { assign: { p1: { c1: 'o3', c2: 'o4' }, p2: { c1: 'o2', c2: 'o6' }, p3: { c1: 'o1', c2: 'o5' } } };

  it('staircase_CrossBlock_AutoCrossesWithinBlock_SavedAsNotes', () => {
    setup({ plan: STAIR_PLAN, solution: STAIR_SOLUTION });
    // Konu × (Renk + Spor) = 18, Spor × Renk = 9.
    expect(el().querySelectorAll('[data-cell]').length).toBe(27);
    expect(el().querySelector('[data-group="c2"]')).not.toBeNull();
    expect(cell('o4', 'o4')).toBeNull(); // Spor × Spor bloğu yok

    click('o4', 'o3'); // Futbol – Yeşil ✓
    expect(cell('o4', 'o1').classList).toContain('is-no');
    expect(cell('o5', 'o3').classList).toContain('is-no');
    expect(cell('p1', 'o3').classList).not.toContain('is-no'); // başka blok etkilenmez
    expect(JSON.parse(emitted.at(-1)!)).toEqual({ version: 1, type: 'logicGrid', cells: [], notes: { 'c2:c1': 'nny..n..n' } });
  });

  it('staircase_NotesRestore_And_ConflictInCrossBlock', () => {
    const answer = JSON.stringify({ version: 1, type: 'logicGrid', cells: [], notes: { 'c2:c1': 'yy.......' } });
    setup({ plan: STAIR_PLAN, solution: STAIR_SOLUTION, answer });
    expect(cell('o4', 'o1').classList).toContain('is-yes');
    expect(cell('o4', 'o2').classList).toContain('is-conflict');
  });

  it('summary_FillsFromTopTicks_ShowsSolution', () => {
    setup({ plan: STAIR_PLAN, solution: STAIR_SOLUTION, answer: cells(['p1', 'o3', 'yes'], ['p1', 'o4', 'yes'], ['p2', 'o1', 'yes'], ['p2', 'o2', 'yes']) });
    const summary = (row: string, cat: string) => el().querySelector(`[data-summary="${row}:${cat}"]`)?.textContent?.trim();
    expect(summary('p1', 'c1')).toBe('Yeşil');
    expect(summary('p1', 'c2')).toBe('Futbol');
    expect(summary('p2', 'c1')).toBe('?');
    expect(summary('p3', 'c1')).toBe('');

    TestBed.resetTestingModule();
    setup({ plan: STAIR_PLAN, solution: STAIR_SOLUTION, showSolution: true });
    expect(summary('p3', 'c2')).toBe('Yüzme');
    // Karalama bloğunda da çözüm: Tenis ↔ Kırmızı (Ayşe).
    expect(cell('o6', 'o2').classList).toContain('is-yes');
  });

  it('authoring_ShowsSolutionReadOnly_HeaderControlsEmit_NoStudentChrome', () => {
    setup({ plan: STAIR_PLAN, solution: STAIR_SOLUTION });
    fixture.componentRef.setInput('authoring', true);
    fixture.detectChanges();
    const moves: unknown[] = [];
    const shuffles: number[] = [];
    fixture.componentInstance.optionMove.subscribe((e) => moves.push(e));
    fixture.componentInstance.categoryShuffle.subscribe((c) => shuffles.push(c));

    expect(cell('p1', 'o3').classList).toContain('is-yes');
    expect(cell('o6', 'o2').classList).toContain('is-yes');
    expect(cell('p1', 'o1').disabled).toBe(true);
    expect(el().querySelector('.lg-clues')).toBeNull();
    expect(q('lg-summary')).toBeNull();
    expect(q('lg-reset')).toBeNull();
    expect((el().querySelector('[data-move="0:0:-1"]') as HTMLButtonElement).disabled).toBe(true);

    (el().querySelector('[data-move="1:0:1"]') as HTMLButtonElement).click();
    (el().querySelector('[data-shuffle="0"]') as HTMLButtonElement).click();
    expect(moves).toEqual([{ category: 1, position: 0, delta: 1 }]);
    expect(shuffles).toEqual([0]);
  });
});
