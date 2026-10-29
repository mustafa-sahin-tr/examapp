// Issue #588 (dilim b): grafik kurma oynatıcısı — dokunarak yükseklik, aynı seviyeye dokunup silme, klavye, sabit sütun, çizgi görünümü,
// yankı, geri bildirim, çözüm.
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BarChartFeedback } from '../../../models/bar-chart';
import { QuestionRegion } from '../../../models/draws';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { QuestionCanvasBarChartComponent } from './question-canvas-bar-chart.component';

const PLAN = {
  version: 1,
  type: 'barChart',
  title: 'Sevilen meyveler',
  yLabel: 'Öğrenci',
  max: 10,
  step: 2,
  unit: 1,
  categories: [
    { id: 'c1', label: 'Elma' },
    { id: 'c2', label: 'Armut' },
    { id: 'c3', label: 'Muz' },
  ],
  fixed: { c1: 6 },
};

const SOLUTION = { values: { c2: 4, c3: 7 } };

function region(): QuestionRegion {
  return { imageUrl: '', width: 0, height: 0, passage: null } as unknown as QuestionRegion;
}

const answer = (values: Record<string, number>) => JSON.stringify({ version: 1, type: 'barChart', values });

describe('QuestionCanvasBarChartComponent (#588)', () => {
  let fixture: ComponentFixture<QuestionCanvasBarChartComponent>;
  let emitted: string[];

  const el = () => fixture.nativeElement as HTMLElement;
  const q = (testId: string) => el().querySelector(`[data-testid="${testId}"]`) as HTMLElement | null;
  const col = (id: string) => el().querySelector(`[data-col="${id}"]`) as HTMLElement;
  const bar = (id: string) => col(id).querySelector('.bc-bar') as HTMLElement | null;
  const last = () => JSON.parse(emitted.at(-1)!).values as Record<string, number>;

  function setup(
    inputs: { plan?: object; answer?: string | null; feedback?: BarChartFeedback | null; showSolution?: boolean } = {}
  ): void {
    TestBed.configureTestingModule({ imports: [QuestionCanvasBarChartComponent, translocoTestingModule()] });
    fixture = TestBed.createComponent(QuestionCanvasBarChartComponent);
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

  /** Sütunun içinde, alttan `ratio` yüksekliğe tıklar. */
  function clickAt(id: string, ratio: number): void {
    const rect = col(id).getBoundingClientRect();
    col(id).dispatchEvent(
      new MouseEvent('click', { bubbles: true, clientX: rect.left + rect.width / 2, clientY: rect.bottom - rect.height * ratio })
    );
    fixture.detectChanges();
  }

  function key(id: string, k: string): void {
    col(id).dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true }));
    fixture.detectChanges();
  }

  it('rendersAxisColumnsAndFixedBar', () => {
    setup();
    expect(q('bc-title')!.textContent).toContain('Sevilen meyveler');
    expect(el().querySelectorAll('.bc-ytick').length).toBe(6); // 0, 2, …, 10
    expect(el().querySelectorAll('.bc-grid').length).toBe(10);
    expect(el().querySelectorAll('.bc-grid.is-major').length).toBe(5);
    expect(col('c1').classList).toContain('is-fixed');
    expect(bar('c1')!.style.height).toBe('60%');
    expect(bar('c2')).toBeNull();
    expect(col('c1').getAttribute('tabindex')).toBe('-1');
    expect(col('c2').getAttribute('tabindex')).toBe('0');
  });

  it('clickSetsNearestUnitAndSameLevelClears', () => {
    setup();
    clickAt('c2', 0.42);
    expect(last()).toEqual({ c2: 4 });
    expect(bar('c2')!.style.height).toBe('40%');
    clickAt('c2', 0.4);
    expect(last()).toEqual({});
    expect(bar('c2')).toBeNull();
  });

  it('fixedColumnIgnoresClicks', () => {
    setup();
    clickAt('c1', 0.9);
    expect(emitted.length).toBe(0);
  });

  it('keyboard', () => {
    setup();
    key('c3', 'ArrowUp');
    expect(last()).toEqual({ c3: 1 });
    key('c3', 'PageUp');
    expect(last()).toEqual({ c3: 3 });
    key('c3', 'End');
    expect(last()).toEqual({ c3: 10 });
    key('c3', 'ArrowUp');
    expect(emitted.length).toBe(3); // en üstte değişmez
    key('c3', 'Home');
    expect(last()).toEqual({ c3: 0 });
    key('c3', 'Delete');
    expect(last()).toEqual({});
    expect(col('c3').getAttribute('aria-valuenow')).toBe('0');
  });

  it('restoresAnswerWithoutEchoAndResets', () => {
    setup({ answer: answer({ c2: 4, c3: 7 }) });
    expect(bar('c3')!.style.height).toBe('70%');
    expect(emitted.length).toBe(0);
    (q('bc-reset') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(last()).toEqual({});
  });

  it('lineStyleDrawsDotsAndPolyline', () => {
    setup({ plan: { ...PLAN, style: 'line' }, answer: answer({ c3: 8 }) });
    expect(el().querySelectorAll('.bc-dot').length).toBe(2);
    expect(el().querySelector('polyline')!.getAttribute('points')).toBe('0.5,4 2.5,2');
  });

  it('feedbackColorsColumns', () => {
    setup({ answer: answer({ c2: 4, c3: 6 }), feedback: { rows: [{ id: 'c2', correct: true }, { id: 'c3', correct: false }] } });
    expect(col('c2').classList).toContain('is-ok');
    expect(col('c3').classList).toContain('is-bad');
    expect(q('bc-result')!.classList).toContain('is-bad');
  });

  it('solutionShowsTargetsReadOnly', () => {
    setup({ answer: answer({ c2: 1 }), showSolution: true });
    expect(bar('c2')!.style.height).toBe('40%');
    expect(bar('c3')!.style.height).toBe('70%');
    expect(q('bc-reset')).toBeNull();
    clickAt('c2', 0.9);
    expect(emitted.length).toBe(0);
  });

  it('invalidPlan', () => {
    setup({ plan: { ...PLAN, step: 3 } });
    expect(q('bc-invalid')).not.toBeNull();
  });

  it('dotStyle_StacksDots_TopDotTapRemovesOne', () => {
    setup({ plan: { ...PLAN, style: 'dot' } });
    // Hazır sütun 6 nokta.
    expect(col('c1').querySelectorAll('[data-dot]').length).toBe(6);
    expect(bar('c1')).toBeNull();
    // 0,35 × 10 = 3,5 → 4. birim aralığı: 4 nokta.
    clickAt('c2', 0.35);
    expect(last()).toEqual({ c2: 4 });
    expect(col('c2').querySelectorAll('[data-dot]').length).toBe(4);
    // En üstteki noktaya (3,5) dokunmak bir nokta kaldırır.
    clickAt('c2', 0.35);
    expect(last()).toEqual({ c2: 3 });
  });
});

