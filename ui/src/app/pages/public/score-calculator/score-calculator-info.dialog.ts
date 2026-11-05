import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';

import { PUBLIC_PAGES_SCOPE } from '../public-page-meta';
import { ExamScoreExam } from './exam-score';

/** #694: hesabın adımları, kullanılan değerler ve kaynaklar. Değerler sayfanın kullandığı JSON'dan gelir. */
@Component({
  selector: 'app-score-calculator-info-dialog',
  standalone: true,
  imports: [MatButtonModule, MatDialogModule, TranslocoDirective],
  providers: [provideTranslocoScope(PUBLIC_PAGES_SCOPE)],
  templateUrl: './score-calculator-info.dialog.html',
  styleUrl: './score-calculator-info.dialog.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ScoreCalculatorInfoDialogComponent {
  private readonly transloco = inject(TranslocoService);
  readonly exam = inject<ExamScoreExam>(MAT_DIALOG_DATA);

  decimal(value: number): string {
    return value.toLocaleString(this.transloco.getActiveLang());
  }

  /** YKS ağırlıkları oran (0,33 → %33), LGS ağırlıkları katsayıdır (4 → ×4). */
  weightLabel(weight: number): string {
    return weight < 1 ? `%${Math.round(weight * 100)}` : `×${weight}`;
  }

  weightsOf(weights: Record<string, number>): [string, number][] {
    return Object.entries(weights);
  }
}
