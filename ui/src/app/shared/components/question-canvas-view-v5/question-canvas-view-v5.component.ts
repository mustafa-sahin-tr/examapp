import {
  Component,
  Input,
  Output,
  EventEmitter,
  signal,
  ElementRef,
  ViewChild,
  AfterViewInit,
  OnDestroy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { AnswerChoice, QuestionRegion } from '../../../models/draws';
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

type LayoutType = 'side-1col' | 'side-2col' | 'top-1row' | 'top-2row' | 'top-4row';

@Component({
  selector: 'app-question-canvas-view-v5',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule],
  templateUrl: './question-canvas-view-v5.component.html',
  styleUrls: ['./question-canvas-view-v5.component.scss'],
})
export class QuestionCanvasViewComponentv5 {
  @ViewChild('questionImage') private questionImageRef?: ElementRef<HTMLImageElement>;
  @ViewChild('questionImageBox') private questionImageBoxRef?: ElementRef<HTMLDivElement>;

  private resizeObserver?: ResizeObserver;

  public contentScale = 1;
  public visualScale = signal<number>(1);

  public questionImageSource = signal<string | null>(null);
  public passageImageSource = signal<string | null>(null);
  public _questionRegion = signal<QuestionRegion>(EMPTY_REGION);
  currentLayout = signal<LayoutType>('top-4row');
  currentMarginLeft = signal<number>(0);
  answerMarginLeft = signal<number>(0);
  answerMinWidth = signal<number>(0);

  // Sıralı cevaplar getter'ı: order > tag > id
  public get sortedAnswers(): AnswerChoice[] {
    const answers = this._questionRegion().answers || [];
    return [...answers].sort((a, b) => {
      // Öncelik: order, sonra label, sonra id
      if (a.order != null && b.order != null && a.order !== b.order) return a.order - b.order;
      if (a.label && b.label && a.label !== b.label) return a.label.localeCompare(b.label);
      return (a.id ?? 0) - (b.id ?? 0);
    });
  }
  @Input({ required: true }) set questionRegion(value: QuestionRegion) {
    const region = value ?? EMPTY_REGION;
    this.currentLayout = signal<LayoutType>('top-4row');
    this.currentMarginLeft = signal<number>(0);
    this.answerMarginLeft = signal<number>(0);
    this.answerMinWidth = signal<number>(0);
    // Veri elimizde olduğu için direkt hesaplıyoruz
    const bestLayout = this.calculateBestLayout(region);
    console.log('Calculated best layout:', bestLayout, 'for question region:', region);
    this.currentLayout.set(bestLayout);

    // 2. Cevapları ID'ye göre sırala
    if (region.answers) {
      region.answers = [...region.answers].sort((a, b) => a.label.localeCompare(b.label) || a.id - b.id);
    }

    this._questionRegion.set(region);
    console.log('Input Question Region:', region);
    const transformedQuestionUrl = this.transformQuestionImageUrl(region.imageUrl);
    this.questionImageSource.set(transformedQuestionUrl);
    const passageUrl = region?.passage?.imageUrl ?? null;
    this.passageImageSource.set(passageUrl && passageUrl.trim().length > 0 ? passageUrl : null);
    queueMicrotask(() => this.updateVisualScale());
  }
  @Input() correctAnswerVisible: boolean = false;
  @Input() isPreviewMode: boolean = false;
  @Input() hidePassage: boolean = false;
  @Input() set selectedChoice(choice: AnswerChoice | undefined) {
    this._selectedChoice.set(choice);
  }
  @Input() set correctChoice(choice: AnswerChoice | undefined) {
    this._correctChoice.set(choice);
  }
  @Input() set mode(m: 'exam' | 'result' | null) {
    this._mode.set(m || 'exam');
  }

  ngAfterViewInit(): void {
    const imageBox = this.questionImageBoxRef?.nativeElement;
    if (!imageBox) {
      return;
    }

    this.resizeObserver = new ResizeObserver(() => this.updateVisualScale());
    this.resizeObserver.observe(imageBox);
    this.updateVisualScale();
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
  }

