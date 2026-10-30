// Issue #585: yol oyunları oynatıcısı — dal / kapı / kutu seçimi ile yol kurma, geri al / baştan, yankı, sapma gösterimi,
// doğru yol (çözüm), taş işaretleme.
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { QuestionRegion } from '../../../models/draws';
import { MazeFeedback } from '../../../models/maze-tree';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { QuestionCanvasMazeTreeComponent } from './question-canvas-maze-tree.component';

const TREE = {
  version: 1,
  type: 'mazeTree',
  variant: 'tree',
  start: 'n1',
  nodes: [
    { id: 'n1', label: 'Kök' },
    { id: 'n2', label: 'Sol' },
    { id: 'n3', label: 'Sağ' },
    { id: 'x1', label: '1. çıkış', end: true },
    { id: 'x2', label: '2. çıkış', end: true },
    { id: 'x3', label: '3. çıkış', end: true },
    { id: 'x4', label: '4. çıkış', end: true },
  ],
  edges: [
    { id: 'e1', from: 'n1', to: 'n2', label: 'D' },
    { id: 'e2', from: 'n1', to: 'n3', label: 'Y' },
    { id: 'e3', from: 'n2', to: 'x1', label: 'D' },
    { id: 'e4', from: 'n2', to: 'x2', label: 'Y' },
    { id: 'e5', from: 'n3', to: 'x3', label: 'D' },
    { id: 'e6', from: 'n3', to: 'x4', label: 'Y' },
  ],
};

const BOARD = {
  version: 1,
  type: 'mazeTree',
  variant: 'board',
  start: 'r1',
  nodes: [{ id: 'r1', label: 'Gaz' }, { id: 'r2', label: 'Katı' }, { id: 'f', label: 'Bitiş', end: true }],
  edges: [
    { id: 'b1', from: 'r1', to: 'r2', label: 'Sıkıştırılamaz.' },
    { id: 'b2', from: 'r1', to: 'r2', label: 'Akışkandır.' },
    { id: 'b3', from: 'r2', to: 'f', label: 'Belirli şekli vardır.' },
    { id: 'b4', from: 'r2', to: 'f', label: 'Akışkandır.' },
  ],
};

const ROOMS = {
  version: 1,
  type: 'mazeTree',
  variant: 'rooms',
  start: 'a',
  nodes: [
    { id: 'a', label: 'A', row: 0, col: 0 },
    { id: 'b', label: 'B', row: 0, col: 1 },
    { id: 'd', label: 'Bitiş', row: 1, col: 1, end: true },
  ],
  edges: [
    { id: 'd1', from: 'a', to: 'b', label: '24' },
    { id: 'd3', from: 'b', to: 'd', label: '12' },
  ],
};

const STONES = {
  version: 1,
  type: 'mazeTree',
  variant: 'stones',
  nodes: [
    { id: 's1', label: '5’er 5’er dağıttı.' },
    { id: 's2', label: 'onbeşbin' },
  ],
};

function region(): QuestionRegion {
  return { imageUrl: '', width: 0, height: 0, passage: null } as unknown as QuestionRegion;
}

const path = (...edges: string[]) => JSON.stringify({ version: 1, type: 'mazeTree', path: edges });

