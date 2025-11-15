import { Component } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TestSolveCanvasComponentv2 } from './test-solve-canvas-enhanced.component';
import { TestService } from '../../services/test.service';
import { MatDialog } from '@angular/material/dialog';
import { QuestionLiteViewComponent } from '../question-lite-view/question-lite-view.component';
import { CountdownComponent } from '../../shared/components/countdown/countdown.component';
import { QuestionCanvasViewComponentv3 } from '../../shared/components/question-canvas-view-v3/question-canvas-view-v3.component';

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
    QuestionCanvasViewComponentv3,
  ],
})
export class TestSolveCanvasComponentv3 extends TestSolveCanvasComponentv2 {
  constructor(route: ActivatedRoute, testService: TestService, router: Router, dialog: MatDialog) {
    super(route, testService, router, dialog);
  }
}
