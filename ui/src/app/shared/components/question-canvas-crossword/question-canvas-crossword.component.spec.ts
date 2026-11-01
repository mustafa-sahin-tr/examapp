// Issue #586: çengel bulmaca oynatıcısı — yazınca ilerleme, yön değiştirme, Backspace, yankı, geri bildirim, çözüm gösterimi.
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CrosswordFeedback } from '../../../models/crossword';
import { QuestionRegion } from '../../../models/draws';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { QuestionCanvasCrosswordComponent } from './question-canvas-crossword.component';

// K A L P
// İ . . .
// R A F .
const PLAN = {
  version: 1,
  type: 'crossword',
  rows: 3,
  cols: 4,
  clues: [
    { id: 'k1', dir: 'across', row: 0, col: 0, length: 4, number: 1, text: 'Organ' },
    { id: 'k2', dir: 'down', row: 0, col: 0, length: 3, number: 1, text: 'Kirli' },
    { id: 'k3', dir: 'across', row: 2, col: 0, length: 3, number: 2, text: 'Raf' },
  ],
};

const SOLUTION = { answers: { k1: 'KALP', k2: 'KİR', k3: 'RAF' } };

function region(): QuestionRegion {
  return { imageUrl: '', width: 0, height: 0, passage: null } as unknown as QuestionRegion;
}

const grid = (...lines: string[]) => JSON.stringify({ version: 1, type: 'crossword', grid: lines });

describe('QuestionCanvasCrosswordComponent (#586)', () => {
  let fixture: ComponentFixture<QuestionCanvasCrosswordComponent>;
  let emitted: string[];

  const el = () => fixture.nativeElement as HTMLElement;
  const cell = (r: number, c: number) => el().querySelector(`input[data-cell="${r},${c}"]`) as HTMLInputElement;
  const q = (testId: string) => el().querySelector(`[data-testid="${testId}"]`) as HTMLElement | null;

  function setup(
    inputs: { answer?: string | null; feedback?: CrosswordFeedback | null; showSolution?: boolean; disabled?: boolean } = {}
  ): void {
    TestBed.configureTestingModule({ imports: [QuestionCanvasCrosswordComponent, translocoTestingModule()] });
    fixture = TestBed.createComponent(QuestionCanvasCrosswordComponent);
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

  function type(input: HTMLInputElement, value: string) {
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function key(input: HTMLInputElement, k: string) {
    input.dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true }));
    fixture.detectChanges();
  }

  it('rendersGridWithBlackCellsAndNumbers', () => {
    setup();
    expect(el().querySelectorAll('input[data-cell]').length).toBe(8);
    expect(el().querySelectorAll('.cw-cell--block').length).toBe(4);
    expect(el().querySelector('.cw-cell__num')?.textContent?.trim()).toBe('1');
    expect(el().querySelectorAll('[data-clue]').length).toBe(3);
  });

  it('typing_UppercasesTurkish_MovesAlongActiveClue_AndEmits', () => {
    setup();
    cell(0, 0).focus();
    fixture.detectChanges();
    type(cell(0, 0), 'k');
    expect(document.activeElement).toBe(cell(0, 1));
    type(cell(0, 1), 'a');
    expect(emitted.at(-1)).toBe(grid('KA  ', '    ', '    '));
    expect(cell(0, 0).value).toBe('K');
  });

  it('spaceTogglesDirection_ThenTypingGoesDown', () => {
    setup();
    cell(0, 0).focus();
    fixture.detectChanges();
    key(cell(0, 0), ' ');
    expect(q('cw-current')?.textContent).toContain('Kirli');
    type(cell(0, 0), 'k');
    expect(document.activeElement).toBe(cell(1, 0));
    type(cell(1, 0), 'i');
    expect(emitted.at(-1)).toBe(grid('K   ', 'İ   ', '    '));
  });

  it('backspaceOnEmptyCell_MovesBackAndClears', () => {
    setup({ answer: grid('KA  ', '    ', '    ') });
    cell(0, 2).focus();
    fixture.detectChanges();
    key(cell(0, 2), 'Backspace');
    expect(document.activeElement).toBe(cell(0, 1));
    expect(emitted.at(-1)).toBe(grid('K   ', '    ', '    '));
  });

  it('echoOfOwnPayload_DoesNotResetGrid', () => {
    setup();
    cell(0, 0).focus();
    type(cell(0, 0), 'K');
    fixture.componentRef.setInput('answerPayloadJson', emitted.at(-1)!);
    fixture.detectChanges();
    expect(cell(0, 0).value).toBe('K');
  });

  it('feedback_MarksWordsAndShowsSummary', () => {
    setup({
      answer: grid('KALP', 'I   ', 'RAF '),
      feedback: { rows: [{ id: 'k1', correct: true }, { id: 'k2', correct: false }, { id: 'k3', correct: true }] },
    });
    expect(cell(0, 0).parentElement!.classList).toContain('is-ok');
    expect(cell(1, 0).parentElement!.classList).toContain('is-bad');
    expect(el().querySelector('[data-clue="k2"]')!.classList).toContain('is-bad');
    expect(q('cw-result')?.classList).toContain('is-bad');
  });

  it('showSolution_ComparesStudentLetters_wrongCellShowsCorrectLetter', () => {
    setup({ answer: grid('KALP', 'I   ', '    '), showSolution: true });
    // Öğrencinin harfi kalır; yanlış hücrede doğru harf köşede, boş hücre de kırmızı.
    expect(cell(1, 0).value).toBe('I');
    expect(cell(1, 0).readOnly).toBe(true);
    expect(cell(1, 0).parentElement!.classList).toContain('is-bad');
    expect(el().querySelector('[data-target="1,0"]')!.textContent).toContain('İ');
    expect(cell(0, 0).parentElement!.classList).toContain('is-ok');
    expect(el().querySelector('[data-target="0,0"]')).toBeNull();
    expect(q('cw-reset')).toBeNull();
  });

  it('showSolution_WithoutAnswer_ShowsSolutionLetters', () => {
    setup({ showSolution: true });
    expect(cell(1, 0).value).toBe('İ');
    expect(el().querySelector('[data-target]')).toBeNull();
  });

  it('disabled_IgnoresTyping', () => {
    setup({ disabled: true });
    cell(0, 0).focus();
    type(cell(0, 0), 'K');
    expect(emitted).toEqual([]);
  });
});
