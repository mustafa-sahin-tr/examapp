import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { SafeHtmlPipe } from '../../../services/safehtml';
import { AnswerChoice } from '../../../models/draws';
import { QuestionCanvasViewComponentv2 } from '../question-canvas-view-v2/question-canvas-view-v2.component';

@Component({
  selector: 'app-question-canvas-view-v3',
  standalone: true,
  templateUrl: './question-canvas-view-v3.component.html',
  styleUrls: ['./question-canvas-view-v3.component.scss'],
  imports: [CommonModule, MatButtonModule, MatIconModule, SafeHtmlPipe],
})
export class QuestionCanvasViewComponentv3 extends QuestionCanvasViewComponentv2 {
  // Canvas v1 API'si ile uyum için boş bir yeniden çizim metodu bırakıyoruz.
  public drawImageSection(): void {
    // Görseller doğrudan <img> etiketleriyle render edildiği için manuel redraw gerekli değil.
  }

  public override getQuestionWrapperStyle(): Record<string, string> {
    const widths = this.getCanvasWidths();
    const heights = this.getCanvasHeights();
    const region = this._questionRegion();
    const questionWidth = widths.questionWidth || region?.width || 0;
    const questionHeight = heights.questionHeight || region?.height || 0;

    return {
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      width: this.formatSize(questionWidth),
      height: this.formatSize(questionHeight),
      maxWidth: '100%',
      overflow: 'hidden',
      margin: '0 auto',
    };
  }

  public override getQuestionImageStyle(): Record<string, string> {
    const widths = this.getCanvasWidths();
    const heights = this.getCanvasHeights();
    const region = this._questionRegion();
    const questionWidth = widths.questionWidth || region?.width || 0;
    const questionHeight = heights.questionHeight || region?.height || 0;

    return {
      width: this.formatSize(questionWidth),
      height: this.formatSize(questionHeight),
      objectFit: 'contain',
      maxWidth: '100%',
      maxHeight: '100%',
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

  public getAnswerGridColumn(answer: AnswerChoice): string | undefined {
    const questionWidth = this.getScaledQuestionWidth();
    const answerWidth = this.getScaledAnswerWidth(answer);

    if (!questionWidth || !answerWidth) return undefined;

    const ratio = answerWidth / questionWidth;

    if (ratio >= 0.75) return '1 / -1';
    if (ratio >= 0.55) return 'span 2';

    return undefined;
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
}
