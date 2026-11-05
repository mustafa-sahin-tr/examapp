import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { EMPTY, Subject, catchError, debounceTime, finalize, of, switchMap, tap } from 'rxjs';

import { TargetExam, TargetGoal, TargetProgram } from '../../../models/target-goal.model';
import { computeTargetGap } from '../../../pages/public/score-calculator/target-gap';
import { LocaleService } from '../../../services/locale.service';
import { TargetGoalService } from '../../../services/target-goal.service';

const SEARCH_DEBOUNCE_MS = 300;
const SEARCH_TAKE = 20;

/**
 * Issue #695: hedef okul ve tabana kalan fark. Girdi yoksa öğrencinin kendisi (seçip değiştirebilir), `childId` velinin
 * seçili çocuğu (salt okunur). 404'te kart gizlenir: özellik kapalıdır ya da çocuk veliye bağlı değildir.
 */
@Component({
  selector: 'app-target-goal-card',
  standalone: true,
  imports: [FormsModule, MatButtonModule, MatButtonToggleModule, MatCardModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressSpinnerModule, TranslocoDirective],
  providers: [provideTranslocoScope('target-goals')],
  templateUrl: './target-goal-card.component.html',
  styleUrl: './target-goal-card.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class TargetGoalCardComponent {
  readonly childId = input<number | null>(null);

  private readonly service = inject(TargetGoalService);
  private readonly localeService = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly load$ = new Subject<number | null>();
  private readonly search$ = new Subject<void>();

  protected readonly goal = signal<TargetGoal | null>(null);
  protected readonly loading = signal(true);
  protected readonly hidden = signal(false);
  protected readonly failed = signal(false);
  protected readonly saveFailed = signal(false);
  protected readonly saving = signal(false);

  protected readonly picking = signal(false);
  protected readonly exam = signal<TargetExam>('lgs');
  protected readonly term = signal('');
  protected readonly options = signal<TargetProgram[]>([]);
  protected readonly searching = signal(false);
  protected readonly searchFailed = signal(false);

  protected readonly readOnly = computed(() => this.childId() !== null);
  protected readonly gap = computed(() => {
    const goal = this.goal();
    return goal?.target ? computeTargetGap(goal.target, goal.results) : null;
  });

  constructor() {
    this.load$
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.failed.set(false);
          this.hidden.set(false);
        }),
        switchMap((childId) =>
          (childId === null ? this.service.getMine() : this.service.getChild(childId)).pipe(
            catchError((error: HttpErrorResponse) => {
              if (error.status === 404) {
                this.hidden.set(true);
              } else {
                this.failed.set(true);
              }
              this.goal.set(null);
              this.loading.set(false);
              return EMPTY;
            }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((goal) => {
        this.goal.set(goal);
        this.loading.set(false);
      });

    this.search$
      .pipe(
        debounceTime(SEARCH_DEBOUNCE_MS),
        tap(() => {
          this.searching.set(true);
          this.searchFailed.set(false);
        }),
        switchMap(() =>
          this.service.searchPrograms({ exam: this.exam(), search: this.term().trim(), take: SEARCH_TAKE }).pipe(
            catchError(() => {
              this.searchFailed.set(true);
              return of({ total: 0, items: [] });
            }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((page) => {
        this.options.set(page.items);
        this.searching.set(false);
      });

    effect(() => {
      const childId = this.childId();
      untracked(() => this.load$.next(childId));
    });
  }

  protected reload(): void {
    this.load$.next(this.childId());
  }

  protected openPicker(): void {
    const scoreType = this.goal()?.target?.scoreType;
    if (scoreType) {
      this.exam.set(scoreType === 'lgs' ? 'lgs' : 'yks');
    }
    this.term.set('');
    this.options.set([]);
    this.picking.set(true);
    this.search$.next();
  }

  protected closePicker(): void {
    this.picking.set(false);
  }

  protected changeExam(exam: TargetExam): void {
    this.exam.set(exam);
    this.search$.next();
  }

  protected changeTerm(term: string): void {
    this.term.set(term);
    this.search$.next();
  }

  protected choose(program: TargetProgram): void {
    this.saving.set(true);
    this.saveFailed.set(false);
    this.service
      .setMine(program.id)
      .pipe(
        finalize(() => this.saving.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (goal) => {
          this.goal.set(goal);
          this.picking.set(false);
        },
        error: () => this.saveFailed.set(true),
      });
  }

  protected remove(): void {
    this.saving.set(true);
    this.saveFailed.set(false);
    this.service
      .clearMine()
      .pipe(
        finalize(() => this.saving.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => this.goal.set({ target: null, results: [] }),
        error: () => this.saveFailed.set(true),
      });
  }

  protected place(program: TargetProgram): string {
    return [program.institution, program.district, program.city].filter(Boolean).join(' · ');
  }

  protected number(value: number, digits = 1): string {
    return new Intl.NumberFormat(this.localeService.localeDefinition().angularLocale, { maximumFractionDigits: digits }).format(value);
  }
}
