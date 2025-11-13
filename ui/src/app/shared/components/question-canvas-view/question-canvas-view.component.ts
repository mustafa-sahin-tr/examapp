import {
  AfterViewChecked,
  AfterViewInit,
  Component,
  effect,
  ElementRef,
  EventEmitter,
  Input,
  Output,
  signal,
  ViewChild,
} from '@angular/core';
import { AnswerChoice, QuestionRegion } from '../../../models/draws';
import { SafeHtmlPipe } from '../../../services/safehtml';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-question-canvas-view',
  imports: [SafeHtmlPipe, MatButtonModule, MatIconModule],
  templateUrl: './question-canvas-view.component.html',
  styleUrl: './question-canvas-view.component.scss',
})
export class QuestionCanvasViewComponent implements AfterViewInit, AfterViewChecked {
  @ViewChild('passagecanvas', { static: false }) passageCanvas!: ElementRef<HTMLCanvasElement>;
  @ViewChild('canvas', { static: true }) canvas!: ElementRef<HTMLCanvasElement>;

  private psgctx!: CanvasRenderingContext2D | null;
  private canvasCtx!: CanvasRenderingContext2D | null;
  private img = new Image();
  private passageImg = new Image();
  public imageCache = new Map<string, HTMLImageElement>();
  public currentImageId = signal<string | null>(null);
  public currentPassageImageId = signal<string | null>(null);

  public _questionRegion = signal<QuestionRegion>({} as QuestionRegion);
  public isImageLoaded = signal(false);

  // Keep canvas size fixed; scale content inside
  private baseCanvasWidth?: number;
  private baseCanvasHeight?: number;
  public contentScale = 1;

  private shouldInitCanvas = false;

  @Input({ required: true }) set questionRegion(value: QuestionRegion) {
    this._questionRegion.set(value);

    if (value?.passage) this.shouldInitCanvas = true;

    // Fix canvas to initial region size and reset scale
    if (value) {
      this.baseCanvasWidth = value.width;
      this.baseCanvasHeight = value.height;
      this.contentScale = 1;
    }

    // Auto select correct answer for convenience
    if (value?.answers) {
      const correctAnswer = value.answers.find((a) => a.isCorrect);
      if (correctAnswer) {
        this._selectedChoice.set(correctAnswer);
        this._correctChoice.set(correctAnswer);
      }
    }
  }

  @Input() correctAnswerVisible: boolean = false;
  @Input() isPreviewMode: boolean = false;

  @Input() set selectedChoice(choice: AnswerChoice | undefined) {
    this._selectedChoice.set(choice);
  }

  @Input() set correctChoice(choice: AnswerChoice | undefined) {
    this._correctChoice.set(choice);
  }

  @Input() set mode(m: 'exam' | 'result' | null) {
    this._mode.set(m || 'exam');
  }

  @Output() hoverRegion = new EventEmitter<MouseEvent>();
  @Output() selectRegion = new EventEmitter<MouseEvent>();
  @Output() choiceSelected = new EventEmitter<AnswerChoice>();
  @Output() answerOpened = new EventEmitter<number>();
  @Output() questionRemove = new EventEmitter<number>();

  public hoveredChoice = signal<AnswerChoice | null>(null);
  private _selectedChoice = signal<AnswerChoice | undefined>(undefined);
  private _correctChoice = signal<AnswerChoice | undefined>(undefined);
  private _mode = signal<'exam' | 'result'>('exam');

  constructor() {
    effect(() => {
      const region = this._questionRegion();
      if (!region || !region.imageUrl) return;
      // Initialize base canvas dims if not set
      this.baseCanvasWidth = this.baseCanvasWidth ?? region.width;
      this.baseCanvasHeight = this.baseCanvasHeight ?? region.height;
      this.loadImageForQuestion();
    });
  }

  ngAfterViewChecked() {
    if (this.shouldInitCanvas) {
      this.shouldInitCanvas = false;
      this.psgctx = this.passageCanvas?.nativeElement.getContext('2d');
    }
  }

  ngAfterViewInit() {
    if (this.passageCanvas) this.psgctx = this.passageCanvas.nativeElement.getContext('2d');
    if (this.canvas) this.canvasCtx = this.canvas.nativeElement.getContext('2d');
  }

