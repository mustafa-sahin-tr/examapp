// Issue #586: kelime avı oynatıcısı — iki dokunuşla seçim, düz olmayan / tekrar çizgi, kaldırma, yankı, geri bildirim, çözüm.
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { QuestionRegion } from '../../../models/draws';
import { WordSearchFeedback } from '../../../models/word-search';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { QuestionCanvasWordSearchComponent } from './question-canvas-word-search.component';

const PLAN = {
  version: 1,
  type: 'wordSearch',
  rows: 4,
  cols: 5,
  letters: ['KALPX', 'EYZİM', 'DTARU', 'İNOKE'],
  words: [
    { id: 'w1', label: 'KALP' },
    { id: 'w2', label: 'KEDİ' },
  ],
};

const SOLUTION = { lines: { w1: { from: [0, 0], to: [0, 3] }, w2: { from: [0, 0], to: [3, 0] } } };

function region(): QuestionRegion {
  return { imageUrl: '', width: 0, height: 0, passage: null } as unknown as QuestionRegion;
}

const payload = (...lines: [number, number, number, number][]) =>
  JSON.stringify({ version: 1, type: 'wordSearch', lines: lines.map(([a, b, c, d]) => ({ from: [a, b], to: [c, d] })) });

describe('QuestionCanvasWordSearchComponent (#586)', () => {
  let fixture: ComponentFixture<QuestionCanvasWordSearchComponent>;
  let emitted: string[];

  const el = () => fixture.nativeElement as HTMLElement;
  const cell = (r: number, c: number) => el().querySelector(`[data-cell="${r},${c}"]`) as HTMLButtonElement;
  const q = (testId: string) => el().querySelector(`[data-testid="${testId}"]`) as HTMLElement | null;

  function setup(
    inputs: { answer?: string | null; feedback?: WordSearchFeedback | null; showSolution?: boolean; disabled?: boolean } = {}
  ): void {
    TestBed.configureTestingModule({ imports: [QuestionCanvasWordSearchComponent, translocoTestingModule()] });
    fixture = TestBed.createComponent(QuestionCanvasWordSearchComponent);
    emitted = [];
    fixture.componentInstance.answerPayloadChange.subscribe((p) => emitted.push(p));
    fixture.componentRef.setInput('questionRegion', region());
    fixture.componentRef.setInput('interactionPlanJson', JSON.stringify(PLAN));
    fixture.componentRef.setInput('interactionSolutionJson', JSON.stringify(SOLUTION));
    fixture.componentRef.setInput('answerPayloadJson', inputs.answer ?? null);
    fixture.componentRef.setInput('feedback', inputs.feedback ?? null);
    fixture.componentRef.setInput('showSolution', !!inputs.showSolution);
    fixture.componentRef.setInput('activityMode', true);
    fixture.componentRef.setInput('disabled', !!inputs.disabled);
    fixture.detectChanges();
  }

  function click(r: number, c: number) {
    cell(r, c).click();
    fixture.detectChanges();
  }

  it('rendersLettersAndWordList', () => {
    setup();
    expect(el().querySelectorAll('[data-cell]').length).toBe(20);
    expect(cell(1, 3).textContent?.trim()).toBe('İ');
    expect(el().querySelectorAll('[data-word]').length).toBe(2);
  });

  it('twoTaps_AddStraightLine_CrookedAndDuplicateIgnored', () => {
    setup();
    click(0, 0);
    expect(cell(0, 0).classList).toContain('is-preview');
    click(0, 3);
    expect(emitted).toEqual([payload([0, 0, 0, 3])]);
    expect(q('ws-picks')?.textContent).toContain('KALP');

    click(0, 0);
    click(1, 2); // düz değil
    expect(emitted.length).toBe(1);

    click(0, 3);
    click(0, 0); // aynı çizgi ters yönde
    expect(emitted.length).toBe(1);

    click(2, 2);
    click(2, 2); // başlangıca yeniden dokunmak vazgeçer
    expect(fixture.componentInstance.anchor()).toBeNull();
  });

  it('removeAndUndo', () => {
    setup({ answer: payload([0, 0, 0, 3], [0, 0, 3, 0]) });
    (el().querySelector('[data-testid="ws-picks"] button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(emitted.at(-1)).toBe(payload([0, 0, 3, 0]));
    (q('ws-undo') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(emitted.at(-1)).toBe(payload());
  });

  it('echoOfOwnPayload_DoesNotResetSelection', () => {
    setup();
    click(0, 0);
    click(3, 0);
    fixture.componentRef.setInput('answerPayloadJson', emitted.at(-1)!);
    fixture.detectChanges();
    expect(fixture.componentInstance.lines().length).toBe(1);
  });

  it('feedback_MarksWordsAndLines', () => {
    setup({
      answer: payload([0, 0, 0, 3], [1, 0, 1, 2]),
      feedback: { rows: [{ id: 'w1', correct: true }, { id: 'w2', correct: false }], lines: [true, false] },
    });
    expect(el().querySelector('[data-word="w1"]')!.classList).toContain('is-found');
    expect(el().querySelector('[data-word="w2"]')!.classList).toContain('is-missing');
    expect(el().querySelectorAll('line.is-ok').length).toBe(1);
    expect(el().querySelectorAll('line.is-bad').length).toBe(1);
    expect(q('ws-result')?.classList).toContain('is-bad');
  });

  it('showSolution_ComparesStudentLines_missedWordDashed', () => {
    // KALP ters yönden bulundu, ikinci çizgi kelime değil; KEDİ bulunamadı.
    setup({ answer: payload([0, 3, 0, 0], [1, 0, 1, 2]), showSolution: true });
    expect(el().querySelectorAll('line.is-ok').length).toBe(1);
    expect(el().querySelectorAll('line.is-bad').length).toBe(1);
    expect(el().querySelectorAll('line.is-missed').length).toBe(1);
    expect(el().querySelector('[data-word="w1"]')!.classList).toContain('is-found');
    expect(el().querySelector('[data-word="w2"]')!.classList).toContain('is-missing');
    expect(q('ws-solution-hint')!.textContent).toContain('kesikli');
  });

  it('showSolution_DrawsWordLines_ReadOnly', () => {
    setup({ showSolution: true });
    expect(el().querySelectorAll('line.is-solution').length).toBe(2);
    expect(cell(0, 0).disabled).toBe(true);
    expect(q('ws-undo')).toBeNull();
  });

  it('disabledUndoAndReset_KeepMaterialDisabledLook_NotGlobalGrayFill', () => {
    // Global `button:disabled` gri zemini çerçeveli Material butonunu ezince açık pasif yazı okunmuyordu.
    setup();
    for (const id of ['ws-undo', 'ws-reset']) {
      const button = q(id) as HTMLButtonElement;
      expect(button.disabled).toBe(true);
      expect(getComputedStyle(button).backgroundColor, id).not.toBe('rgb(204, 204, 204)');
    }
    const plain = document.createElement('button');
    plain.disabled = true;
    document.body.appendChild(plain);
    try {
      expect(getComputedStyle(plain).backgroundColor).toBe('rgb(204, 204, 204)');
    } finally {
      plain.remove();
    }
  });
});
