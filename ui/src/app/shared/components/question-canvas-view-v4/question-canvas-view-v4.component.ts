import { Component, Input, Output, EventEmitter, signal } from '@angular/core';
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
  selector: 'app-question-canvas-view-v4',
  standalone: true,
  imports: [CommonModule, SafeHtmlPipe, MatButtonModule, MatIconModule],
  templateUrl: './question-canvas-view-v4.component.html',
  styleUrls: ['./question-canvas-view-v4.component.scss'],
})
export class QuestionCanvasViewComponentv4 {
  public get layoutClass(): string {
    return this._questionRegion().layoutPlan?.layoutClass || '';
  }
  public questionImageSource = signal<string | null>(null);
  public _questionRegion = signal<QuestionRegion>(EMPTY_REGION);

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
    this._questionRegion.set(region);
    console.log('Input Question Region:', region);
    const transformedQuestionUrl = this.transformQuestionImageUrl(region.imageUrl);
    console.log('Transformed Question URL:', transformedQuestionUrl);
    this.questionImageSource.set(transformedQuestionUrl);
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

  public get answerColumns(): number {
    return this._questionRegion().layoutPlan?.answerColumns || 1;
  }

  public isInlineLayout(): boolean {
    return this._questionRegion().layoutPlan?.layoutClass?.includes('inline') || false;
  }

  public getPanelFlex(panel: 'question' | 'answers'): string {
    if (!this._questionRegion()) {
      return panel === 'question' ? '7 7 0' : '2 2 0';
    }
    const plan = this._questionRegion().layoutPlan;
    if (plan && typeof plan.questionFlex === 'number' && typeof plan.answersFlex === 'number') {
      if (panel === 'question') return `${plan.questionFlex} ${plan.questionFlex} 0`;
      if (panel === 'answers') return `${plan.answersFlex} ${plan.answersFlex} 0`;
    }
    return panel === 'question' ? '7 7 0' : '2 2 0';
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
}
