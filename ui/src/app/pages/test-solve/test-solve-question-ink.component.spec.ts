import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import testSolveTr from '../../../../public/i18n/test-solve/tr.json';
import { selectPenColor } from '../../shared/utils/pen-color';
import { TestSolveQuestionInkComponent } from './test-solve-question-ink.component';
import { readInkStrokes, writeInkStrokes } from './test-solve-scratch-store';

const INSTANCE_ID = 31;

describe('TestSolveQuestionInkComponent (#1175)', () => {
  let fixture: ComponentFixture<TestSolveQuestionInkComponent>;

  const host = () => fixture.nativeElement as HTMLElement;
  const surface = () => host().querySelector<SVGSVGElement>('svg.ink')!;
  const paths = () => host().querySelectorAll('path').length;

  function pointer(type: string, x: number, y: number) {
    const rect = surface().getBoundingClientRect();
    surface().dispatchEvent(
      new PointerEvent(type, { pointerId: 1, isPrimary: true, pointerType: 'pen', bubbles: true, cancelable: true, clientX: rect.left + x, clientY: rect.top + y }),
    );
    fixture.detectChanges();
  }

  function draw() {
    pointer('pointerdown', 10, 10);
    pointer('pointermove', 60, 40);
    pointer('pointerup', 60, 40);
  }

  function create(active: boolean, questionId = 1) {
    fixture = TestBed.createComponent(TestSolveQuestionInkComponent);
    fixture.componentRef.setInput('instanceId', INSTANCE_ID);
    fixture.componentRef.setInput('questionId', questionId);
    fixture.componentRef.setInput('active', active);
    // Testte kapsayıcı çerçeve yok: katman kendi boyunu alır.
    host().style.cssText = 'position:relative;display:block;width:300px;height:200px';
    fixture.detectChanges();
  }

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [
        TestSolveQuestionInkComponent,
        TranslocoTestingModule.forRoot({
          langs: { tr: { 'test-solve': testSolveTr } },
          translocoConfig: { availableLangs: ['tr'], defaultLang: 'tr' },
        }),
      ],
    });
  });

  afterEach(() => {
    localStorage.clear();
    selectPenColor('default');
  });

  it('draws only while active and lets taps through otherwise', () => {
    create(false);
    expect(getComputedStyle(host()).pointerEvents).toBe('none');
    draw();
    expect(paths()).toBe(0);

    fixture.componentRef.setInput('active', true);
    fixture.detectChanges();
    expect(getComputedStyle(host()).pointerEvents).toBe('auto');
    expect(getComputedStyle(host()).touchAction).toBe('none');
    expect(surface().getAttribute('aria-label')).toBe(testSolveTr.ink.drawingAria);
    draw();

    expect(paths()).toBe(1);
    expect(readInkStrokes(INSTANCE_ID, 1)).toHaveLength(1);
  });

  it('keeps strokes visible when the pen is turned off', () => {
    writeInkStrokes(INSTANCE_ID, 1, ['M1 2L3 4']);
    create(false);
    expect(paths()).toBe(1);
  });

  it('strokes belong to the question and come back on return', () => {
    create(true, 1);
    draw();
    fixture.componentRef.setInput('questionId', 2);
    fixture.detectChanges();
    expect(paths()).toBe(0);

    fixture.componentRef.setInput('questionId', 1);
    fixture.detectChanges();
    expect(paths()).toBe(1);
  });

  it('new strokes take the selected pen colour and old ones keep theirs (#1241)', () => {
    writeInkStrokes(INSTANCE_ID, 1, ['M1 2L3 4']);
    create(true);
    selectPenColor('red');
    draw();

    const stored = readInkStrokes(INSTANCE_ID, 1);
    expect(stored[0]).toBe('M1 2L3 4');
    expect(stored[1]).toMatch(/^red:M/);
    const [old, coloured] = Array.from(host().querySelectorAll<SVGPathElement>('path'));
    expect(old.style.stroke).toBe('');
    expect(coloured.style.stroke).not.toBe('');
    expect(coloured.getAttribute('d')).toMatch(/^M/);
  });

  it('undo removes the last stroke, and clear can be undone', () => {
    create(true);
    draw();
    draw();
    const ink = fixture.componentInstance;

    ink.undo();
    fixture.detectChanges();
    expect(paths()).toBe(1);

    ink.clear();
    fixture.detectChanges();
    expect(paths()).toBe(0);
    expect(readInkStrokes(INSTANCE_ID, 1)).toEqual([]);
    expect(ink.canUndo()).toBe(true);

    ink.undo();
    fixture.detectChanges();
    expect(paths()).toBe(1);
    expect(readInkStrokes(INSTANCE_ID, 1)).toHaveLength(1);
  });
});
