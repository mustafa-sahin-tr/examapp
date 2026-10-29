// Issue #588: sayı doğrusu oynatıcısı — kart seçip çentiğe koyma, doğruya dokununca en yakın çentik, ok tuşuyla kaydırma, havuza geri,
// aynı çentikte üst üste kart, yankı, geri bildirim, çözüm.
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { QuestionRegion } from '../../../models/draws';
import { NumberLineFeedback } from '../../../models/number-line';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { QuestionCanvasNumberLineComponent } from './question-canvas-number-line.component';

const PLAN = {
  version: 1,
  type: 'numberLine',
  divisions: 4,
  labels: [
    { at: 0, text: '0' },
    { at: 4, text: '1' },
  ],
  cards: [
    { id: 'k1', label: '1/4' },
    { id: 'k2', label: '2/4' },
    { id: 'k3', label: '5/4' },
  ],
};

const SOLUTION = { positions: { k1: 1, k2: 2, k3: null } };

function region(): QuestionRegion {
  return { imageUrl: '', width: 0, height: 0, passage: null } as unknown as QuestionRegion;
}

const answer = (placed: Record<string, number>) => JSON.stringify({ version: 1, type: 'numberLine', placed });

describe('QuestionCanvasNumberLineComponent (#588)', () => {
  let fixture: ComponentFixture<QuestionCanvasNumberLineComponent>;
  let emitted: string[];

  const el = () => fixture.nativeElement as HTMLElement;
  const q = (testId: string) => el().querySelector(`[data-testid="${testId}"]`) as HTMLElement | null;
  const poolCard = (id: string) => q('nl-pool')!.querySelector(`[data-card="${id}"]`) as HTMLButtonElement | null;
  const placedCard = (id: string) => q('nl-track')!.querySelector(`[data-card="${id}"]`) as HTMLButtonElement | null;
  const tick = (at: number) => el().querySelector(`[data-tick="${at}"]`) as HTMLButtonElement;
  const stack = (at: number) => el().querySelector(`[data-stack="${at}"]`) as HTMLElement | null;
  const last = () => JSON.parse(emitted.at(-1)!).placed as Record<string, number>;

  function setup(
    inputs: { plan?: object; answer?: string | null; feedback?: NumberLineFeedback | null; showSolution?: boolean } = {}
  ): void {
    TestBed.configureTestingModule({ imports: [QuestionCanvasNumberLineComponent, translocoTestingModule()] });
    fixture = TestBed.createComponent(QuestionCanvasNumberLineComponent);
    emitted = [];
    fixture.componentInstance.answerPayloadChange.subscribe((p) => emitted.push(p));
    fixture.componentRef.setInput('questionRegion', region());
    fixture.componentRef.setInput('interactionPlanJson', JSON.stringify(inputs.plan ?? PLAN));
    fixture.componentRef.setInput('interactionSolutionJson', JSON.stringify(SOLUTION));
    fixture.componentRef.setInput('answerPayloadJson', inputs.answer ?? null);
    fixture.componentRef.setInput('feedback', inputs.feedback ?? null);
    fixture.componentRef.setInput('showSolution', !!inputs.showSolution);
    fixture.componentRef.setInput('activityMode', true);
    fixture.detectChanges();
  }

  function click(target: HTMLElement) {
    target.click();
    fixture.detectChanges();
  }

  it('rendersTicksLabelsAndPool', () => {
    setup();
    expect(el().querySelectorAll('[data-tick]').length).toBe(5);
    expect(el().querySelectorAll('.nl-label').length).toBe(2);
    expect(q('nl-pool')!.querySelectorAll('[data-card]').length).toBe(3);
    // Kart seçilmeden çentikler pasif.
    expect(tick(1).disabled).toBe(true);
  });

  it('placesSelectedCardOnTick', () => {
    setup();
    click(poolCard('k1')!);
    expect(poolCard('k1')!.classList).toContain('is-selected');
    click(tick(1));
    expect(poolCard('k1')).toBeNull();
    expect(stack(1)!.textContent).toContain('1/4');
    expect(last()).toEqual({ k1: 1 });
    expect(JSON.parse(emitted.at(-1)!).type).toBe('numberLine');
  });

  it('snapsPointerOnLineToNearestTick', () => {
    setup();
    click(poolCard('k2')!);
    const line = q('nl-line')!;
    const rect = line.getBoundingClientRect();
    line.dispatchEvent(new MouseEvent('click', { bubbles: true, clientX: rect.left + rect.width * 0.55, clientY: rect.top + 5 }));
    fixture.detectChanges();
    expect(last()).toEqual({ k2: 2 });
  });

  it('stacksCardsOnSameTick', () => {
    setup({ answer: answer({ k1: 2, k2: 2 }) });
    expect(stack(2)!.querySelectorAll('[data-card]').length).toBe(2);
    expect(q('nl-pool')!.querySelectorAll('[data-card]').length).toBe(1);
  });

  it('arrowKeysMovePlacedCardAndDeleteReturnsIt', () => {
    setup({ answer: answer({ k1: 2 }) });
    placedCard('k1')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    fixture.detectChanges();
    expect(last()).toEqual({ k1: 1 });
    // Uçta durur.
    placedCard('k1')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    placedCard('k1')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    fixture.detectChanges();
    expect(last()).toEqual({ k1: 0 });
    const count = emitted.length;
    placedCard('k1')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    expect(emitted.length).toBe(count);
    placedCard('k1')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Delete', bubbles: true }));
    fixture.detectChanges();
    expect(last()).toEqual({});
    expect(poolCard('k1')).not.toBeNull();
  });

  it('secondTapOnPlacedCardReturnsItToPool', () => {
    setup({ answer: answer({ k1: 1 }) });
    click(placedCard('k1')!);
    expect((q('nl-return') as HTMLButtonElement).disabled).toBe(false);
    click(placedCard('k1')!);
    expect(poolCard('k1')).not.toBeNull();
    expect(last()).toEqual({});
  });

  it('returnButtonAndReset', () => {
    setup({ answer: answer({ k1: 1, k2: 2 }) });
    click(placedCard('k2')!);
    click(q('nl-return')!);
    expect(last()).toEqual({ k1: 1 });
    click(q('nl-reset')!);
    expect(last()).toEqual({});
    expect((q('nl-reset') as HTMLButtonElement).disabled).toBe(true);
  });

  it('doesNotRestoreOwnEcho', () => {
    setup();
    click(poolCard('k1')!);
    click(tick(3));
    click(poolCard('k2')!);
    // Yankı (eski payload) seçimi ve yerleşimi bozmasın.
    fixture.componentRef.setInput('answerPayloadJson', emitted.at(-1)!);
    fixture.detectChanges();
    expect(poolCard('k2')!.classList).toContain('is-selected');
    expect(stack(3)).not.toBeNull();
  });

  it('hiddenTicksDrawOnlyLabelled', () => {
    setup({ plan: { ...PLAN, divisions: 100, hideTicks: true, labels: [{ at: 0, text: '0' }, { at: 100, text: '1' }] } });
    expect(el().querySelectorAll('[data-tick]').length).toBe(2);
  });

  it('feedbackMarksCards', () => {
    setup({
      answer: answer({ k1: 1, k2: 3 }),
      feedback: { rows: [{ id: 'k1', correct: true }, { id: 'k2', correct: false }, { id: 'k3', correct: true }] },
    });
    expect(placedCard('k1')!.classList).toContain('is-ok');
    expect(placedCard('k2')!.classList).toContain('is-bad');
    expect(q('nl-result')!.classList).toContain('is-bad');
  });

  it('solutionShowsTargetsAndKeepsDistractorInPool', () => {
    setup({ answer: answer({ k3: 4 }), showSolution: true });
    expect(stack(1)!.textContent).toContain('1/4');
    expect(stack(2)!.textContent).toContain('2/4');
    expect(poolCard('k3')).not.toBeNull();
    expect(poolCard('k3')!.disabled).toBe(true);
    expect(q('nl-reset')).toBeNull();
    expect(q('nl-solution-hint')).not.toBeNull();
  });

  it('invalidPlan', () => {
    setup({ plan: { type: 'numberLine', divisions: 0, cards: [] } });
    expect(q('nl-invalid')).not.toBeNull();
  });
});
