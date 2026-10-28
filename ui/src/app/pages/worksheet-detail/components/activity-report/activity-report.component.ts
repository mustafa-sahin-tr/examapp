import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoDirective } from '@jsverse/transloco';

import { ActivityReport, ActivityReportCell, ActivityReportItem, ActivityReportQuestion } from '../../../../models/activity-report';
import { TestService } from '../../../../services/test.service';

type LoadState = 'idle' | 'loading' | 'loaded' | 'error';

/** Isı haritası hücresinin tonu: oran ≥ %80 iyi, ≥ %50 orta, altı zayıf; boş / puanlanamaz ayrı. */
export type HeatTone = 'good' | 'mid' | 'low' | 'empty' | 'none';

const KNOWN_TYPES = new Set([
  'matrixChoice',
  'slotFill',
  'dragPlace',
  'match',
  'textSelect',
  'dragDropLabeling',
  'openText',
  'mazeTree',
  'crossword',
  'wordSearch',
  'logicGrid',
  'regionPaint',
]);

/**
 * Issue #584: öğretmenin atanmış etkinlik sayfası analizi — öğrenci × madde ısı haritası ve madde başına satır/öğe doğru oranı
 * ile en sık yanlış cevap ("öğrencilerin %40'ı kemiği kaslara koydu"). Rapor istenince yüklenir ("Analizi göster"): takip tablosu
 * (#573) yüzde göstermez, analiz ayrı ve isteğe bağlıdır. Scope sözlüğü üst sayfa (`worksheet-detail`) tarafından sağlanır.
 */
@Component({
  selector: 'app-activity-report',
  standalone: true,
  imports: [MatButtonModule, MatExpansionModule, MatIconModule, MatProgressSpinnerModule, MatTooltipModule, TranslocoDirective],
  templateUrl: './activity-report.component.html',
  styleUrl: './activity-report.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActivityReportComponent {
  private readonly testService = inject(TestService);
  private readonly destroyRef = inject(DestroyRef);

  readonly worksheetId = input.required<number>();

  readonly state = signal<LoadState>('idle');
  readonly report = signal<ActivityReport | null>(null);

  load(): void {
    this.state.set('loading');
    this.testService
      .getActivityReport(this.worksheetId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (report) => {
          this.report.set(report);
          this.state.set('loaded');
        },
        error: () => {
          this.report.set(null);
          this.state.set('error');
        },
      });
  }

  tone(cell: ActivityReportCell, question: ActivityReportQuestion): HeatTone {
    if (!question.gradable || cell.totalCount === 0) return 'none';
    if (!cell.answered) return 'empty';
    const ratio = cell.correctCount / cell.totalCount;
    return ratio >= 0.8 ? 'good' : ratio >= 0.5 ? 'mid' : 'low';
  }

  percent(count: number, total: number): number {
    return total > 0 ? Math.round((count * 100) / total) : 0;
  }

  /** Öğenin doğru oranı (%). */
  itemPercent(item: ActivityReportItem): number {
    return this.percent(item.correctCount, item.studentCount);
  }

  itemTone(item: ActivityReportItem): HeatTone {
    const ratio = item.studentCount > 0 ? item.correctCount / item.studentCount : 0;
    return ratio >= 0.8 ? 'good' : ratio >= 0.5 ? 'mid' : 'low';
  }

  /** Çeviri anahtarı soneki: bilinen etkileşim tipi ya da `mcq`. */
  typeKey(question: ActivityReportQuestion): string {
    const type = question.interactionType ?? '';
    return KNOWN_TYPES.has(type) ? type : 'mcq';
  }
}
