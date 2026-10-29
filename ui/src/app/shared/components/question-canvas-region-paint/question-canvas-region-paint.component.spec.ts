// Issue #587: boyama oynatıcısı — renk seçme, dokunma / sürükleme ile boyama, aynı renkten başlayınca silme, silgi, canlı sayaç,
// yankı, geri bildirim, çözüm.
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { QuestionRegion } from '../../../models/draws';
import { RegionPaintFeedback } from '../../../models/region-paint';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { QuestionCanvasRegionPaintComponent } from './question-canvas-region-paint.component';

const PLAN = {
  version: 1,
  type: 'regionPaint',
  mode: 'grid',
  rows: 2,
  cols: 3,
  palette: [
    { id: 'p1', color: 'blue', label: '1/2' },
    { id: 'p2', color: 'red' },
  ],
};

const SOLUTION = { counts: { p1: 3, p2: 1 } };

function region(): QuestionRegion {
  return { imageUrl: '', width: 0, height: 0, passage: null } as unknown as QuestionRegion;
}

const grid = (...rows: string[]) => JSON.stringify({ version: 1, type: 'regionPaint', grid: rows });

describe('QuestionCanvasRegionPaintComponent (#587)', () => {
  let fixture: ComponentFixture<QuestionCanvasRegionPaintComponent>;
  let emitted: string[];

  const el = () => fixture.nativeElement as HTMLElement;
  const cell = (r: number, c: number) => el().querySelector(`[data-r="${r}"][data-c="${c}"]`) as HTMLButtonElement;
  const swatch = (i: number) => el().querySelector(`[data-paint="${i}"]`) as HTMLButtonElement;
  const count = (i: number) => el().querySelector(`[data-count="${i}"]`)?.textContent?.trim();
  const q = (testId: string) => el().querySelector(`[data-testid="${testId}"]`) as HTMLElement | null;

  function setup(inputs: { answer?: string | null; feedback?: RegionPaintFeedback | null; showSolution?: boolean } = {}): void {
    TestBed.configureTestingModule({ imports: [QuestionCanvasRegionPaintComponent, translocoTestingModule()] });
    fixture = TestBed.createComponent(QuestionCanvasRegionPaintComponent);
    emitted = [];
    fixture.componentInstance.answerPayloadChange.subscribe((p) => emitted.push(p));
    fixture.componentRef.setInput('questionRegion', region());
    fixture.componentRef.setInput('interactionPlanJson', JSON.stringify(PLAN));
    fixture.componentRef.setInput('interactionSolutionJson', JSON.stringify(SOLUTION));
    fixture.componentRef.setInput('answerPayloadJson', inputs.answer ?? null);
    fixture.componentRef.setInput('feedback', inputs.feedback ?? null);
    fixture.componentRef.setInput('showSolution', !!inputs.showSolution);
    fixture.componentRef.setInput('activityMode', true);
    fixture.detectChanges();
  }

  function pointer(type: string, target: Element) {
    const rect = target.getBoundingClientRect();
    target.dispatchEvent(
      new PointerEvent(type, { bubbles: true, cancelable: true, clientX: rect.left + rect.width / 2, clientY: rect.top + rect.height / 2 })
    );
  }

  function tap(r: number, c: number) {
    pointer('pointerdown', cell(r, c));
    pointer('pointerup', cell(r, c));
    fixture.detectChanges();
  }

  it('rendersGridAndPalette', () => {
    setup();
    expect(el().querySelectorAll('[data-cell]').length).toBe(6);
    expect(swatch(0).textContent).toContain('1/2');
    expect(swatch(1).getAttribute('aria-checked')).toBe('false');
  });

  it('tap_PaintsWithSelectedColor_CountsLive_EmitsOnce', () => {
    setup();
    tap(0, 0);
    expect(cell(0, 0).style.background).toContain('--paint-blue');
    expect(count(0)).toBe('1');
    expect(emitted).toEqual([grid('1..', '...')]);

    swatch(1).click();
    fixture.detectChanges();
    tap(1, 2);
    expect(emitted.at(-1)).toBe(grid('1..', '..2'));
    expect(count(1)).toBe('1');
  });

  it('drag_PaintsEveryCellUnderPointer', () => {
    setup();
    const host = q('rp-grid')!;
    pointer('pointerdown', cell(0, 0));
    pointer('pointermove', cell(0, 1));
    pointer('pointermove', cell(0, 2));
    pointer('pointerup', host);
    fixture.detectChanges();
    expect(emitted).toEqual([grid('111', '...')]);
  });

  it('tapOnSameColor_Erases_AndEraserErases', () => {
    setup({ answer: grid('11.', '2..') });
    tap(0, 0);
    expect(emitted.at(-1)).toBe(grid('.1.', '2..'));
    q('rp-eraser')!.click();
    fixture.detectChanges();
    tap(1, 0);
    expect(emitted.at(-1)).toBe(grid('.1.', '...'));
  });

  it('echoOfOwnPayload_DoesNotResetCells', () => {
    setup();
    tap(1, 1);
    fixture.componentRef.setInput('answerPayloadJson', emitted.at(-1)!);
    fixture.detectChanges();
    expect(count(0)).toBe('1');
  });

  it('feedback_MarksPaletteEntries', () => {
    setup({ answer: grid('111', '...'), feedback: { rows: [{ id: 'p1', correct: true }, { id: 'p2', correct: false }] } });
    expect(swatch(0).classList).toContain('is-ok');
    expect(swatch(1).classList).toContain('is-bad');
    expect(q('rp-result')?.classList).toContain('is-bad');
  });

  it('showSolution_SampleFill_ReadOnly', () => {
    setup({ answer: grid('2..', '...'), showSolution: true });
    expect(count(0)).toBe('3');
    expect(count(1)).toBe('1');
    expect(cell(1, 0).style.background).toContain('--paint-red');
    expect(cell(0, 0).disabled).toBe(true);
    expect(q('rp-reset')).toBeNull();
    expect(q('rp-eraser')).toBeNull();
  });
});

