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
    return {
      ...base,
      width: this.formatSize(width),
      maxWidth: '100%',
      height: this.formatSize(height),
      margin: '0',
      alignSelf: 'flex-start',
      overflow: 'hidden',
    };
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

    return {
      width: this.formatSize(width),
      height: this.formatSize(height),
      maxWidth: '100%',
      maxHeight: '100%',
      objectFit: 'contain',
    };
  }

  private formatSize(value: number): string {
    const safe = Number.isFinite(value) && value > 0 ? value : 0;
    if (!safe) {
      return 'auto';
    }

    return `${Math.max(Math.round(safe), 1)}px`;
  }
}
