import {
  AfterViewInit,
  ChangeDetectorRef,
  Component,
  ElementRef,
  NgZone,
  OnDestroy,
  ViewChild,
  effect,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { SafeHtmlPipe } from '../../../services/safehtml';
import { AnswerChoice, CanvasLayoutPlan } from '../../../models/draws';
import { QuestionCanvasViewComponentv2 } from '../question-canvas-view-v2/question-canvas-view-v2.component';

@Component({
  selector: 'app-question-canvas-view-v3',
  standalone: true,
  templateUrl: './question-canvas-view-v3.component.html',
  styleUrls: ['./question-canvas-view-v3.component.scss'],
  imports: [CommonModule, MatButtonModule, MatIconModule, SafeHtmlPipe],
})
export class QuestionCanvasViewComponentv3 extends QuestionCanvasViewComponentv2 implements AfterViewInit, OnDestroy {
  @ViewChild('questionImage')
  set questionImageRef(value: ElementRef<HTMLImageElement> | undefined) {
    const native = value?.nativeElement ?? null;

    if (native === this.questionImageEl) {
      return;
    }

    if (this.questionImageEl && this.resizeObserver) {
      this.resizeObserver.unobserve(this.questionImageEl);
    }

    this.questionImageEl = native;

    if (this.questionImageEl && this.resizeObserver) {
      this.resizeObserver.observe(this.questionImageEl);
    }
  }

  private questionImageEl: HTMLImageElement | null = null;

  private resizeObserver?: ResizeObserver;
  private layoutScale = 1;
  private userScale = 1;
  private lastBaseQuestionWidth = 0;
  private lastBaseQuestionHeight = 0;
  private currentRegionId: number | null = null;
  private readonly maxQuestionViewportHeight = 500;
  private readonly maxPassageViewportHeight = 360;

  constructor(private readonly ngZone: NgZone, private readonly cdr: ChangeDetectorRef) {
    super();

    effect(() => {
      const regionId = this._questionRegion()?.id ?? null;
      if (this.currentRegionId === regionId) {
        return;
      }

      this.currentRegionId = regionId;
      this.userScale = 1;
      this.layoutScale = 1;
      this.lastBaseQuestionWidth = 0;
      this.applyEffectiveScale();
    });
  }
  // Canvas v1 API'si ile uyum için boş bir yeniden çizim metodu bırakıyoruz.
  public drawImageSection(): void {
    // Görseller doğrudan <img> etiketleriyle render edildiği için manuel redraw gerekli değil.
  }

  public ngAfterViewInit(): void {
    this.ngZone.runOutsideAngular(() => {
      if (typeof ResizeObserver === 'undefined' || this.resizeObserver) {
        return;
      }

      this.resizeObserver = new ResizeObserver((entries) => {
        const entry = entries[0];
        if (!entry) return;

        const { width } = entry.contentRect;
        if (!Number.isFinite(width) || width <= 0) return;

        this.ngZone.run(() => this.updateLayoutScale(width));
      });

      if (this.questionImageEl) {
        this.resizeObserver.observe(this.questionImageEl);
      }
    });
  }

  public ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
    this.resizeObserver = undefined;
  }

  public override getQuestionWrapperStyle(): Record<string, string> {
    const widths = this.getCanvasWidths();
    const heights = this.getCanvasHeights();
    const region = this._questionRegion();
    const questionWidth = widths.questionWidth || region?.width || 0;
    const questionHeight = this.capQuestionHeight(heights.questionHeight || region?.height || 0);

    const maxWidth = questionWidth > 0 ? this.formatSize(questionWidth) : '100%';
    const maxHeight = questionHeight > 0 ? this.formatSize(questionHeight) : 'auto';

    return {
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      width: '100%',
      maxWidth,
      maxHeight,
      overflow: 'hidden',
      margin: '0 auto',
    };
  }

  public override getQuestionImageStyle(): Record<string, string> {
    const widths = this.getCanvasWidths();
    const heights = this.getCanvasHeights();
    const region = this._questionRegion();
    const questionWidth = widths.questionWidth || region?.width || 0;
    const questionHeight = this.capQuestionHeight(heights.questionHeight || region?.height || 0);

    const maxWidth = questionWidth > 0 ? this.formatSize(questionWidth) : '100%';
    const maxHeight = questionHeight > 0 ? this.formatSize(questionHeight) : 'auto';

    return {
      width: '100%',
      height: 'auto',
      objectFit: 'contain',
      maxWidth,
      maxHeight,
    };
  }

  public override getAnswerWrapperStyle(_answer?: AnswerChoice): Record<string, string> {
    if (!_answer) {
      return {
        width: '100%',
      };
    }

    const width = (_answer.width || 0) * this.contentScale;
    const height = (_answer.height || 0) * this.contentScale;

    const base = super.getAnswerWrapperStyle(_answer);
    const styles: Record<string, string> = {
      ...base,
      width: '100%',
      margin: '0',
      alignSelf: 'stretch',
      overflow: 'hidden',
    };

    if (width > 0) {
      styles['max-width'] = this.formatSize(width);
    }

    if (height > 0) {
      styles['max-height'] = this.formatSize(height);
    }

    return styles;
  }

  public override getAnswerImageStyle(_answer?: AnswerChoice): Record<string, string> {
    if (!_answer) {
      return {
        width: '100%',
        height: 'auto',
      };
    }

    const width = (_answer.width || 0) * this.contentScale;
    const height = (_answer.height || 0) * this.contentScale;

    const styles: Record<string, string> = {
      width: '100%',
      height: 'auto',
      objectFit: 'contain',
    };

    if (width > 0) {
      styles['max-width'] = this.formatSize(width);
    }

    if (height > 0) {
      styles['max-height'] = this.formatSize(height);
    }

    return styles;
  }

  public override getPassageWrapperStyle(): Record<string, string> {
    const widths = this.getCanvasWidths();
    const passageWidth = widths.passageWidth || widths.questionWidth || 0;

    const widthValue = passageWidth > 0 ? this.formatSize(passageWidth) : '100%';

    return {
      width: widthValue,
      maxWidth: '100%',
    };
  }

  public override getPassageImageStyle(): Record<string, string> {
    const widths = this.getCanvasWidths();
    const heights = this.getCanvasHeights();

    const passageWidth = widths.passageWidth || widths.questionWidth || 0;
    const passageHeight = this.capPassageHeight(heights.passageHeight || 0);

    const maxWidthValue = passageWidth > 0 ? this.formatSize(passageWidth) : '100%';
    const maxHeightValue = passageHeight > 0 ? this.formatSize(passageHeight) : 'auto';

    return {
      width: '100%',
      height: 'auto',
      objectFit: 'contain',
      maxWidth: maxWidthValue,
      maxHeight: maxHeightValue,
    };
  }

  public getAnswerGridColumn(answer: AnswerChoice): string | undefined {
    const columns = this.resolveAnswerColumnCount();
    if (columns <= 1) return undefined;

    const questionWidth = this.getScaledQuestionWidth();
    const answerWidth = this.getScaledAnswerWidth(answer);

    if (!questionWidth || !answerWidth) return undefined;

    const ratio = answerWidth / questionWidth;

    if (ratio >= 0.75) return '1 / -1';
    if (ratio >= 0.55 && columns >= 2) return `span ${Math.min(columns, 2)}`;

    return undefined;
  }

  public getAnswerGridTemplate(): string {
    const columns = this.resolveAnswerColumnCount();
    return `repeat(${columns}, minmax(0, 1fr))`;
  }

  public override getCanvasHeights(): { questionHeight: number; passageHeight: number; hasPassageImage: boolean } {
    const baseHeights = super.getCanvasHeights();
    const baseQuestionHeight = this.estimateBaseQuestionHeight(baseHeights.questionHeight);
    if (baseQuestionHeight > 0) {
      this.lastBaseQuestionHeight = baseQuestionHeight;
    }

    return {
      ...baseHeights,
      questionHeight: this.capQuestionHeight(baseHeights.questionHeight),
      passageHeight: this.capPassageHeight(baseHeights.passageHeight),
    };
  }

  public getCanvasLayoutClass(): string {
    const plan = this.getLayoutPlanHint();
    if (plan?.layoutClass) {
      console.log('layout class v3:', plan.layoutClass);
      return plan.layoutClass;
    }
    console.log('fallback layout class v3:', this.buildFallbackLayoutClass());
    return this.buildFallbackLayoutClass();
  }

  private formatSize(value: number): string {
    const safe = Number.isFinite(value) && value > 0 ? value : 0;
    if (!safe) {
      return 'auto';
    }

    return `${Math.max(Math.round(safe), 1)}px`;
  }

  private getScaledQuestionWidth(): number {
    const widths = this.getCanvasWidths();
    if (widths.questionWidth && widths.questionWidth > 0) {
      return widths.questionWidth;
    }

    const region = this._questionRegion();
    return (region?.width || 0) * this.contentScale;
  }

  private getScaledAnswerWidth(answer: AnswerChoice): number {
    if (!answer) return 0;
    return (answer.width || 0) * this.contentScale;
  }

  private resolveAnswerColumnCount(): number {
    const planColumns = this.getLayoutPlanHint()?.answerColumns;
    if (planColumns && Number.isFinite(planColumns) && planColumns > 0) {
      return Math.max(1, Math.round(planColumns));
    }

    return this.computeAutoAnswerColumns();
  }

  private computeAutoAnswerColumns(): number {
    const region = this._questionRegion();
    const answers = region?.answers || [];
    const answerCount = answers.length;
    if (answerCount === 0) return 1;

    const questionWidth = this.getScaledQuestionWidth();
    const gap = 16;
    const widths = answers
      .map((answer) => this.getScaledAnswerWidth(answer))
      .filter((value) => Number.isFinite(value) && value > 0);

    const maxAnswerWidth = widths.length ? Math.max(...widths) : 0;
    const minColumnWidth = Math.max(180, maxAnswerWidth);

    const maxColumns =
      questionWidth > 0 && minColumnWidth > 0
        ? Math.max(1, Math.floor((questionWidth + gap) / (minColumnWidth + gap)))
        : Math.max(1, answerCount);

    let columns = Math.max(1, Math.min(answerCount, maxColumns));

    if (answerCount === 4) {
      if (maxColumns >= 4) {
        columns = 4;
      } else if (columns === 3) {
        columns = maxColumns >= 2 ? 2 : 1;
      }
    } else if (answerCount === 5) {
      columns = Math.min(2, Math.max(1, maxColumns));
    }

    return Math.max(1, Math.min(answerCount, columns));
  }

  public override rescaleQuestion(factor: number): void {
    const nextUserScale = this.clampScale(this.userScale * factor);
    if (Math.abs(nextUserScale - this.userScale) < 0.001) {
      return;
    }

    this.userScale = nextUserScale;
    this.applyEffectiveScale();
  }

  public override retsetImageScale(): void {
    this.userScale = 1;
    this.applyEffectiveScale();
  }

  private updateLayoutScale(displayWidth: number): void {
    const widths = super.getCanvasWidths();
    const baseQuestionWidth = this.safeBaseSize(widths.questionWidth);
    if (baseQuestionWidth <= 0) {
      return;
    }

    const nextLayoutScale = this.clampScale(displayWidth / baseQuestionWidth);
    if (Math.abs(nextLayoutScale - this.layoutScale) < 0.001) {
      return;
    }

    this.layoutScale = nextLayoutScale;
    this.applyEffectiveScale();
  }

  private applyEffectiveScale(): void {
    let effectiveScale = this.clampScale(this.layoutScale * this.userScale);
    effectiveScale = this.limitScaleByHeight(effectiveScale);

    if (Math.abs(effectiveScale - this.contentScale) < 0.001) {
      return;
    }

    this.contentScale = effectiveScale;
    this.cdr.markForCheck();
  }

  private clampScale(value: number): number {
    const MIN_SCALE = 0.2;
    const MAX_SCALE = 3;
    if (!Number.isFinite(value) || value <= 0) {
      return MIN_SCALE;
    }

    return Math.min(Math.max(value, MIN_SCALE), MAX_SCALE);
  }

  private safeBaseSize(currentSize: number): number {
    if (!Number.isFinite(currentSize) || currentSize <= 0) {
      const regionWidth = this._questionRegion()?.width ?? 0;
      if (Number.isFinite(regionWidth) && regionWidth > 0) {
        this.lastBaseQuestionWidth = regionWidth;
        return regionWidth;
      }

      return this.lastBaseQuestionWidth;
    }

    const base = currentSize / (this.contentScale || 1);
    if (Number.isFinite(base) && base > 0) {
      this.lastBaseQuestionWidth = base;
      return base;
    }

    return this.lastBaseQuestionWidth;
  }

  private getLayoutPlanHint(): CanvasLayoutPlan | undefined {
    const region = this._questionRegion();
    return region?.layoutPlan;
  }

  private buildFallbackLayoutClass(): string {
    const region = this._questionRegion();
    const columns = this.computeAutoAnswerColumns();
    return this.composeLayoutClass(!!region?.passage, false, columns);
  }

  private composeLayoutClass(hasPassage: boolean, inlineAnswers: boolean, columns: number): string {
    const prefix = hasPassage ? 'canvas-layout--passage' : 'canvas-layout--solo';
    const flow = inlineAnswers ? 'inline' : 'stack';
    const sanitizedColumns = Math.max(1, Math.min(columns, 4));
    return `${prefix}-${flow}--cols-${sanitizedColumns}`;
  }

  private capQuestionHeight(value: number): number {
    if (!Number.isFinite(value) || value <= 0) {
      return value;
    }

    const limit = this.getQuestionHeightLimit();
    if (!Number.isFinite(limit) || limit <= 0) {
      return value;
    }

    return Math.min(value, limit);
  }

  private capPassageHeight(value: number): number {
    if (!Number.isFinite(value) || value <= 0) {
      return value;
    }

    const limit = this.getPassageHeightLimit();
    if (!Number.isFinite(limit) || limit <= 0) {
      return value;
    }

    return Math.min(value, limit);
  }

  private limitScaleByHeight(scale: number): number {
    const baseHeight = this.getBaseQuestionHeight();
    if (!Number.isFinite(baseHeight) || baseHeight <= 0) {
      return scale;
    }

    const heightLimit = this.getQuestionHeightLimit();
    if (!Number.isFinite(heightLimit) || heightLimit <= 0) {
      return scale;
    }

    const heightScale = heightLimit / baseHeight;
    if (!Number.isFinite(heightScale) || heightScale <= 0) {
      return scale;
    }

    const limited = Math.min(scale, heightScale);
    return this.clampScale(limited);
  }

  private getBaseQuestionHeight(): number {
    const regionHeight = this._questionRegion()?.height ?? 0;
    if (Number.isFinite(regionHeight) && regionHeight > 0) {
      return regionHeight;
    }

    if (Number.isFinite(this.lastBaseQuestionHeight) && this.lastBaseQuestionHeight > 0) {
      return this.lastBaseQuestionHeight;
    }

    return 0;
  }

  private estimateBaseQuestionHeight(scaledHeight: number): number {
    if (!Number.isFinite(scaledHeight) || scaledHeight <= 0) {
      return 0;
    }

    const currentScale = this.contentScale;
    if (!Number.isFinite(currentScale) || currentScale <= 0) {
      return 0;
    }

    const normalized = scaledHeight / currentScale;
    if (!Number.isFinite(normalized) || normalized <= 0) {
      return 0;
    }

    return normalized;
  }

  private getQuestionHeightLimit(): number {
    const scaleBoost = Math.max(this.userScale, 1);
    return this.maxQuestionViewportHeight * scaleBoost;
  }

  private getPassageHeightLimit(): number {
    const scaleBoost = Math.max(this.userScale, 1);
    return this.maxPassageViewportHeight * scaleBoost;
  }
}
