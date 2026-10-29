// Issue #632: terazi oynatıcısı — ağırlığı seçip kefeye koyma, kefeler arası taşıma, havuza geri, canlı / gizli eğim, hazır yükler,
// yankı, geri bildirim, çözüm.
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BalanceScaleFeedback } from '../../../models/balance-scale';
import { QuestionRegion } from '../../../models/draws';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { QuestionCanvasBalanceScaleComponent } from './question-canvas-balance-scale.component';

// Süper Combo Mat s.203 2a: 5, 9, 10, 14 kg.
const PLAN = {
  version: 1,
  type: 'balanceScale',
  unit: 'kg',
  cards: [
    { id: 'k1', value: 5 },
    { id: 'k2', value: 9 },
    { id: 'k3', value: 10 },
    { id: 'k4', value: 14 },
  ],
};

const SOLUTION = { useAll: true };

function region(): QuestionRegion {
  return { imageUrl: '', width: 0, height: 0, passage: null } as unknown as QuestionRegion;
}

const answer = (left: string[], right: string[]) => JSON.stringify({ version: 1, type: 'balanceScale', left, right });

describe('QuestionCanvasBalanceScaleComponent (#632)', () => {
  let fixture: ComponentFixture<QuestionCanvasBalanceScaleComponent>;
  let emitted: string[];

  const el = () => fixture.nativeElement as HTMLElement;
  const q = (testId: string) => el().querySelector(`[data-testid="${testId}"]`) as HTMLElement | null;
  const poolCard = (id: string) => q('bs-pool')!.querySelector(`[data-card="${id}"]`) as HTMLButtonElement | null;
  const panCard = (side: 'left' | 'right', id: string) =>
    q(`bs-pan-${side}`)!.querySelector(`[data-card="${id}"]`) as HTMLButtonElement | null;
  const tilt = () => q('bs-scale')!.getAttribute('data-tilt');
  const last = () => {
    const p = JSON.parse(emitted.at(-1)!);
    return { left: p.left as string[], right: p.right as string[] };
  };

  function setup(
    inputs: { plan?: object; answer?: string | null; feedback?: BalanceScaleFeedback | null; showSolution?: boolean } = {}
  ): void {
    TestBed.configureTestingModule({ imports: [QuestionCanvasBalanceScaleComponent, translocoTestingModule()] });
    fixture = TestBed.createComponent(QuestionCanvasBalanceScaleComponent);
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

  it('rendersPoolAndLevelScale', () => {
    setup();
    expect(q('bs-pool')!.querySelectorAll('[data-card]').length).toBe(4);
    expect(poolCard('k1')!.textContent).toContain('5 kg');
    expect(tilt()).toBe('level');
    expect((q('bs-to-left') as HTMLButtonElement).disabled).toBe(true);
  });

  it('placesSelectedWeightOnTappedPanAndTilts', () => {
    setup();
    click(poolCard('k4')!);
    click(q('bs-pan-left')!);
    expect(poolCard('k4')).toBeNull();
    expect(panCard('left', 'k4')).not.toBeNull();
    expect(last()).toEqual({ left: ['k4'], right: [] });
    expect(tilt()).toBe('left');
    click(poolCard('k3')!);
    click(q('bs-to-right')!);
    expect(last()).toEqual({ left: ['k4'], right: ['k3'] });
    expect(tilt()).toBe('left');
  });

  it('balancedPlacementLevelsScale', () => {
    setup({ answer: answer(['k1', 'k4'], ['k2', 'k3']) });
    expect(tilt()).toBe('level');
    expect(q('bs-pool')!.querySelectorAll('[data-card]').length).toBe(0);
  });

  it('movesWeightBetweenPansAndReturnsIt', () => {
    setup({ answer: answer(['k1'], ['k2']) });
    click(panCard('left', 'k1')!);
    click(q('bs-pan-right')!);
    expect(last()).toEqual({ left: [], right: ['k2', 'k1'] });
    click(panCard('right', 'k2')!);
    click(q('bs-return')!);
    expect(last()).toEqual({ left: [], right: ['k1'] });
    // İkinci dokunuş havuza geri koyar.
    click(panCard('right', 'k1')!);
    click(panCard('right', 'k1')!);
    expect(last()).toEqual({ left: [], right: [] });
    expect((q('bs-reset') as HTMLButtonElement).disabled).toBe(true);
  });

  it('resetClearsPans', () => {
    setup({ answer: answer(['k1'], ['k2']) });
    click(q('bs-reset')!);
    expect(last()).toEqual({ left: [], right: [] });
  });

  it('fixedLoadsStayOnPanAndCount', () => {
    setup({ plan: { ...PLAN, left: [{ id: 'f1', value: 20, label: 'Kutu' }] } });
    expect(q('bs-pan-left')!.querySelector('[data-fixed="f1"]')!.textContent).toContain('Kutu');
    expect(tilt()).toBe('left');
  });

  it('hiddenTiltStaysLevelUntilFeedback', () => {
    setup({ plan: { ...PLAN, hideTilt: true }, answer: answer(['k4'], []) });
    expect(tilt()).toBe('level');
    fixture.componentRef.setInput('feedback', { correct: false, tilt: 'left', unused: 3 });
    fixture.detectChanges();
    expect(tilt()).toBe('left');
  });

  it('doesNotRestoreOwnEcho', () => {
    setup();
    click(poolCard('k1')!);
    click(q('bs-pan-left')!);
    click(poolCard('k2')!);
    fixture.componentRef.setInput('answerPayloadJson', emitted.at(-1)!);
    fixture.detectChanges();
    expect(poolCard('k2')!.classList).toContain('is-selected');
    expect(panCard('left', 'k1')).not.toBeNull();
  });

  it('feedbackMessages', () => {
    setup({ answer: answer(['k1', 'k2'], ['k4']), feedback: { correct: false, tilt: 'level', unused: 1 } });
    expect(q('bs-result')!.classList).toContain('is-bad');
    expect(q('bs-result')!.textContent).toContain('1 ağırlık kullanılmadı');
    fixture.componentRef.setInput('feedback', { correct: true, tilt: 'level', unused: 0 });
    fixture.detectChanges();
    expect(q('bs-result')!.classList).toContain('is-ok');
  });

  it('solutionShowsBalancingArrangementReadOnly', () => {
    setup({ answer: answer(['k1'], []), showSolution: true });
    const left = [...q('bs-pan-left')!.querySelectorAll('[data-card]')].map((b) => b.getAttribute('data-card'));
    const right = [...q('bs-pan-right')!.querySelectorAll('[data-card]')].map((b) => b.getAttribute('data-card'));
    expect(left.length + right.length).toBe(4);
    expect(tilt()).toBe('level');
    expect(q('bs-reset')).toBeNull();
    expect(q('bs-solution-hint')).not.toBeNull();
  });

  it('invalidPlan', () => {
    setup({ plan: { type: 'balanceScale', cards: [] } });
    expect(q('bs-invalid')).not.toBeNull();
  });
});