describe('QuestionCanvasMazeTreeComponent (#585)', () => {
  let fixture: ComponentFixture<QuestionCanvasMazeTreeComponent>;
  let emitted: string[];

  const el = () => fixture.nativeElement as HTMLElement;
  const edge = (id: string) => el().querySelector(`[data-edge="${id}"]`) as HTMLButtonElement;
  const node = (id: string) => el().querySelector(`[data-node="${id}"]`) as HTMLElement;
  const q = (testId: string) => el().querySelector(`[data-testid="${testId}"]`) as HTMLElement | null;

  function setup(
    plan: object,
    inputs: { answer?: string | null; feedback?: MazeFeedback | null; solution?: object | null; showSolution?: boolean; disabled?: boolean } = {}
  ): void {
    TestBed.configureTestingModule({ imports: [QuestionCanvasMazeTreeComponent, translocoTestingModule()] });
    fixture = TestBed.createComponent(QuestionCanvasMazeTreeComponent);
    emitted = [];
    fixture.componentInstance.answerPayloadChange.subscribe((p) => emitted.push(p));
    fixture.componentRef.setInput('questionRegion', region());
    fixture.componentRef.setInput('interactionPlanJson', JSON.stringify(plan));
    fixture.componentRef.setInput('interactionSolutionJson', inputs.solution ? JSON.stringify(inputs.solution) : null);
    fixture.componentRef.setInput('answerPayloadJson', inputs.answer ?? null);
    fixture.componentRef.setInput('feedback', inputs.feedback ?? null);
    fixture.componentRef.setInput('showSolution', !!inputs.showSolution);
    fixture.componentRef.setInput('activityMode', true);
    fixture.componentRef.setInput('disabled', !!inputs.disabled);
    fixture.detectChanges();
  }

  function click(button: HTMLElement) {
    button.click();
    fixture.detectChanges();
  }

  it('tree_OnlyBranchesOfCurrentNodeAreEnabled_AndChoosingMovesOn', () => {
    setup(TREE);
    expect(edge('e1').disabled).toBe(false);
    expect(edge('e2').disabled).toBe(false);
    expect(edge('e3').disabled).toBe(true);
    expect(node('n1').classList).toContain('is-current');

    click(edge('e1'));
    expect(emitted).toEqual([path('e1')]);
    expect(node('n2').classList).toContain('is-current');
    expect(el().querySelector('.mz-token')).not.toBeNull();
    expect(edge('e1').classList).toContain('is-chosen');
    expect(edge('e2').disabled).toBe(true);
    expect(edge('e4').disabled).toBe(false);

    click(edge('e4'));
    expect(emitted.at(-1)).toBe(path('e1', 'e4'));
    expect(edge('e3').disabled).toBe(true); // bitişte seçim yok
    expect(q('mz-status')?.textContent).toContain('2. çıkış');
  });

  it('tree_CheckedEnd_GreenWhenReached_RedWhenPathDeviated', () => {
    setup(TREE, { answer: path('e1', 'e3') });
    fixture.componentRef.setInput('feedback', {
      rows: [
        { id: 'n1', correct: true },
        { id: 'n2', correct: true },
      ],
      status: 'reached',
      at: 'x1',
    });
    fixture.detectChanges();
    expect(node('x1').classList).toContain('is-reached');
    expect(node('x1').classList).not.toContain('is-wrong-end');

    fixture.componentRef.setInput('feedback', {
      rows: [
        { id: 'n1', correct: true },
        { id: 'n2', correct: false },
      ],
      status: 'deviated',
      at: 'n2',
    });
    fixture.detectChanges();
    expect(node('x1').classList).not.toContain('is-reached');
    expect(node('x1').classList).toContain('is-wrong-end');
  });

  it('tree_UndoAndReset', () => {
    setup(TREE, { answer: path('e1', 'e4') });
    click(q('mz-undo')!);
    expect(emitted.at(-1)).toBe(path('e1'));
    click(q('mz-reset')!);
    expect(emitted.at(-1)).toBe(path());
    expect((q('mz-undo') as HTMLButtonElement).disabled).toBe(true);
  });

  it('echoOfOwnPayload_DoesNotResetPath', () => {
    setup(TREE);
    click(edge('e1'));
    click(edge('e4'));
    // Ebeveyn ilk yayını geç geri verir: ekrandaki iki adımlık yol korunur.
    fixture.componentRef.setInput('answerPayloadJson', emitted.at(-1));
    fixture.detectChanges();
    expect(fixture.componentInstance.path()).toEqual(['e1', 'e4']);
  });

  it('deviationFeedback_MarksNodeAndWrongBranch', () => {
    setup(TREE, {
      answer: path('e2', 'e5'),
      feedback: { rows: [{ id: 'n1', correct: false }], status: 'deviated', at: 'n1' },
    });
    expect(node('n1').classList).toContain('is-deviated');
    expect(node('n1').textContent).toContain('Burada yanlış yöne saptın');
    expect(edge('e2').classList).toContain('is-wrong');
    expect(q('mz-result')?.getAttribute('data-status')).toBe('deviated');
  });

  it('showSolution_HighlightsRouteAndLocksPath', () => {
    setup(TREE, { answer: path('e2'), solution: { choices: { n1: 'e1', n2: 'e4', n3: 'e5' } }, showSolution: true });
    expect(edge('e1').classList).toContain('is-route');
    expect(edge('e4').classList).toContain('is-route');
    expect(edge('e2').classList).not.toContain('is-route');
    expect(node('x2').classList).toContain('is-route');
    expect(edge('e5').disabled).toBe(true);
    expect(q('mz-undo')).toBeNull();
  });

  it('board_OnlyActiveRowSelectable_AndChosenBoxLights', () => {
    setup(BOARD);
    expect(edge('b1').disabled).toBe(false);
    expect(edge('b3').disabled).toBe(true);
    click(edge('b2'));
    expect(edge('b2').classList).toContain('is-lit');
    expect(edge('b1').disabled).toBe(true);
    expect(edge('b3').disabled).toBe(false);
    // Bitiş en üstte, ilk satır en altta.
    const rows = Array.from(el().querySelectorAll('.mz-board__row')).map((r) => r.getAttribute('data-node'));
    expect(rows).toEqual(['r2', 'r1']);
  });

  it('rooms_DoorsWorkBothWays', () => {
    setup(ROOMS);
    click(edge('d1'));
    expect(node('b').classList).toContain('is-current');
    expect(edge('d1').disabled).toBe(false); // geri dönülebilir
    click(edge('d1'));
    expect(node('a').classList).toContain('is-current');
    expect(emitted.at(-1)).toBe(path('d1', 'd1'));
  });

  it('stones_ToggleMarks_AndShowPerStoneFeedback', () => {
    setup(STONES);
    click(node('s2'));
    expect(emitted.at(-1)).toBe(JSON.stringify({ version: 1, type: 'mazeTree', marked: ['s2'] }));
    expect(node('s2').getAttribute('aria-pressed')).toBe('true');
    click(node('s2'));
    expect(emitted.at(-1)).toBe(JSON.stringify({ version: 1, type: 'mazeTree', marked: [] }));

    fixture.componentRef.setInput('feedback', {
      rows: [
        { id: 's1', correct: true },
        { id: 's2', correct: false },
      ],
      status: 'deviated',
      at: 's2',
    });
    fixture.detectChanges();
    expect(node('s1').classList).toContain('is-ok');
    expect(node('s2').classList).toContain('is-bad');
    expect(node('s2').classList).toContain('is-first-wrong');
    expect(node('s2').textContent).toContain('yanlış');
  });

  it('rooms_PathLengthCapStopsFurtherSteps', () => {
    const back = Array.from({ length: 128 }, () => 'd1');
    setup(ROOMS, { answer: path(...back) });
    expect(fixture.componentInstance.path()).toHaveLength(128);
    expect(edge('d1').disabled).toBe(true);
    expect(edge('d3').disabled).toBe(true);
  });

  it('disabled_BlocksChoices', () => {
    setup(TREE, { disabled: true });
    expect(edge('e1').disabled).toBe(true);
    click(edge('e1'));
    expect(emitted).toEqual([]);
  });
});
