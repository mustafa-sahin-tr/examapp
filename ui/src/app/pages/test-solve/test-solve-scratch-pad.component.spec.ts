import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import testSolveTr from '../../../../public/i18n/test-solve/tr.json';
import { TestSolveScratchPadComponent } from './test-solve-scratch-pad.component';
import { readScratchNotes, writeScratchNotes } from './test-solve-scratch-store';

const INSTANCE_ID = 30;

describe('TestSolveScratchPadComponent (#782)', () => {
  let fixture: ComponentFixture<TestSolveScratchPadComponent>;

  const root = () => fixture.nativeElement as HTMLElement;
  const surface = () => root().querySelector<SVGSVGElement>('.scratch__surface')!;
  const paths = () => root().querySelectorAll('path').length;
  const button = (label: string) => root().querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)!;

  function pointer(type: string, x: number, y: number, init: PointerEventInit = {}) {
    const rect = surface().getBoundingClientRect();
    surface().dispatchEvent(
      new PointerEvent(type, { pointerId: 1, isPrimary: true, pointerType: 'mouse', bubbles: true, clientX: rect.left + x, clientY: rect.top + y, ...init }),
    );
    fixture.detectChanges();
  }

  function draw(init: PointerEventInit = {}) {
    pointer('pointerdown', 10, 10, init);
    pointer('pointermove', 60, 40, init);
    pointer('pointerup', 60, 40, init);
  }

  function create(questionId = 1) {
    fixture = TestBed.createComponent(TestSolveScratchPadComponent);
    fixture.componentRef.setInput('instanceId', INSTANCE_ID);
    fixture.componentRef.setInput('questionId', questionId);
    (fixture.nativeElement as HTMLElement).style.cssText = 'display:block;width:300px;height:300px';
    fixture.detectChanges();
  }

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [
        TestSolveScratchPadComponent,
        NoopAnimationsModule,
        TranslocoTestingModule.forRoot({
          langs: { tr: { 'test-solve': testSolveTr } },
          translocoConfig: { availableLangs: ['tr'], defaultLang: 'tr' },
        }),
      ],
    });
  });

  afterEach(() => {
    vi.useRealTimers();
    localStorage.clear();
  });

  it('a stroke is saved on this device only when it ends', () => {
    create();

    pointer('pointerdown', 10, 10);
    pointer('pointermove', 60, 40);
    expect(paths()).toBe(1);
    expect(readScratchNotes(INSTANCE_ID)[1]).toBeUndefined();

    pointer('pointerup', 60, 40);
    const [stroke] = readScratchNotes(INSTANCE_ID)[1].strokes;
    expect(stroke).toMatch(/^M\d+ \d+L\d+ \d+L\d+ \d+$/);
    expect(paths()).toBe(1);
  });

  it('coordinates are relative to width, so a note survives a resize', () => {
    create();

    draw();

    const width = surface().getBoundingClientRect().width;
    const expected = Math.round((60 * 1000) / width);
    expect(readScratchNotes(INSTANCE_ID)[1].strokes[0]).toContain(`L${expected} `);
  });

  it('a single tap leaves a visible dot', () => {
    create();

    pointer('pointerdown', 10, 10);
    pointer('pointerup', 10, 10);

    expect(readScratchNotes(INSTANCE_ID)[1].strokes[0]).toMatch(/^M(\d+) (\d+)L\1 \2$/);
  });

  it('notes belong to the question and come back on return', () => {
    create(1);
    draw();

    fixture.componentRef.setInput('questionId', 2);
    fixture.detectChanges();
    expect(paths()).toBe(0);

    fixture.componentRef.setInput('questionId', 1);
    fixture.detectChanges();
    expect(paths()).toBe(1);
  });

  it('notes reload when the pad opens again', () => {
    writeScratchNotes(INSTANCE_ID, { 1: { strokes: ['M1 2L3 4'], text: '' } });

    create(1);

    expect(paths()).toBe(1);
  });

  it('a stroke stays with the question it started on', () => {
    create(1);

    pointer('pointerdown', 10, 10);
    fixture.componentRef.setInput('questionId', 2);
    fixture.detectChanges();
    pointer('pointerup', 10, 10);

    const notes = readScratchNotes(INSTANCE_ID);
    expect(notes[1].strokes.length).toBe(1);
    expect(notes[2]).toBeUndefined();
  });

  it('undo removes the last stroke, and clear can be undone', () => {
    create();
    draw();
    draw();
    expect(paths()).toBe(2);

    button(testSolveTr.scratch.undo).click();
    fixture.detectChanges();
    expect(paths()).toBe(1);

    button(testSolveTr.scratch.clear).click();
    fixture.detectChanges();
    expect(paths()).toBe(0);
    expect(readScratchNotes(INSTANCE_ID)[1].strokes).toEqual([]);

    button(testSolveTr.scratch.undo).click();
    fixture.detectChanges();
    expect(paths()).toBe(1);
    expect(button(testSolveTr.scratch.undo).disabled).toBe(false);
  });

  it('touch does not draw once a pen has been used', () => {
    create();
    draw({ pointerType: 'pen' });
    expect(paths()).toBe(1);

    draw({ pointerType: 'touch' });

    expect(paths()).toBe(1);
  });

  it('a second finger does not start another stroke', () => {
    create();

    pointer('pointerdown', 10, 10);
    pointer('pointerdown', 80, 80, { pointerId: 2, isPrimary: false });
    pointer('pointerup', 80, 80, { pointerId: 2, isPrimary: false });
    expect(readScratchNotes(INSTANCE_ID)[1]).toBeUndefined();

    pointer('pointerup', 10, 10);
    expect(readScratchNotes(INSTANCE_ID)[1].strokes.length).toBe(1);
  });

  it('typing is saved shortly after, and on close if that is sooner', async () => {
    vi.useFakeTimers();
    create();
    root().querySelectorAll<HTMLElement>('mat-button-toggle button')[1].click();
    fixture.detectChanges();

    const textarea = root().querySelector('textarea')!;
    textarea.value = 'x = 4';
    textarea.dispatchEvent(new Event('input'));
    expect(readScratchNotes(INSTANCE_ID)[1]).toBeUndefined();

    await vi.advanceTimersByTimeAsync(500);
    expect(readScratchNotes(INSTANCE_ID)[1].text).toBe('x = 4');

    textarea.value = 'x = 5';
    textarea.dispatchEvent(new Event('input'));
    fixture.destroy();
    expect(readScratchNotes(INSTANCE_ID)[1].text).toBe('x = 5');
  });

  it('says so when this device cannot save', () => {
    create();
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('full', 'QuotaExceededError');
    });

    draw();
    vi.restoreAllMocks();

    expect(root().querySelector('[role="alert"]')?.textContent).toContain(testSolveTr.scratch.saveError);
    expect(paths()).toBe(1);
  });

  it('the close button asks the screen to close', () => {
    create();
    const closed = vi.fn();
    fixture.componentInstance.closed.subscribe(closed);

    button(testSolveTr.scratch.close).click();

    expect(closed).toHaveBeenCalledTimes(1);
  });
});
