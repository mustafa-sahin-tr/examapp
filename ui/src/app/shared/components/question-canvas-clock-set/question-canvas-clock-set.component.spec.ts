// Issue #634: saat kurma oynatıcısı — klavyeyle el çevirme (akrep ve yelkovan ayrı), kadrana dokunma, sabit saat, yankı, geri bildirim,
// çözüm.
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { QuestionRegion } from '../../../models/draws';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { QuestionCanvasClockSetComponent } from './question-canvas-clock-set.component';

const PLAN = {
  version: 1,
  type: 'clockSet',
  step: 5,
  clocks: [{ id: 'c0', label: 'Başlangıç', fixed: '09:20' }, { id: 'c1', label: 'Bitiş' }],
};
const SOLUTION = { times: { c1: '10:00' } };

function region(): QuestionRegion {
  return { imageUrl: '', width: 0, height: 0, passage: null } as unknown as QuestionRegion;
}

describe('QuestionCanvasClockSetComponent (#634)', () => {
  let fixture: ComponentFixture<QuestionCanvasClockSetComponent>;
  let emitted: string[];

  const el = () => fixture.nativeElement as HTMLElement;
  const q = (testId: string) => el().querySelector(`[data-testid="${testId}"]`) as HTMLElement | null;
  const rotation = (testId: string) => q(testId)!.getAttribute('transform');
  const last = () => JSON.parse(emitted.at(-1)!).clocks as Record<string, { h: number; m: number }>;
  const key = (testId: string, k: string, times = 1) => {
    for (let i = 0; i < times; i++) q(testId)!.dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true }));
    fixture.detectChanges();
  };

  function setup(
    inputs: { answer?: string | null; feedback?: Record<string, boolean> | null; showSolution?: boolean; disabled?: boolean } = {}
  ): void {
    TestBed.configureTestingModule({ imports: [QuestionCanvasClockSetComponent, translocoTestingModule()] });
    fixture = TestBed.createComponent(QuestionCanvasClockSetComponent);
    emitted = [];
    fixture.componentInstance.answerPayloadChange.subscribe((p) => emitted.push(p));
    fixture.componentRef.setInput('questionRegion', region());
    fixture.componentRef.setInput('interactionPlanJson', JSON.stringify(PLAN));
    fixture.componentRef.setInput('interactionSolutionJson', JSON.stringify(SOLUTION));
    fixture.componentRef.setInput('answerPayloadJson', inputs.answer ?? null);
    fixture.componentRef.setInput('feedback', inputs.feedback ?? null);
    fixture.componentRef.setInput('showSolution', !!inputs.showSolution);
    fixture.componentRef.setInput('disabled', !!inputs.disabled);
    fixture.detectChanges();
  }

  it('shows the fixed clock set and not focusable; free clock starts at 12', () => {
    setup();
    expect(rotation('cs-hour-c0')).toBe('rotate(280 100 100)'); // 9.20 → 560/720 × 360
    expect(rotation('cs-minute-c0')).toBe('rotate(120 100 100)');
    expect(q('cs-hour-c0')!.getAttribute('tabindex')).toBeNull();
    expect(rotation('cs-hour-c1')).toBe('rotate(0 100 100)');
    expect(q('cs-minute-c1')!.getAttribute('role')).toBe('slider');
  });

  it('turns the hour and minute hands separately with the keyboard', () => {
    setup();
    key('cs-minute-c1', 'ArrowRight', 3); // 15 dk
    expect(last()).toEqual({ c1: { h: 0, m: 15 } });
    key('cs-hour-c1', 'PageUp', 2); // iki saat
    expect(last()).toEqual({ c1: { h: 120, m: 15 } });
    key('cs-hour-c1', 'ArrowRight', 3); // 2.15'te akrep 2'nin 3 adım (7,5°) ötesinde
    expect(last()).toEqual({ c1: { h: 135, m: 15 } });
    key('cs-minute-c1', 'ArrowLeft', 4); // 15 → 55
    expect(last()['c1'].m).toBe(55);
    expect(rotation('cs-hour-c1')).toBe('rotate(67.5 100 100)');
  });

  it('pointer on the dial turns the active hand', () => {
    setup();
    const svg = el().querySelector('[data-clock="c1"] svg') as SVGSVGElement;
    const rect = svg.getBoundingClientRect();
    const at = (dx: number, dy: number) =>
      ({ clientX: rect.left + rect.width / 2 + dx, clientY: rect.top + rect.height / 2 + dy, pointerId: 1, target: svg, currentTarget: svg,
        preventDefault: () => undefined }) as unknown as PointerEvent;
    const cmp = fixture.componentInstance;
    cmp.onPointerDown(at(0, 60), 'c1'); // aşağı = 30 dk (varsayılan el yelkovan)
    expect(last()).toEqual({ c1: { h: 0, m: 30 } });
    cmp.setActiveHand('hour');
    cmp.onPointerMove(at(60, 0), 'c1'); // sürükleme sürüyor: sağ = 3
    expect(last()).toEqual({ c1: { h: 180, m: 30 } });
    cmp.onPointerUp(at(0, 0));
    cmp.onPointerDown(at(0, 60), 'c0'); // sabit saat oynatılamaz
    expect(last()).toEqual({ c1: { h: 180, m: 30 } });
  });

  it('restores the answer, ignores its own echo, shows feedback and resets', () => {
    setup({ answer: JSON.stringify({ version: 1, type: 'clockSet', clocks: { c1: { h: 600, m: 0 } } }), feedback: { c1: false } });
    expect(rotation('cs-hour-c1')).toBe('rotate(300 100 100)');
    expect(q('cs-result-c1')!.textContent).toContain('cancel');
    expect(q('cs-result-c0')).toBeNull();
    key('cs-minute-c1', 'ArrowRight');
    fixture.componentRef.setInput('answerPayloadJson', emitted.at(-1)!);
    fixture.detectChanges();
    expect(last()).toEqual({ c1: { h: 600, m: 5 } });
    (q('cs-reset') as HTMLButtonElement).click();
    expect(last()).toEqual({});
  });

  it('solution compares the student clock: wrong clock red with dashed target hands', () => {
    setup({ answer: JSON.stringify({ version: 1, type: 'clockSet', clocks: { c1: { h: 135, m: 15 } } }), showSolution: true });
    // Öğrencinin elleri kalır (2.15), doğrusu 10.00 kesikli.
    expect(rotation('cs-hour-c1')).toBe('rotate(67.5 100 100)');
    expect(el().querySelector('[data-clock="c1"]')!.classList).toContain('is-bad');
    expect(q('cs-ghost-c1')).not.toBeNull();
    expect(q('cs-target-c1')!.textContent).toContain('10:00');
    expect(q('cs-ghost-c0')).toBeNull();
    expect(q('cs-solution-hint')!.textContent).toContain('kesikli');
  });

  it('solution compare: correct clock green without ghost', () => {
    setup({ answer: JSON.stringify({ version: 1, type: 'clockSet', clocks: { c1: { h: 600, m: 0 } } }), showSolution: true });
    expect(el().querySelector('[data-clock="c1"]')!.classList).toContain('is-ok');
    expect(q('cs-ghost-c1')).toBeNull();
  });

  it('solution shows target hands and is read-only', () => {
    setup({ showSolution: true });
    expect(rotation('cs-hour-c1')).toBe('rotate(300 100 100)');
    expect(q('cs-hand-toggle')).toBeNull();
    key('cs-minute-c1', 'ArrowRight');
    expect(emitted).toEqual([]);
    expect(q('cs-solution-hint')).not.toBeNull();
  });
});
