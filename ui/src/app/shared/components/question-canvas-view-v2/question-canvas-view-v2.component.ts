import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AnswerChoice, QuestionRegion } from '../../../models/draws';
import { SafeHtmlPipe } from '../../../services/safehtml';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

const EMPTY_REGION: QuestionRegion = {
  id: 0,
  name: '',
  x: 0,
  y: 0,
  width: 0,
  height: 0,
  passageId: '',
  answers: [],
  imageId: '',
  imageUrl: '',
  exampleAnswer: null,
  isExample: false,
};

@Component({
  selector: 'app-question-canvas-view-v2',
  imports: [CommonModule, SafeHtmlPipe, MatButtonModule, MatIconModule],
  templateUrl: './question-canvas-view-v2.component.html',
  styleUrls: ['./question-canvas-view-v2.component.scss'],
})
export class QuestionCanvasViewComponentv2 {
  public imageCache = new Map<string, HTMLImageElement>();
  public questionImageSource = signal<string | null>(null);
  public passageImageSource = signal<string | null>(null);

  public _questionRegion = signal<QuestionRegion>(EMPTY_REGION);
  public isImageLoaded = signal(false);

  // Track base dimensions for consistent scaling
  private baseCanvasWidth?: number;
  private baseCanvasHeight?: number;
  public contentScale = 1;
  private imageNaturalWidth = 0;
  private imageNaturalHeight = 0;
  private passageNaturalWidth = 0;
  private passageNaturalHeight = 0;
  private questionContentWidth = 0;
  private questionContentHeight = 0;

  /**
   * Cevap paneli için grid/flex ayarlarını döndürür.
   * Backend'den gelen answerColumns değerine göre otomatik ayarlanır.
   */
  public getAnswersPanelStyle(): Record<string, string> {
    const columns = this._questionRegion().layoutPlan?.answerColumns || 1;
    const layoutClass = this._questionRegion().layoutPlan?.layoutClass || '';
    if (layoutClass.includes('stack') && layoutClass.includes('cols-1')) {
      return {
        display: 'flex',
        flexDirection: 'column',
        gap: '12px',
        width: '100%',
        minHeight: '0',
        maxHeight: '100%',
        flexShrink: '1',
        alignItems: 'stretch',
        overflow: 'visible',
      };
    }
    if (columns > 1) {
      return {
        display: 'grid',
        gridTemplateColumns: `repeat(${columns}, 1fr)`,
        gap: '12px',
        width: '100%',
        height: '100%',
      };
    } else {
      return {
        display: 'flex',
        flexDirection: 'column',
        gap: '12px',
        width: '100%',
        height: '100%',
      };
    }
  }

