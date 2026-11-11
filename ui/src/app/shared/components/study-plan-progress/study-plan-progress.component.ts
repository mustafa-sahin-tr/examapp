import { ChangeDetectionStrategy, Component, DestroyRef, OnChanges, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

import { StudyPlan } from '../../../models/study-plan.model';
import { StudyPlanService } from '../../../services/study-plan.service';

/**
 * Issue #1113: veli panelinde çocuğun bu haftaki görevleri (salt okunur). Yüklenemezse (planda yok, bağlantı yok) hiçbir şey çizilmez;
 * çağıran yalnız çocuğun planında `weekly-plan` varken kullanır.
 */
@Component({
  selector: 'app-study-plan-progress',
  standalone: true,
  imports: [MatIconModule, MatProgressBarModule, TranslocoDirective],
  providers: [provideTranslocoScope('study-plan')],
  templateUrl: './study-plan-progress.component.html',
  styleUrl: './study-plan-progress.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StudyPlanProgressComponent implements OnChanges {
  private readonly service = inject(StudyPlanService);
  private readonly destroyRef = inject(DestroyRef);

  readonly childId = input.required<number>();
  readonly plan = signal<StudyPlan | null>(null);

  ngOnChanges(): void {
    this.plan.set(null);
    this.service
      .child(this.childId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (plan) => this.plan.set(plan), error: () => this.plan.set(null) });
  }

  percent(plan: StudyPlan): number {
    return plan.totalCount > 0 ? Math.round((plan.doneCount / plan.totalCount) * 100) : 0;
  }
}