  private calculateBestLayout(region: QuestionRegion): LayoutType {
    const { width: qW, sanitizedHeight, height: qH, answers } = region;
    const effectiveHeight = sanitizedHeight || qH;
    const qRatio = qW / effectiveHeight;

    const maxAns = answers?.reduce((max, ans) => (ans.width > (max?.width || 0) ? ans : max), answers?.[0]);
    const aW = 20 + (maxAns?.width || 0); // Cevap kartlarının minimum genişliği (padding(8) + border(2) + görsel)
    this.answerMinWidth.set(aW);
    const gap = 12; // CSS'teki gap değeriyle uyumlu olmalı

    // 1. Durum: Soru Dikey veya Kareyse (Yan yana yerleşim)
    if (qRatio < 1.1) {
      return effectiveHeight > 800 || qRatio < 0.6 ? 'side-1col' : 'side-2col';
    }

    // 2. Durum: Soru Yatay ise (Genişlik bazlı hiyerarşik kontrol)
    this.currentMarginLeft.set(0);
    // 4 şık yan yana sığar mı? (Soru genişliğinin %95'ini baz alalım)
    const totalWidth4 = aW * 4 + gap * 3;
    if (totalWidth4 < qW * 1.1) {
      if (totalWidth4 > qW) {
        this.currentMarginLeft.set(Math.max(0, (totalWidth4 - qW) / 2));
      } else {
        this.currentMarginLeft.set(0);
        this.answerMarginLeft.set(Math.max(0, (qW - totalWidth4) / 2));
      }
      return 'top-1row'; // 4 tane yan yana (Senin örneğin tam buraya düşer)
    }

    // 2 şık yan yana sığar mı?
    const totalWidth2 = aW * 2 + gap;
    if (totalWidth2 < qW * 1.1) {
      if (totalWidth2 > qW) {
        this.currentMarginLeft.set(Math.max(0, (totalWidth2 - qW) / 2));
      } else {
        this.currentMarginLeft.set(0);
        this.answerMarginLeft.set(Math.max(0, (qW - totalWidth2) / 2));
      }
      return 'top-2row'; // 2 satır 2 sütun (2x2)
    }

    // Sığmıyorsa alt alta
    return 'top-4row';
  }

  private calculateBestLayoutv1(region: QuestionRegion): LayoutType {
    const { width: qW, height: qH, answers } = region;
    const qRatio = qW / qH;

    const firstAns = answers?.[0];
    const aW = firstAns?.width || 0;
    const gap = 12; // CSS'teki gap değeriyle uyumlu olmalı

    // 1. Durum: Soru Dikey veya Kareyse (Yan yana yerleşim)
    if (qRatio < 1.1) {
      return qH > 800 || qRatio < 0.6 ? 'side-1col' : 'side-2col';
    }

    // 2. Durum: Soru Yatay ise (Genişlik bazlı hiyerarşik kontrol)

    // 4 şık yan yana sığar mı? (Soru genişliğinin %95'ini baz alalım)
    const totalWidth4 = aW * 4 + gap * 3;
    if (totalWidth4 < qW * 0.95) {
      return 'top-1row'; // 4 tane yan yana (Senin örneğin tam buraya düşer)
    }

    // 2 şık yan yana sığar mı?
    const totalWidth2 = aW * 2 + gap;
    if (totalWidth2 < qW * 0.95) {
      return 'top-2row'; // 2 satır 2 sütun (2x2)
    }

    // Sığmıyorsa alt alta
    return 'top-4row';
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
    const answer = this.sortedAnswers?.[index];
    if (!answer) return;
    this._selectedChoice.set(answer);
    this.choiceSelected.emit(answer);
  }

  showCorrectAnswer() {
    this.answerOpened.emit(1);
  }

  removeQuestion() {
    this.questionRemove.emit(this._questionRegion().id);
  }

  private transformQuestionImageUrl(url?: string | null): string | null {
    if (!url) {
      return null;
    }

    return url.replace(/question(\.[^/?#]+)?$/i, (_match, ext) => `question-v2${ext ?? ''}`);
  }

  public updateVisualScale(): void {
    const questionWidth = this._questionRegion().width || 0;
    if (!questionWidth) {
      this.visualScale.set(1);
      return;
    }

    const renderedWidth = this.questionImageRef?.nativeElement.getBoundingClientRect().width || 0;
    if (!renderedWidth) {
      return;
    }

    const ratio = Math.max(0.2, Math.min(1, renderedWidth / questionWidth));
    this.visualScale.set(ratio);
  }

  public getScaledAnswerWidth(answer: AnswerChoice): number | null {
    const answerWidth = answer.width || 0;
    if (!answerWidth) {
      return null;
    }

    return Math.round(answerWidth * this.visualScale());
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
}
