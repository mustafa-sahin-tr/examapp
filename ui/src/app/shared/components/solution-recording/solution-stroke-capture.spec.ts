import { describe, expect, it } from 'vitest';
import { SolutionStrokeCapture } from './solution-stroke-capture';

describe('SolutionStrokeCapture (#1226)', () => {
  let now = 0;
  const surface = document.createElement('div');
  surface.getBoundingClientRect = () => ({ left: 0, top: 0, width: 500, height: 375 }) as DOMRect;

  function pointer(type: string, x: number, y: number, init: PointerEventInit = {}): PointerEvent {
    const event = new PointerEvent(type, { pointerId: 1, isPrimary: true, pointerType: 'pen', clientX: x, clientY: y, cancelable: true, ...init });
    Object.defineProperty(event, 'currentTarget', { value: surface });
    return event;
  }

  function create(): SolutionStrokeCapture {
    now = 0;
    return new SolutionStrokeCapture(() => now, 1000, 750);
  }

  function draw(capture: SolutionStrokeCapture, at: number): void {
    now = at;
    capture.start(pointer('pointerdown', 10, 10), 'red');
    now = at + 120;
    capture.extend(pointer('pointermove', 60, 40));
    capture.end(pointer('pointerup', 60, 40));
  }

  it('records points in page units with the time since the stroke began', () => {
    const capture = create();
    draw(capture, 1500);

    expect(capture.strokes()).toEqual([{ c: 'red', t: 1500, p: [20, 20, 0, 120, 80, 120] }]);
    expect(capture.visible()).toEqual([{ d: 'M20 20L20 20L120 80', css: '#e53935' }]);
  });

  it('undo and clear keep the stroke and stamp when it disappears', () => {
    const capture = create();
    draw(capture, 1000);
    draw(capture, 2000);

    now = 3000;
    capture.undo();
    expect(capture.strokes().map((s) => s.e)).toEqual([undefined, 3000]);
    expect(capture.visible()).toHaveLength(1);

    now = 4000;
    capture.clear();
    expect(capture.strokes().map((s) => s.e)).toEqual([4000, 3000]);
    expect(capture.canUndo()).toBe(false);
  });

  it('ignores touch after a pen was used, and a second pointer', () => {
    const capture = create();
    draw(capture, 100);

    expect(capture.start(pointer('pointerdown', 5, 5, { pointerType: 'touch' }), 'default')).toBe(false);
    expect(capture.start(pointer('pointerdown', 5, 5), 'default')).toBe(true);
    expect(capture.start(pointer('pointerdown', 9, 9, { pointerId: 2 }), 'default')).toBe(false);
  });

  it('closes the stroke in progress when the recording pauses', () => {
    const capture = create();
    capture.start(pointer('pointerdown', 10, 10), 'default');
    capture.finish();

    expect(capture.current()).toBeNull();
    expect(capture.strokes()).toHaveLength(1);
  });
});
