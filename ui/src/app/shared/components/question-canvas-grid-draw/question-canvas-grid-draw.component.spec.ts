// Issue #588 (dilim c): kareli zeminde çizim oynatıcısı — sürükleyerek ve iki dokunuşla çizim, dokunarak silme, üst üste binme,
// klavye, yankı, geri bildirim, çözüm.
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { QuestionRegion } from '../../../models/draws';
import { GridDrawFeedback } from '../../../models/grid-draw';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { QuestionCanvasGridDrawComponent } from './question-canvas-grid-draw.component';

const PLAN = { version: 1, type: 'gridDraw', rows: 10, cols: 16 };
const SOLUTION = { perimeter: 14, all: true };

function region(): QuestionRegion {
  return { imageUrl: '', width: 0, height: 0, passage: null } as unknown as QuestionRegion;
}

const answer = (shapes: object[]) => JSON.stringify({ version: 1, type: 'gridDraw', shapes });

describe('QuestionCanvasGridDrawComponent (#588)', () => {
  let fixture: ComponentFixture<QuestionCanvasGridDrawComponent>;
  let emitted: string[];

  const el = () => fixture.nativeElement as HTMLElement;
  const q = (testId: string) => el().querySelector(`[data-testid="${testId}"]`) as HTMLElement | null;
  const board = () => q('gd-board') as unknown as SVGSVGElement;
  const shapes = () => el().querySelectorAll('[data-shape]');
  const last = () => JSON.parse(emitted.at(-1)!).shapes as object[];

  function setup(inputs: { answer?: string | null; feedback?: GridDrawFeedback | null; showSolution?: boolean } = {}): void {
    TestBed.configureTestingModule({ imports: [QuestionCanvasGridDrawComponent, translocoTestingModule()] });
    fixture = TestBed.createComponent(QuestionCanvasGridDrawComponent);
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

  /** Hücrenin ortasında işaretçi olayı. */
  function pointer(type: string, row: number, col: number): void {
    const rect = board().getBoundingClientRect();
    board().dispatchEvent(
      new PointerEvent(type, {
        bubbles: true,
        pointerId: 1,
        clientX: rect.left + ((col + 0.5) / PLAN.cols) * rect.width,
        clientY: rect.top + ((row + 0.5) / PLAN.rows) * rect.height,
      })
    );
    fixture.detectChanges();
  }

  const tap = (row: number, col: number) => {
    pointer('pointerdown', row, col);
    pointer('pointerup', row, col);
  };

  function drag(from: [number, number], to: [number, number]): void {
    pointer('pointerdown', ...from);
    pointer('pointermove', ...to);
    pointer('pointerup', ...to);
  }

  it('sürükleyerek dikdörtgen çizer', () => {
    setup();
    drag([1, 2], [3, 7]);
    expect(last()).toEqual([{ row: 1, col: 2, width: 6, height: 3 }]);
    expect(shapes().length).toBe(1);
    expect(el().textContent).toContain('6 × 3');
  });

  it('iki dokunuşla çizer (aynı kareye ikinci dokunuş 1 × 1), çizime dokunmak siler', () => {
    setup();
    tap(0, 0);
    expect(emitted.length).toBe(0);
    expect(q('gd-preview')).not.toBeNull();
    tap(2, 3);
    expect(last()).toEqual([{ row: 0, col: 0, width: 4, height: 3 }]);
    tap(5, 5);
    tap(5, 5);
    expect(last()).toEqual([
      { row: 0, col: 0, width: 4, height: 3 },
      { row: 5, col: 5, width: 1, height: 1 },
    ]);
    tap(1, 1);
    expect(last()).toEqual([{ row: 5, col: 5, width: 1, height: 1 }]);
  });

  it('üst üste binen çizimi reddeder', () => {
    setup({ answer: answer([{ row: 0, col: 0, width: 3, height: 3 }]) });
    drag([4, 4], [2, 2]);
    expect(emitted.length).toBe(0);
    expect(q('gd-notice')).not.toBeNull();
  });

  it('klavyeyle çizer ve siler', () => {
    setup();
    const key = (k: string) => {
      board().dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true }));
      fixture.detectChanges();
    };
    key('Enter');
    key('ArrowRight');
    key('ArrowDown');
    key('Enter');
    expect(last()).toEqual([{ row: 0, col: 0, width: 2, height: 2 }]);
    key('Delete');
    expect(last()).toEqual([]);
  });

  it('yankıda yeniden kurmaz; geri bildirimle çizimleri işaretler', () => {
    setup();
    drag([0, 0], [3, 2]);
    fixture.componentRef.setInput('answerPayloadJson', emitted.at(-1)!);
    fixture.componentRef.setInput('feedback', { shapes: ['ok'], found: 1, missing: 2, extra: 0 });
    fixture.detectChanges();
    expect(shapes()[0].classList).toContain('is-ok');
    expect(q('gd-result')?.textContent).toContain('2');
  });

  it('çözümde kurala uyan boyutları örnek yerleşimle çizer, salt okunur', () => {
    setup({ showSolution: true });
    expect(shapes().length).toBe(3);
    expect(q('gd-solution-hint')?.textContent).toContain('1 × 6, 2 × 5, 3 × 4');
    drag([8, 0], [9, 1]);
    expect(emitted.length).toBe(0);
  });
});
