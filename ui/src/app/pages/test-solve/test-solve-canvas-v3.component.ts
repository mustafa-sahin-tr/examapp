import { Component } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TestSolveCanvasComponentv2 } from './test-solve-canvas-enhanced.component';
import { TestService } from '../../services/test.service';
import { MatDialog } from '@angular/material/dialog';
import { QuestionLiteViewComponent } from '../question-lite-view/question-lite-view.component';
import { CountdownComponent } from '../../shared/components/countdown/countdown.component';
import { QuestionCanvasViewComponentv3 } from '../../shared/components/question-canvas-view-v3/question-canvas-view-v3.component';
import { QuestionCanvasViewComponentv2 } from '../../shared/components/question-canvas-view-v2/question-canvas-view-v2.component';
import { QuestionCanvasViewComponentv4 } from '../../shared/components/question-canvas-view-v4/question-canvas-view-v4.component';
import { QuestionCanvasViewComponent } from '../../shared/components/question-canvas-view/question-canvas-view.component';
import { QuestionCanvasViewComponentv5 } from '../../shared/components/question-canvas-view-v5/question-canvas-view-v5.component';
import { QuestionCanvasDragDropLabelingComponent } from '../../shared/components/question-canvas-dragdrop-labeling/question-canvas-dragdrop-labeling.component';

@Component({
  selector: 'app-test-solve-v3',
  standalone: true,
  templateUrl: 'test-solve-canvas-v3.component.html',
  styleUrls: ['test-solve-canvas-v3.component.scss'],
  imports: [
    CommonModule,
    MatButtonModule,
    MatIconModule,
    MatTooltipModule,
    QuestionLiteViewComponent,
    CountdownComponent,
    MatProgressSpinnerModule,
    QuestionCanvasViewComponentv5,
    QuestionCanvasDragDropLabelingComponent,
  ],
})
export class TestSolveCanvasComponentv3 extends TestSolveCanvasComponentv2 {
  private passageOnlyByQuestionIndex = new Map<number, boolean>();

  constructor(route: ActivatedRoute, testService: TestService, router: Router, dialog: MatDialog) {
    super(route, testService, router, dialog);
  }

  public override async loadTest(testId: number) {
    await super.loadTest(testId);
    this.initializePassageFirstState();
  }

  private initializePassageFirstState() {
    this.passageOnlyByQuestionIndex.clear();

    const questions = this.testInstance?.testInstanceQuestions ?? [];
    questions.forEach((q, index) => {
      const hasPassage = !!q.question?.passage;
      const showPassageFirst = !!q.question?.showPassageFirst;
      this.passageOnlyByQuestionIndex.set(index, hasPassage && showPassageFirst);
    });
  }

  public isPassageFirstActive(questionIndex: number): boolean {
    const q = this.testInstance?.testInstanceQuestions?.[questionIndex]?.question;
    return !!q?.showPassageFirst && !!q?.passage;
  }

  public isShowingPassageOnly(questionIndex: number): boolean {
    if (!this.isPassageFirstActive(questionIndex)) return false;
    return this.passageOnlyByQuestionIndex.get(questionIndex) ?? false;
  }

  public showQuestion(questionIndex: number) {
    this.passageOnlyByQuestionIndex.set(questionIndex, false);
  }

  public showPassageOnly(questionIndex: number) {
    if (!this.isPassageFirstActive(questionIndex)) return;
    this.passageOnlyByQuestionIndex.set(questionIndex, true);
  }

  // Explicitly re-expose on v3 for Angular template type-checking.
  public override saveDragDropAnswerForQuestion(answerPayloadJson: string, questionIndex: number) {
    super.saveDragDropAnswerForQuestion(answerPayloadJson, questionIndex);
  }

  public sideDockActive(): boolean {
    return this.questionsPerView() === 1 && this.questionDockPosition() === 'side';
  }
}