// Issue #587 dilim b: bölge modu — bölgeye dokunma, aynı renkle kaldırma, silgi, tek renkte palet gizli, geri bildirim, çözüm.
describe('QuestionCanvasRegionPaintComponent regions (#587 dilim b)', () => {
  const REGIONS_PLAN = {
    version: 1,
    type: 'regionPaint',
    mode: 'regions',
    style: 'fill',
    palette: [
      { id: 'p1', color: 'blue' },
      { id: 'p2', color: 'red', label: 'tek' },
    ],
    regions: [
      { id: 'r1', points: [[0, 0], [50, 0], [50, 50], [0, 50]] },
      { id: 'r2', points: [[50, 0], [100, 0], [100, 50], [50, 50]], label: '7' },
    ],
  };

  let fixture: ComponentFixture<QuestionCanvasRegionPaintComponent>;
  let emitted: string[];
  const el = () => fixture.nativeElement as HTMLElement;
  const shape = (id: string) => el().querySelector(`[data-region="${id}"] polygon`) as SVGPolygonElement;
  const group = (id: string) => el().querySelector(`[data-region="${id}"]`) as SVGGElement;
  const q = (testId: string) => el().querySelector(`[data-testid="${testId}"]`) as HTMLElement | null;

  function setup(
    inputs: { plan?: object; answer?: string | null; feedback?: RegionPaintFeedback | null; showSolution?: boolean } = {}
  ): void {
    TestBed.configureTestingModule({ imports: [QuestionCanvasRegionPaintComponent, translocoTestingModule()] });
    fixture = TestBed.createComponent(QuestionCanvasRegionPaintComponent);
    emitted = [];
    fixture.componentInstance.answerPayloadChange.subscribe((p) => emitted.push(p));
    fixture.componentRef.setInput('questionRegion', {
      imageUrl: 'data:image/gif;base64,R0lGODlhAQABAAAAACw=',
      width: 100,
      height: 50,
      passage: null,
    } as unknown as QuestionRegion);
    fixture.componentRef.setInput('interactionPlanJson', JSON.stringify(inputs.plan ?? REGIONS_PLAN));
    fixture.componentRef.setInput('interactionSolutionJson', JSON.stringify({ fills: { r1: 'p1' } }));
    fixture.componentRef.setInput('answerPayloadJson', inputs.answer ?? null);
    fixture.componentRef.setInput('feedback', inputs.feedback ?? null);
    fixture.componentRef.setInput('showSolution', !!inputs.showSolution);
    fixture.detectChanges();
  }

  const fills = (f: Record<string, string>) => JSON.stringify({ version: 1, type: 'regionPaint', fills: f });

  function click(id: string) {
    shape(id).dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();
  }

  it('rendersRegionsOverImage_NoGrid', () => {
    setup();
    expect(el().querySelectorAll('[data-region]').length).toBe(2);
    expect(q('rp-grid')).toBeNull();
    expect(q('rp-regions')?.getAttribute('viewBox')).toBe('0 0 100 50');
  });

  it('tap_PaintsWithSelectedColor_SameColorClears', () => {
    setup();
    click('r1');
    expect(emitted.at(-1)).toBe(fills({ r1: 'p1' }));
    expect(group('r1').classList.contains('is-painted')).toBe(true);
    (el().querySelector('[data-paint="1"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    click('r1');
    expect(emitted.at(-1)).toBe(fills({ r1: 'p2' }));
    click('r1');
    expect(emitted.at(-1)).toBe(fills({}));
  });

  it('eraser_Clears_KeyboardWorks', () => {
    setup({ answer: fills({ r2: 'p2' }) });
    expect(el().querySelector('[data-count="1"]')?.textContent?.trim()).toBe('1');
    q('rp-eraser')!.click();
    fixture.detectChanges();
    shape('r2').dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    fixture.detectChanges();
    expect(emitted.at(-1)).toBe(fills({}));
  });

  it('singleColor_HidesPalette_TapToggles', () => {
    setup({ plan: { ...REGIONS_PLAN, style: 'circle', palette: [{ id: 'p1', color: 'red' }] } });
    expect(q('rp-palette')).toBeNull();
    click('r2');
    expect(emitted.at(-1)).toBe(fills({ r2: 'p1' }));
    expect(group('r2').querySelector('ellipse')).not.toBeNull();
  });

  it('feedback_MarksPaintedRegions_Counter', () => {
    setup({
      answer: fills({ r1: 'p2', r2: 'p2' }),
      feedback: { rows: [{ id: 'r1', correct: false, reason: 'color' }, { id: 'r2', correct: false, reason: 'extra' }], found: 0, missing: 1, extra: 1 },
    });
    expect(group('r1').getAttribute('data-mark')).toBe('color');
    expect(group('r2').getAttribute('data-mark')).toBe('extra');
    expect(q('rp-result')?.classList.contains('is-bad')).toBe(true);
  });

  it('solution_ShowsTargets_ReadOnly', () => {
    setup({ answer: fills({ r2: 'p2' }), showSolution: true });
    expect(group('r1').classList.contains('is-painted')).toBe(true);
    expect(group('r2').classList.contains('is-painted')).toBe(false);
    click('r2');
    expect(emitted).toEqual([]);
    expect(q('rp-solution-hint')).not.toBeNull();
  });
});

// Issue #587 dilim c: numaralı boyama — bölgeye dokunmak o numaralı bütün bölgeleri boyar, numara listesi, grup başına işaret, çözüm.
describe('QuestionCanvasRegionPaintComponent numbered (#587 dilim c)', () => {
  const GROUPS_PLAN = {
    version: 1,
    type: 'regionPaint',
    mode: 'regions',
    palette: [
      { id: 'p1', color: 'blue' },
      { id: 'p2', color: 'red' },
    ],
    groups: [{ id: 'g1', label: '1' }, { id: 'g2', label: '2' }],
    regions: [
      { id: 'r1', points: [[0, 0], [30, 0], [30, 50], [0, 50]], group: 'g1' },
      { id: 'r2', points: [[30, 0], [60, 0], [60, 50], [30, 50]], group: 'g2' },
      { id: 'r3', points: [[60, 0], [100, 0], [100, 50], [60, 50]], group: 'g1' },
    ],
  };

  let fixture: ComponentFixture<QuestionCanvasRegionPaintComponent>;
  let emitted: string[];
  const el = () => fixture.nativeElement as HTMLElement;
  const regionEl = (id: string) => el().querySelector(`[data-region="${id}"]`) as SVGGElement;
  const groupBtn = (id: string) => el().querySelector(`[data-group="${id}"]`) as HTMLButtonElement;
  const q = (testId: string) => el().querySelector(`[data-testid="${testId}"]`) as HTMLElement | null;
  const fills = (f: Record<string, string>) => JSON.stringify({ version: 1, type: 'regionPaint', fills: f });

  function setup(inputs: { answer?: string | null; feedback?: RegionPaintFeedback | null; showSolution?: boolean } = {}): void {
    TestBed.configureTestingModule({ imports: [QuestionCanvasRegionPaintComponent, translocoTestingModule()] });
    fixture = TestBed.createComponent(QuestionCanvasRegionPaintComponent);
    emitted = [];
    fixture.componentInstance.answerPayloadChange.subscribe((p) => emitted.push(p));
    fixture.componentRef.setInput('questionRegion', {
      imageUrl: 'data:image/gif;base64,R0lGODlhAQABAAAAACw=',
      width: 100,
      height: 50,
      passage: null,
    } as unknown as QuestionRegion);
    fixture.componentRef.setInput('interactionPlanJson', JSON.stringify(GROUPS_PLAN));
    fixture.componentRef.setInput('interactionSolutionJson', JSON.stringify({ fills: { g1: 'p1', g2: 'p2' } }));
    fixture.componentRef.setInput('answerPayloadJson', inputs.answer ?? null);
    fixture.componentRef.setInput('feedback', inputs.feedback ?? null);
    fixture.componentRef.setInput('showSolution', !!inputs.showSolution);
    fixture.detectChanges();
  }

  function click(id: string) {
    el().querySelector(`[data-region="${id}"] polygon`)!.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();
  }

  it('tapRegion_PaintsWholeGroup_SameColorClears', () => {
    setup();
    click('r3');
    expect(emitted.at(-1)).toBe(fills({ g1: 'p1' }));
    expect(regionEl('r1').classList.contains('is-painted')).toBe(true);
    expect(regionEl('r3').classList.contains('is-painted')).toBe(true);
    expect(regionEl('r2').classList.contains('is-painted')).toBe(false);
    click('r1');
    expect(emitted.at(-1)).toBe(fills({}));
    expect(regionEl('r3').classList.contains('is-painted')).toBe(false);
  });

  it('groupList_PaintsFromKeyboard', () => {
    setup();
    expect(el().querySelectorAll('[data-group]').length).toBe(2);
    (el().querySelector('[data-paint="1"]') as HTMLButtonElement).click();
    groupBtn('g2').click();
    fixture.detectChanges();
    expect(emitted.at(-1)).toBe(fills({ g2: 'p2' }));
    expect(regionEl('r2').classList.contains('is-painted')).toBe(true);
  });

  it('feedback_MarksGroups_AndRegions', () => {
    setup({ answer: fills({ g1: 'p1', g2: 'p1' }), feedback: { rows: [{ id: 'g1', correct: true }, { id: 'g2', correct: false }] } });
    expect(regionEl('r1').getAttribute('data-mark')).toBe('correct');
    expect(regionEl('r3').getAttribute('data-mark')).toBe('correct');
    expect(regionEl('r2').getAttribute('data-mark')).toBe('color');
    expect(groupBtn('g1').classList.contains('is-ok')).toBe(true);
    expect(groupBtn('g2').classList.contains('is-bad')).toBe(true);
    expect(q('rp-result')?.classList.contains('is-bad')).toBe(true);
  });

  it('solution_PaintsEveryGroup_ReadOnly', () => {
    setup({ showSolution: true });
    expect(['r1', 'r2', 'r3'].every((id) => regionEl(id).classList.contains('is-painted'))).toBe(true);
    expect(groupBtn('g1').disabled).toBe(true);
    click('r2');
    expect(emitted).toEqual([]);
  });
});
