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
    const base = super.getQuestionWrapperStyle();
    return {
      ...base,
      maxWidth: 'min(var(--canvas-question-width, 960px), 100%)',
    };
  }

  public override getQuestionImageStyle(): Record<string, string> {
    const base = super.getQuestionImageStyle();
    return {
      ...base,
      maxHeight: 'clamp(360px, 72vh, var(--canvas-question-height, 820px))',
    };
  }

  public override getAnswerWrapperStyle(_answer?: AnswerChoice): Record<string, string> {
    const base = super.getAnswerWrapperStyle(_answer);
    return {
      ...base,
      maxWidth: 'min(var(--canvas-question-width, 780px), 100%)',
      margin: '0 auto',
    };
  }

  public override getAnswerImageStyle(_answer?: AnswerChoice): Record<string, string> {
    const base = super.getAnswerImageStyle(_answer);
    return {
      ...base,
      maxHeight: 'clamp(120px, 42vh, var(--canvas-question-height, 780px))',
    };
  }
}
