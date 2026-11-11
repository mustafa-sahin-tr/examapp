import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

import { PlacementReport } from '../../../models/placement.model';

/**
 * Issue #1114: seviye tespiti raporu — puan, ders kırılımı, güçlü / zayıf konular ve plan önerisi. Öğrenci sayfası ve veli paneli
 * birlikte kullanır; `audience='student'` planlara bağlantı ve "planı velin seçer" notunu gösterir.
 */
@Component({
  selector: 'app-placement-report',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatProgressBarModule, RouterLink, TranslocoDirective],
  providers: [provideTranslocoScope('placement')],
  templateUrl: './placement-report.component.html',
  styleUrl: './placement-report.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlacementReportComponent {
  readonly report = input.required<PlacementReport>();
  readonly audience = input<'student' | 'parent'>('student');
  readonly heading = input<string | null>(null);
}
