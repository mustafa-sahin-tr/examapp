import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  linkedSignal,
  signal,
} from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { colorStroke, penColor, penColorCss, strokeCss, strokePath } from '../../shared/utils/pen-color';
import { ScratchStrokeInput } from './scratch-stroke-input';
import { SCRATCH_VIEW_WIDTH, readInkStrokes, writeInkStrokes } from './test-solve-scratch-store';

/**
 * Issue #1175: soru görselinin üstünde kalemle karalama katmanı. Çizim yalnız {@link active} iken; kapalıyken çizgiler
 * görünür ama dokunuş görsele geçer. Karalama alanı (#782) gibi cihazda tutulur, #1310 ile sunucuya da gider (öğretmen
 * ve veli görür); test bitince cihazdan silinir.
 */
@Component({
  selector: 'app-test-solve-question-ink',
  standalone: true,
  imports: [TranslocoDirective],
  templateUrl: './test-solve-question-ink.component.html',
  styleUrl: './test-solve-question-ink.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.is-active]': 'active()' },
})
export class TestSolveQuestionInkComponent {
  readonly instanceId = input.required<number>();
  /** `TestInstanceQuestion.id`. */
  readonly questionId = input.required<number>();
  readonly active = input(false);

  protected readonly viewWidth = SCRATCH_VIEW_WIDTH;
  protected readonly viewHeight = signal(SCRATCH_VIEW_WIDTH);
  readonly strokes = linkedSignal(() => readInkStrokes(this.instanceId(), this.questionId()));
  /** Temizlenen çizgiler; temizleme de geri alınabilir. Soru değişince unutulur. */
  private readonly cleared = linkedSignal<number, string[] | null>({ source: this.questionId, computation: () => null });
  readonly canUndo = computed(() => this.strokes().length > 0 || !!this.cleared()?.length);
  readonly saveFailed = signal(false);

  private readonly strokeInput = new ScratchStrokeInput();
  protected readonly currentStroke = this.strokeInput.current;
  protected readonly currentCss = computed(() => penColorCss(penColor()));
  protected readonly strokePath = strokePath;
  protected readonly strokeCss = strokeCss;
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  constructor() {
    effect((onCleanup) => {
      const element = this.host.nativeElement;
      this.measure(element);
      const observer = new ResizeObserver(() => this.measure(element));
      observer.observe(element);
      onCleanup(() => observer.disconnect());
    });
  }

  protected startStroke(event: PointerEvent): void {
    if (this.active()) this.strokeInput.start(event);
  }

  protected extendStroke(event: PointerEvent): void {
    this.strokeInput.extend(event);
  }

  protected endStroke(event: PointerEvent): void {
    const stroke = this.strokeInput.end(event);
    if (stroke === null) return;
    this.cleared.set(null);
    this.save([...this.strokes(), colorStroke(stroke, penColor())]);
  }

  undo(): void {
    const cleared = this.cleared();
    this.cleared.set(null);
    this.save(cleared?.length ? cleared : this.strokes().slice(0, -1));
  }

  clear(): void {
    const strokes = this.strokes();
    if (!strokes.length) return;
    this.cleared.set(strokes);
    this.save([]);
  }

  private save(strokes: string[]): void {
    this.strokes.set(strokes);
    this.saveFailed.set(!writeInkStrokes(this.instanceId(), this.questionId(), strokes));
  }

  private measure(element: HTMLElement): void {
    const { width, height } = element.getBoundingClientRect();
    if (width > 0 && height > 0) {
      this.viewHeight.set(Math.round((height / width) * SCRATCH_VIEW_WIDTH));
    }
  }
}
