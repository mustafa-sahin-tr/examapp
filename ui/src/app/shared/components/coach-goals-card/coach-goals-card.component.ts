import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { EMPTY, Observable, Subject, catchError, defer, finalize, switchMap, tap } from 'rxjs';

import { ParentCoachSummary, StudentCoachGoal } from '../../../models/coach.model';
import { CoachService } from '../../../services/coach.service';

/**
 * Issue #700: koçların bu haftaki hedefleri. `childId` yoksa öğrencinin kendisi: hedef yoksa ya da yüklenemezse kart
 * çizilmez (ana sayfa isteğe bağlı alanı). `childId` varsa velinin seçili çocuğu: "Planım" sayıları ve hedefler, öğretmen
 * kimliği ve notlar yok; 404'te kart gizlenir. Issue #893: öğrenci hedefi "yaptım" diye işaretler, veli yalnız görür.
 */
@Component({
  selector: 'app-coach-goals-card',
  standalone: true,
  imports: [MatButtonModule, MatCardModule, MatCheckboxModule, MatIconModule, MatProgressSpinnerModule, TranslocoDirective],
  providers: [provideTranslocoScope('coach')],
  templateUrl: './coach-goals-card.component.html',
  styleUrl: './coach-goals-card.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class CoachGoalsCardComponent {
  readonly childId = input<number | null>(null);

  private readonly service = inject(CoachService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly myGoals = signal<StudentCoachGoal[]>([]);
  protected readonly summary = signal<ParentCoachSummary | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal(false);
  protected readonly hidden = signal(false);
  /** İşareti kaydedilmekte olan hedefler. */
  protected readonly saving = signal<ReadonlySet<number>>(new Set());
  protected readonly markFailed = signal(false);

  protected readonly audience = computed(() => (this.childId() == null ? 'self' : 'child'));

  /** Öğrenci kartı yalnız hedef varken görünür; veli kartı yüklenirken ve hedef yokken de görünür. */
  protected readonly visible = computed(() =>
    this.audience() === 'self' ? this.myGoals().length > 0 : !this.hidden(),
  );

  private readonly requests = new Subject<number | null>();

  constructor() {
    this.requests
      .pipe(
        switchMap((childId) => this.fetch(childId)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();

    effect(() => {
      const childId = this.childId();
      untracked(() => this.requests.next(childId));
    });
  }

  protected reload(): void {
    this.requests.next(this.childId());
  }

  /** İyimser: kutu hemen değişir, hata gelirse geri alınır. */
  protected toggleDone(goal: StudentCoachGoal, done: boolean): void {
    if (this.saving().has(goal.id)) return;
    this.markFailed.set(false);
    this.setDone(goal.id, done);
    this.saving.update((ids) => new Set(ids).add(goal.id));
    this.service
      .setMyGoalDone(goal.id, done)
      .pipe(
        finalize(() =>
          this.saving.update((ids) => {
            const next = new Set(ids);
            next.delete(goal.id);
            return next;
          }),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (saved) => this.setDone(saved.id, saved.done),
        error: () => {
          this.setDone(goal.id, !done);
          this.markFailed.set(true);
        },
      });
  }

  private setDone(id: number, done: boolean): void {
    this.myGoals.update((goals) => goals.map((g) => (g.id === id ? { ...g, done } : g)));
  }

  private fetch(childId: number | null): Observable<unknown> {
    return defer(() => {
      this.loading.set(true);
      this.error.set(false);
      this.hidden.set(false);
      this.myGoals.set([]);
      this.summary.set(null);
      const request: Observable<unknown> =
        childId == null
          ? this.service.getMyGoals().pipe(tap((goals) => this.myGoals.set(goals)))
          : this.service.getChildSummary(childId).pipe(tap((summary) => this.summary.set(summary)));
      return request.pipe(
        catchError((err: HttpErrorResponse) => {
          if (err?.status === 404) this.hidden.set(true);
          else this.error.set(true);
          return EMPTY;
        }),
        finalize(() => this.loading.set(false)),
      );
    });
  }
}