  @Input({ required: true }) set questionRegion(value: QuestionRegion) {
    const region = value ?? EMPTY_REGION;
    this._questionRegion.set(region);

    this.questionContentWidth = 0;
    this.questionContentHeight = 0;
    this.baseCanvasWidth = undefined;
    this.baseCanvasHeight = undefined;

    this.contentScale = 1;
    this.isImageLoaded.set(false);
    this.imageNaturalWidth = 0;
    this.imageNaturalHeight = 0;
    this.passageNaturalWidth = 0;
    this.passageNaturalHeight = 0;
    this.hoveredChoice.set(null);

    if (region.answers?.length) {
      const correctAnswer = region.answers.find((answer) => answer.isCorrect);
      if (correctAnswer) {
        this._selectedChoice.set(correctAnswer);
        this._correctChoice.set(correctAnswer);
      } else {
        this._selectedChoice.set(undefined);
        this._correctChoice.set(undefined);
      }
    } else {
      this._selectedChoice.set(undefined);
      this._correctChoice.set(undefined);
    }

    const transformedQuestionUrl = this.transformQuestionImageUrl(region.imageUrl);
    this.questionImageSource.set(transformedQuestionUrl);
    this.passageImageSource.set(region.passage?.imageUrl ?? null);

    void this.loadImageAssets(region);
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

  public hasPassageImage(): boolean {
    const passageUrl = this.passageImageSource();
    return typeof passageUrl === 'string' && passageUrl.trim().length > 0;
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

  private async loadImageAssets(region: QuestionRegion): Promise<void> {
    const questionImageUrl = this.transformQuestionImageUrl(region?.imageUrl);
    if (!questionImageUrl) {
      this.isImageLoaded.set(false);
      return;
    }

    const cacheKey = region.imageId ? `${region.imageId}-v2` : questionImageUrl;

    try {
      const image = await this.fetchImage(cacheKey, questionImageUrl);
      const currentRegion = this._questionRegion();
      if (currentRegion.imageId && region.imageId && currentRegion.imageId !== region.imageId) {
        return;
      }

      this.imageNaturalWidth = image.naturalWidth;
      this.imageNaturalHeight = image.naturalHeight;
      this.questionContentWidth = image.naturalWidth;
      this.questionContentHeight = image.naturalHeight;
      this.baseCanvasWidth = image.naturalWidth;
      this.baseCanvasHeight = image.naturalHeight;
      this.questionImageSource.set(questionImageUrl);
      this.isImageLoaded.set(true);
    } catch {
      this.isImageLoaded.set(false);
      return;
    }

    const passage = region.passage;
    if (passage?.imageUrl && passage.imageId) {
      try {
        const passageImage = await this.fetchImage(passage.imageId, passage.imageUrl);
        if (this._questionRegion().passage?.imageId !== passage.imageId) return;

        this.passageNaturalWidth = passageImage.naturalWidth;
        this.passageNaturalHeight = passageImage.naturalHeight;
        this.passageImageSource.set(passage.imageUrl);
      } catch {
        if (this._questionRegion().passage?.imageId === passage.imageId) {
          this.passageNaturalWidth = 0;
          this.passageNaturalHeight = 0;
          this.passageImageSource.set(null);
        }
      }
    } else {
      this.passageNaturalWidth = 0;
      this.passageNaturalHeight = 0;
      this.passageImageSource.set(null);
    }
  }

  private async fetchImage(id: string, url: string): Promise<HTMLImageElement> {
    const cached = this.imageCache.get(id);
    if (cached && cached.complete && cached.naturalWidth > 0) {
      return cached;
    }

    const image = cached ?? new Image();
    if (!cached || image.src !== url) {
      image.src = url;
    }

    if (image.complete && image.naturalWidth > 0) {
      this.imageCache.set(id, image);
      return image;
    }

    await new Promise<void>((resolve, reject) => {
      const onLoad = () => {
        cleanup();
        resolve();
      };
      const onError = () => {
        cleanup();
        reject(new Error(`Failed to load image ${id}`));
      };
      const cleanup = () => {
        image.removeEventListener('load', onLoad);
        image.removeEventListener('error', onError);
      };

      image.addEventListener('load', onLoad);
      image.addEventListener('error', onError);
    });

    this.imageCache.set(id, image);
    return image;
  }

  public getQuestionWrapperStyle(): Record<string, string> {
    return {
      width: '100%',
      height: '100%',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      overflow: 'auto',
    };
  }

  public getQuestionImageStyle(): Record<string, string> {
    const scaleValue = this.toSizeValue(this.contentScale);
    return {
      height: `calc(${scaleValue} * 100%)`,
      width: 'auto',
      maxWidth: `calc(${scaleValue} * 100%)`,
      objectFit: 'contain',
    };
  }

  public getAnswerWrapperStyle(_answer?: AnswerChoice, answerCount?: number): Record<string, string> {
    // Stack/cols-1 modunda her cevabın height özelliğini kullan
    const region = this._questionRegion();
    const layoutClass = region.layoutPlan?.layoutClass || '';
    if (layoutClass.includes('stack') && layoutClass.includes('cols-1') && _answer) {
      // Cevabın height özelliği varsa onu kullan, yoksa fallback 48px
      const answerHeight = typeof _answer.height === 'number' && _answer.height > 0 ? _answer.height : 48;
      return {
        width: '100%',
        minHeight: `${answerHeight}px`,
        boxSizing: 'border-box',
      };
    }
    return {
      width: '100%',
    };
  }

  /**
   * Returns the scale ratio of the question image: (displayed width / natural width)
   * If natural width is 0, returns 1.
   */
  public get questionImageScaleRatio(): number {
    // Use getCanvasWidths to get the displayed width
    const naturalWidth = this.imageNaturalWidth || this.questionContentWidth || this._questionRegion().width || 1;
    const displayedWidth = this.getCanvasWidths().questionWidth;
    if (!naturalWidth || !displayedWidth) return 1;
    return displayedWidth / naturalWidth;
  }

  public getAnswerImageStyle(_answer?: AnswerChoice): Record<string, string> {
    // Cevap görselleri, answer-panel'in yüksekliğine göre orantılı scale edilmeli
    // Stack+cols-1 modunda, parent answer-panel'in yüksekliği ve toplam cevap yüksekliği ile orantı alınır
    const region = this._questionRegion();
    const layoutClass = region.layoutPlan?.layoutClass || '';
    let scaleRatio = this.questionImageScaleRatio;
    if (layoutClass.includes('stack') && layoutClass.includes('cols-1') && _answer) {
      // Parent answer-panel yüksekliği
      const answersPanel = document.querySelector('.answers-panel');
      let panelHeight = 0;
      if (answersPanel) {
        panelHeight = (answersPanel as HTMLElement).offsetHeight;
      }
      // Toplam cevap yüksekliği (gap dahil)
      const answers = region.answers || [];
      const totalAnswersHeight =
        answers.reduce((sum, a) => sum + (typeof a.height === 'number' && a.height > 0 ? a.height : 48), 0) +
        (answers.length > 1 ? (answers.length - 1) * 12 : 0);
      // Oran
      scaleRatio = panelHeight > 0 && totalAnswersHeight > 0 ? panelHeight / totalAnswersHeight : 1;
    }
    return {
      width: 'auto',
      height: 'auto',
      objectFit: 'contain',
      maxWidth: scaleRatio < 1 ? `${Math.round(scaleRatio * 100)}%` : '100%',
      maxHeight: scaleRatio < 1 ? `${Math.round(scaleRatio * 100)}%` : '100%',
    };
  }

  public getPassageWrapperStyle(): Record<string, string> {
    const passage = this._questionRegion().passage;
    const scale = this.contentScale;
    const width = passage?.width || this.passageNaturalWidth;
    const height = passage?.height || this.passageNaturalHeight;

    return {
      width: this.toSize(Math.max((width || 0) * scale, 1)),
      height: this.toSize(Math.max((height || 0) * scale, 1)),
    };
  }

  public getPassageImageStyle(): Record<string, string> {
    const scale = this.contentScale;
    const width = this.passageNaturalWidth || this._questionRegion().passage?.width || 0;
    const height = this.passageNaturalHeight || this._questionRegion().passage?.height || 0;

    return {
      width: this.toSize((width || 0) * scale),
      height: this.toSize((height || 0) * scale),
      objectFit: 'contain',
    };
  }

  public getAnswerClasses(answer: AnswerChoice): Record<string, boolean> {
    const mode = this._mode();
    const isSelected = this._selectedChoice() === answer;
    const isCorrect = !!answer.isCorrect;
    const isHovered = this.hoveredChoice() === answer;

    return {
      'is-selected': isSelected,
      'is-correct': mode === 'exam' ? isCorrect || isSelected : isCorrect,
      'is-hovered': mode === 'exam' && !isCorrect && !isSelected && isHovered,
      'is-incorrect': mode === 'result' && isSelected && !isCorrect,
    };
  }

  hoverAnswer(answer: AnswerChoice | null) {
    this.hoveredChoice.set(answer);
  }

  selectAnswer(index: number) {
    const region = this._questionRegion();
    const answer = region?.answers?.[index];
    if (!answer) return;

    answer.scale = this.contentScale;
    this._selectedChoice.set(answer);
    this.choiceSelected.emit(answer);
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
  }

  public rescaleQuestion(factor: number) {
    const next = this.contentScale * factor;
    this.contentScale = Math.max(0.2, Math.min(3, next));
  }

  getCanvasHeights(): { questionHeight: number; passageHeight: number; hasPassageImage: boolean } {
    const region = this._questionRegion();
    const questionBaseHeight = this.imageNaturalHeight || this.questionContentHeight || region.height || 0;
    const questionHeight = Math.round(questionBaseHeight * this.contentScale) || 0;

    const passage = region?.passage;
    const hasPassageImage = this.hasPassageImage();
    let passageHeight = 0;

    if (hasPassageImage) {
      const basePassageHeight = passage?.height || this.passageNaturalHeight || 0;
      passageHeight = Math.round(basePassageHeight * this.contentScale) || 0;
    }

    return { questionHeight, passageHeight, hasPassageImage };
  }

  getCanvasWidths(): { questionWidth: number; passageWidth: number; hasPassageImage: boolean } {
    const region = this._questionRegion();
    const baseQuestionWidth =
      this.baseCanvasWidth && this.baseCanvasWidth > 0
        ? this.baseCanvasWidth
        : this.imageNaturalWidth || this.questionContentWidth || region?.width || 0;
    const questionWidth = Math.round(baseQuestionWidth * this.contentScale) || 0;

    const passage = region?.passage;
    const hasPassageImage = this.hasPassageImage();
    let passageWidth = 0;

    if (hasPassageImage) {
      const basePassageWidth = passage?.width || this.passageNaturalWidth || 0;
      passageWidth = Math.round(basePassageWidth * this.contentScale) || 0;
    }

    return { questionWidth, passageWidth, hasPassageImage };
  }

  /**
   * Ana container'ın flex yönünü backend'den gelen layoutClass'a göre ayarlar.
   * stack => column, inline => row
   */
  public getContainerStyle(): Record<string, string> {
    const layoutClass = this._questionRegion().layoutPlan?.layoutClass || '';
    if (layoutClass.includes('-stack-')) {
      if (layoutClass.includes('cols-1')) {
        return {
          display: 'flex',
          flexDirection: 'column',
          width: '100%',
          height: 'auto',
          maxHeight: '100%',
          minHeight: '0',
        };
      }
      return { display: 'flex', flexDirection: 'column', width: '100%', height: '100%' };
    }
    // inline veya default
    return { display: 'flex', flexDirection: 'row', width: '100%', height: '100%' };
  }

  /**
   * Backend'den gelen flex oranlarını uygular. Inline layout'ta backend planı varsa kullanılır.
   * @param panel 'question' | 'answers'
   */
  public getPanelFlex(panel: 'question' | 'answers'): string {
    const plan = this._questionRegion().layoutPlan;
    const answerCount = this._questionRegion().answers?.length ?? 0;
    const layoutClass = plan?.layoutClass || '';
    // Stack + cols-1 modunda cevap paneli ve soru paneli için dinamik flex oranı
    if (layoutClass.includes('stack') && layoutClass.includes('cols-1')) {
      // Backend'den flex oranı geliyorsa onu kullan
      if (typeof plan?.questionFlex === 'number' && typeof plan?.answersFlex === 'number') {
        if (panel === 'question') return `${plan.questionFlex} ${plan.questionFlex} 0`;
        if (panel === 'answers') return `${plan.answersFlex} ${plan.answersFlex} 0`;
      }
      // Dinamik oran: soru resminin gerçek yüksekliği ve cevapların toplam yüksekliği
      // Cevapların height özelliği varsa onları topla, yoksa 48px fallback kullan
      const answers = this._questionRegion().answers || [];
      const answersTotalHeight =
        answers.reduce((sum, a) => sum + (typeof a.height === 'number' && a.height > 0 ? a.height : 48), 0) +
        (answers.length > 1 ? (answers.length - 1) * 12 : 0); // 12px gap
      // Soru resmi yüksekliği (imageNaturalHeight) + padding
      const questionImageHeight = this._questionRegion().height; // this.imageNaturalHeight || 200; // fallback 200px
      // Toplam yükseklik
      const totalHeight = questionImageHeight + answersTotalHeight;
      // Flex oranları
      const questionFlex = Math.max(1, Math.round((questionImageHeight / totalHeight) * 10));
      const answersFlex = Math.max(1, Math.round((answersTotalHeight / totalHeight) * 10));
      if (panel === 'question') return `${questionFlex} ${questionFlex} 0`;
      if (panel === 'answers') return `${answersFlex} ${answersFlex} 0`;
    }
    // Diğer modlarda backend'den gelen flex oranı varsa kullanılır
    if (plan) {
      if (panel === 'question' && plan.questionFlex) {
        return `${plan.questionFlex} ${plan.questionFlex} 0`;
      }
      if (panel === 'answers' && plan.answersFlex) {
        return `${plan.answersFlex} ${plan.answersFlex} 0`;
      }
    }
    // Fallback: eski oranlar
    if (panel === 'question') return '3 3 0';
    if (panel === 'answers') return '2 2 0';
    return '';
  }

  private toSize(value: number): string {
    return `${this.toSizeValue(value)}px`;
  }

  private transformQuestionImageUrl(url?: string | null): string | null {
    if (!url) {
      return null;
    }

    return url.replace(/question(\.[^/?#]+)?$/i, (_match, ext) => `question-v2${ext ?? ''}`);
  }

  private toSizeValue(value: number): string {
    if (!Number.isFinite(value)) {
      return '0';
    }

    const normalized = value
      .toFixed(3)
      .replace(/\.0+$/, '')
      .replace(/(\.\d*?)0+$/, '$1');

    return normalized === '-0' ? '0' : normalized;
  }
}