  private async loadImageForQuestion() {
    const region = this._questionRegion();
    if (!region?.imageUrl || !region.imageId) return;

    const cached = this.imageCache.get(region.imageId);
    if (cached) {
      this.img = cached;
      this.isImageLoaded.set(true);
      this.currentImageId.set(region.imageId);

      if (region?.passage?.imageUrl && region?.passage?.imageId) {
        const psgCached = this.imageCache.get(region.passage?.imageId || '');
        if (psgCached) {
          this.img = psgCached;
          this.isImageLoaded.set(true);
          this.currentPassageImageId.set(region.passage?.imageId!);
        }
      }

      this.drawImageSection();
      return;
    }

    this.img = new Image();
    this.img.src = region.imageUrl;
    await new Promise<void>((resolve, reject) => {
      this.img.onload = () => {
        this.imageCache.set(region.imageId!, this.img);
        this.isImageLoaded.set(true);
        this.currentImageId.set(region.imageId!);
        resolve();
      };
      this.img.onerror = () => reject();
    });

    if (region?.passage?.imageUrl && region?.passage?.imageId) {
      this.passageImg = new Image();
      this.passageImg.src = region.passage.imageUrl;
      await new Promise<void>((resolve, reject) => {
        this.passageImg.onload = () => {
          this.imageCache.set(region.passage?.imageId!, this.passageImg);
          this.isImageLoaded.set(true);
          this.currentPassageImageId.set(region.passage?.imageId!);
          resolve();
        };
        this.passageImg.onerror = () => reject();
      });
    }

    this.drawImageSection();
  }

  private drawPassageSection() {
    const region = this._questionRegion();
    if (!this.psgctx || !this.passageCanvas || !this.isImageLoaded()) return;
    if (!region?.passage) {
      // Hide passage canvas completely
      if (this.passageCanvas?.nativeElement) {
        this.passageCanvas.nativeElement.width = 0;
        this.passageCanvas.nativeElement.height = 0;
        this.psgctx?.clearRect(0, 0, 0, 0);
      }
      return;
    }

    const passageCanvasEl = this.passageCanvas.nativeElement;
    const targetW = region.passage.width * this.contentScale;
    const targetH = region.passage.height * this.contentScale;
    passageCanvasEl.width = targetW;
    passageCanvasEl.height = targetH;

    this.psgctx.clearRect(0, 0, passageCanvasEl.width, passageCanvasEl.height);
    this.psgctx.drawImage(
      this.passageImg,
      0, //region.passage.x || 0,
      0, //region.passage.y || 0,
      region.passage.width || 0,
      region.passage.height || 0,
      0,
      0,
      targetW, // region.passage.width || 0,
      targetH // region.passage.height || 0
    );
  }

  private drawAnswer(answer: AnswerChoice, color: string) {
    if (!this.canvasCtx) return;
    const region = this._questionRegion();
    const scale = this.contentScale;

    const x = (answer.x - region.x) * scale;
    const y = (answer.y - region.y) * scale;
    const w = answer.width * scale;
    const h = answer.height * scale;

    const gradient = this.canvasCtx.createLinearGradient(x, y, x + w, y + h);
    gradient.addColorStop(0, color);
    gradient.addColorStop(1, color);

    this.canvasCtx.fillStyle = gradient;
    this.canvasCtx.fillRect(x, y, w, h);
    this.canvasCtx.fill();

    this.canvasCtx.strokeStyle = 'black';
    this.canvasCtx.lineWidth = 2;
    this.canvasCtx.strokeRect(x, y, w, h);
    this.canvasCtx.stroke();
  }

  drawImageSection() {
    if (!this.isImageLoaded()) return;
    this.drawPassageSection();

    const canvasEl = this.canvas.nativeElement;
    this.canvasCtx = canvasEl.getContext('2d');

    if (!this.canvasCtx) return;

    const region = this._questionRegion();

    // Keep canvas fixed sized
    const targetW = (this.baseCanvasWidth ?? region.width) * this.contentScale;
    const targetH = (this.baseCanvasHeight ?? region.height) * this.contentScale;
    canvasEl.width = targetW;
    canvasEl.height = targetH;

    this.canvasCtx.clearRect(0, 0, canvasEl.width, canvasEl.height);

    // Draw scaled image section
    const dstW = Math.round(region.width * this.contentScale);
    const dstH = Math.round(region.height * this.contentScale);

    this.canvasCtx.drawImage(this.img, region.x, region.y, region.width, region.height, 0, 0, dstW, dstH);

    // Overlays
    for (const answer of region.answers || []) {
      const isSelected = this._selectedChoice() === answer;
      const isHovered = this.hoveredChoice() === answer;
      const isCorrect = !!answer.isCorrect;

      if (this._mode() === 'exam') {
        if (isCorrect || isSelected) {
          this.drawAnswer(answer, 'rgba(76, 195, 80, 0.3)');
        } else if (isHovered) {
          const scale = this.contentScale;
          const x = (answer.x - region.x) * scale;
          const y = (answer.y - region.y) * scale;
          const w = answer.width * scale;
          const h = answer.height * scale;
          this.canvasCtx.fillStyle = 'rgba(0, 0, 255, 0.3)';
          this.canvasCtx.fillRect(x, y, w, h);
        }
      } else if (this._mode() === 'result') {
        if (isCorrect) this.drawAnswer(answer, 'rgba(76, 195, 80, 0.3)');
        if (isSelected && !isCorrect) this.drawAnswer(answer, 'rgba(234, 21, 21, 0.46)');
      }
    }
  }

