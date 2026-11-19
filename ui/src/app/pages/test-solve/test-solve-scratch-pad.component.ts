import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  OnInit,
  computed,
  effect,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  SCRATCH_TEXT_MAX_LENGTH,
  SCRATCH_VIEW_WIDTH,
  ScratchNote,
  ScratchNotes,
  purgeOldScratchNotes,
  readScratchNotes,
  writeScratchNotes,
} from './test-solve-scratch-store';
import { colorStroke, penColor, penColorCss, strokeCss, strokePath } from '../../shared/utils/pen-color';
import { PenColorPickerComponent } from '../../shared/components/pen-color-picker/pen-color-picker.component';
import { ScratchStrokeInput } from './scratch-stroke-input';

const EMPTY_NOTE: ScratchNote = { strokes: [], text: '' };
const TEXT_SAVE_DELAY_MS = 400;

/**
 * Issue #782: soru başına karalama alanı. Çizim parmak, kalem ya da fareyle; yazı klavye ve ekran okuyucu içindir.
 * Puanı etkilemez, sunucuya gitmez.
 */
@Component({
  selector: 'app-test-solve-scratch-pad',
  standalone: true,
  imports: [
    MatButtonModule,
    MatButtonToggleModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    PenColorPickerComponent,
    TranslocoDirective,
  ],
  templateUrl: './test-solve-scratch-pad.component.html',
  styleUrl: './test-solve-scratch-pad.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TestSolveScratchPadComponent implements OnInit, OnDestroy {
  readonly instanceId = input.required<number>();
  /** `TestInstanceQuestion.id`. */
  readonly questionId = input.required<number>();
  readonly closed = output<void>();

  protected readonly viewWidth = SCRATCH_VIEW_WIDTH;
  protected readonly textMaxLength = SCRATCH_TEXT_MAX_LENGTH;
  protected readonly mode = signal<'draw' | 'write'>('draw');
  protected readonly notes = signal<ScratchNotes>({});
  protected readonly note = computed(() => this.notes()[this.questionId()] ?? EMPTY_NOTE);
  /** Çizilmekte olan çizgi; bırakılınca nota eklenir. */
  protected readonly currentStroke = computed(() => this.strokeInput.current());
  protected readonly currentCss = computed(() => penColorCss(penColor()));
  protected readonly strokePath = strokePath;
  protected readonly strokeCss = strokeCss;
  protected readonly viewHeight = signal(SCRATCH_VIEW_WIDTH);
  protected readonly saveFailed = signal(false);
  /** Temizlenen çizgiler soru başına; temizleme de geri alınabilir. */
  private readonly clearedStrokes = signal<Record<number, string[]>>({});
  protected readonly canUndo = computed(
    () => this.note().strokes.length > 0 || !!this.clearedStrokes()[this.questionId()]?.length,
  );

  private readonly surface = viewChild<ElementRef<SVGSVGElement>>('surface');
  private resizeObserver: ResizeObserver | null = null;
  private readonly strokeInput = new ScratchStrokeInput();
  private strokeQuestionId: number | null = null;
  private textSaveTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    // Çizim alanı yazı modunda DOM'da değil: mod her değiştiğinde yeniden ölçülür.
    effect((onCleanup) => {
      const element = this.surface()?.nativeElement;
      if (!element) return;
      this.measure(element);
      this.resizeObserver = new ResizeObserver(() => this.measure(element));
      this.resizeObserver.observe(element);
      onCleanup(() => {
        this.resizeObserver?.disconnect();
        this.resizeObserver = null;
      });
    });
  }

  ngOnInit(): void {
    purgeOldScratchNotes();
    this.notes.set(readScratchNotes(this.instanceId()));
  }

  ngOnDestroy(): void {
    if (this.textSaveTimer !== null) {
      clearTimeout(this.textSaveTimer);
      this.save();
    }
  }

  protected startStroke(event: PointerEvent): void {
    if (this.strokeInput.start(event)) this.strokeQuestionId = this.questionId();
  }

  protected extendStroke(event: PointerEvent): void {
    this.strokeInput.extend(event);
  }

  protected endStroke(event: PointerEvent): void {
    const stroke = this.strokeInput.end(event);
    const questionId = this.strokeQuestionId;
    if (stroke === null) return;
    this.strokeQuestionId = null;
    if (questionId !== null) {
      const note = this.notes()[questionId] ?? EMPTY_NOTE;
      this.updateNote(questionId, { ...note, strokes: [...note.strokes, colorStroke(stroke, penColor())] });
      this.forgetCleared(questionId);
      this.save();
    }
  }

  protected undo(): void {
    const questionId = this.questionId();
    const cleared = this.clearedStrokes()[questionId];
    if (cleared?.length) {
      this.updateNote(questionId, { ...this.note(), strokes: cleared });
      this.forgetCleared(questionId);
    } else {
      this.updateNote(questionId, { ...this.note(), strokes: this.note().strokes.slice(0, -1) });
    }
    this.save();
  }

  protected clearDrawing(): void {
    const questionId = this.questionId();
    const strokes = this.note().strokes;
    if (!strokes.length) return;
    this.clearedStrokes.update((all) => ({ ...all, [questionId]: strokes }));
    this.updateNote(questionId, { ...this.note(), strokes: [] });
    this.save();
  }

  protected changeText(text: string): void {
    this.updateNote(this.questionId(), { ...this.note(), text });
    if (this.textSaveTimer !== null) clearTimeout(this.textSaveTimer);
    this.textSaveTimer = setTimeout(() => {
      this.textSaveTimer = null;
      this.save();
    }, TEXT_SAVE_DELAY_MS);
  }

  private updateNote(questionId: number, note: ScratchNote): void {
    this.notes.update((notes) => ({ ...notes, [questionId]: note }));
  }

  private forgetCleared(questionId: number): void {
    this.clearedStrokes.update(({ [questionId]: _removed, ...rest }) => rest);
  }

  private save(): void {
    this.saveFailed.set(!writeScratchNotes(this.instanceId(), this.notes()));
  }

  private measure(element: SVGSVGElement): void {
    const { width, height } = element.getBoundingClientRect();
    if (width > 0 && height > 0) {
      this.viewHeight.set(Math.round((height / width) * SCRATCH_VIEW_WIDTH));
    }
  }
}
