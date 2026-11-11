import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

import { StudyPlan, StudyPlanTask, studyPlanTaskLink } from '../../models/study-plan.model';
import { StudyPlanService } from '../../services/study-plan.service';

/**
 * Issue #1113: öğrencinin bu haftaki görevleri (Pro). Görevler Pazartesi kurulur; öğrenci yaptığını işaretler, her görev ilgili sayfayı
 * açar. Rota `entitlementGuard('weekly-plan')` ile korunur.
 */
@Component({
  selector: 'app-study-plan',
  standalone: true,
  imports: [
    DatePipe,
    MatButtonModule,
    MatCheckboxModule,
    MatIconModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    RouterLink,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope('study-plan')],
  templateUrl: './study-plan.component.html',
  styleUrl: './study-plan.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StudyPlanComponent implements OnInit {
  private readonly service = inject(StudyPlanService);
  private readonly destroyRef = inject(DestroyRef);

  readonly plan = signal<StudyPlan | null>(null);
  readonly loading = signal(true);
  readonly failed = signal(false);
  readonly saving = signal<number | null>(null);
  readonly percent = computed(() => {
    const plan = this.plan();
    return plan && plan.totalCount > 0 ? Math.round((plan.doneCount / plan.totalCount) * 100) : 0;
  });

  readonly link = studyPlanTaskLink;

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.service
      .current()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (plan) => {
          this.plan.set(plan);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.failed.set(true);
        },
      });
  }

  toggle(task: StudyPlanTask, done: boolean): void {
    if (this.saving() !== null) return;
    this.saving.set(task.id);
    this.mark(task.id, done);
    this.service
      .setDone(task.id, done)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (plan) => {
          this.plan.set(plan);
          this.saving.set(null);
        },
        // İşaret geri alınır.
        error: () => {
          this.saving.set(null);
          this.mark(task.id, !done);
        },
      });
  }

  private mark(taskId: number, done: boolean): void {
    this.plan.update((p) => (p ? { ...p, tasks: p.tasks.map((t) => (t.id === taskId ? { ...t, done } : t)) } : p));
  }
}