  onMouseMove(event: MouseEvent) {
    if (!this.canvas?.nativeElement) return;

    const rect = this.canvas.nativeElement.getBoundingClientRect();
    const mouseX = event.clientX - rect.left;
    const mouseY = event.clientY - rect.top;

    // Normalize to unscaled coordinate space
    const scale = this.contentScale || 1;
    const nx = mouseX / scale;
    const ny = mouseY / scale;

    let found = false;

    for (const answer of this._questionRegion().answers || []) {
      if (
        nx >= answer.x - this._questionRegion().x &&
        nx <= answer.x - this._questionRegion().x + answer.width &&
        ny >= answer.y - this._questionRegion().y &&
        ny <= answer.y - this._questionRegion().y + answer.height
      ) {
        this.hoveredChoice.set(answer);
        this.canvas.nativeElement.style.cursor = 'pointer';
        found = true;
        break;
      }
    }

    if (!found) {
      this.hoveredChoice.set(null);
      this.canvas.nativeElement.style.cursor = 'auto';
    }

    this.drawImageSection();
  }

  selectChoice(event: MouseEvent) {
    if (!this.canvas?.nativeElement) return;

    const rect = this.canvas.nativeElement.getBoundingClientRect();
    const mouseX = event.clientX - rect.left;
    const mouseY = event.clientY - rect.top;

    const scale = this.contentScale || 1;
    const nx = mouseX / scale;
    const ny = mouseY / scale;

    for (const answer of this._questionRegion().answers || []) {
      if (
        nx >= answer.x - this._questionRegion().x &&
        nx <= answer.x - this._questionRegion().x + answer.width &&
        ny >= answer.y - this._questionRegion().y &&
        ny <= answer.y - this._questionRegion().y + answer.height
      ) {
        answer.scale = this.contentScale;
        this._selectedChoice.set(answer);
        this.choiceSelected.emit(answer);
        break;
      }
    }

    this.drawImageSection();
  }

  showCorrectAnswer() {
    this.answerOpened.emit(1);
  }

  removeQuestion() {
    this.questionRemove.emit(this._questionRegion().id);
  }

  enlargeImage() {
    this.rescaleQuestion(1.05);
  }

  shrinkImage() {
    this.rescaleQuestion(0.95);
  }

  retsetImageScale() {
    this.contentScale = 1;
    this.drawImageSection();
  }

  public rescaleQuestion(factor: number) {
    // Only scale the drawn content; keep canvas size fixed
    const next = this.contentScale * factor;
    // Clamp for safety
    this.contentScale = Math.max(0.2, Math.min(3, next));
    console.log(`[QuestionCanvasView] Rescaled content to ${this.contentScale.toFixed(2)}x`);
    this.drawImageSection();
  }
  getCanvasHeights(): { questionHeight: number; passageHeight: number; hasPassageImage: boolean } {
    const region = this._questionRegion();
    const baseQuestionHeight = this.baseCanvasHeight ?? region?.height ?? 0;
    const questionHeight = Math.round(baseQuestionHeight * this.contentScale) || 0;

    const passageImageUrl = region?.passage?.imageUrl?.trim() || '';
    const hasPassageImage = passageImageUrl.length > 0;

    let passageHeight = 0;
    if (hasPassageImage) {
      const basePassageHeight = region?.passage?.height || this.passageImg?.naturalHeight || 0;
      const effectiveBase = basePassageHeight > 0 ? basePassageHeight : this.passageCanvas?.nativeElement?.height ?? 0;
      passageHeight = Math.round(effectiveBase * this.contentScale) || 0;
    }

    return { questionHeight, passageHeight, hasPassageImage };
  }

  getCanvasWidths(): { questionWidth: number; passageWidth: number; hasPassageImage: boolean } {
    const region = this._questionRegion();
    const baseQuestionWidth = this.baseCanvasWidth ?? region?.width ?? 0;
    const questionWidth = Math.round(baseQuestionWidth * this.contentScale) || 0;

    const passageImageUrl = region?.passage?.imageUrl?.trim() || '';
    const hasPassageImage = passageImageUrl.length > 0;

    let passageWidth = 0;
    if (hasPassageImage) {
      const basePassageWidth = region?.passage?.width || this.passageImg?.naturalWidth || 0;
      const effectiveBase = basePassageWidth > 0 ? basePassageWidth : this.passageCanvas?.nativeElement?.width ?? 0;
      passageWidth = Math.round(effectiveBase * this.contentScale) || 0;
    }

    return { questionWidth, passageWidth, hasPassageImage };
  }
}
