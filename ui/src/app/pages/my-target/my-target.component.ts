import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { Subject, catchError, debounceTime, of, switchMap, tap } from 'rxjs';

import { TargetExam, TargetGoal, TargetProgram, TargetProgramsForScore } from '../../models/target-goal.model';
import { LocaleService } from '../../services/locale.service';
import { TargetGoalService } from '../../services/target-goal.service';
import { TargetGoalCardComponent, examForGrade } from '../../shared/components/target-goal-card/target-goal-card.component';
import { TargetProgressPoint, targetProgress } from '../public/score-calculator/target-gap';
import { TargetProgressChartComponent } from './target-progress-chart.component';

const SCORE_DEBOUNCE_MS = 300;
const MAX_SCORE = 1000;
const LGS_SCORE_TYPES = ['lgs'];
const YKS_SCORE_TYPES = ['tyt', 'say', 'ea', 'soz'];

/** Öğrencinin hedefi, denemeden denemeye gelişimi ve puanına göre okullar. */
@Component({
  selector: 'app-my-target',
  standalone: true,
  imports: [
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    TargetGoalCardComponent,
    TargetProgressChartComponent,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope('target-goals')],
  templateUrl: './my-target.component.html',
  styleUrl: './my-target.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class MyTargetComponent {
  private readonly service = inject(TargetGoalService);
  private readonly localeService = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly card = viewChild(TargetGoalCardComponent);
  private readonly search$ = new Subject<void>();

  protected readonly goal = signal<TargetGoal | null>(null);
  protected readonly scoreType = signal('lgs');
  protected readonly points = signal<TargetProgressPoint[]>([]);
  protected readonly historyLoaded = signal(false);
  protected readonly historyFailed = signal(false);

  protected readonly score = signal<number | null>(null);
  protected readonly city = signal('');
  protected readonly cities = signal<string[]>([]);
  protected readonly programs = signal<TargetProgramsForScore | null>(null);
  protected readonly searching = signal(false);
  protected readonly searchFailed = signal(false);
  protected readonly saving = signal(false);
  protected readonly saveFailed = signal(false);

  /** Sınıf sınavı belirliyse yalnız onun puan türleri; geçiş sınıfı ve bilinmeyen sınıf hepsini görür. */
  protected readonly scoreTypes = computed(() => {
    const exam = examForGrade(this.goal()?.gradeLevel);
    return exam === 'lgs' ? LGS_SCORE_TYPES : exam === 'yks' ? YKS_SCORE_TYPES : [...LGS_SCORE_TYPES, ...YKS_SCORE_TYPES];
  });

  protected readonly target = computed(() => {
    const target = this.goal()?.target;
    return target && target.scoreType === this.scoreType() ? target : null;
  });

  /** Son iki denemenin hedefe uzaklık farkı; pozitif = yaklaştı. */
  protected readonly trend = computed(() => {
    const target = this.target();
    const points = this.points();
    if (!target || points.length < 2) return null;
    const last = points[points.length - 1].score;
    const previous = points[points.length - 2].score;
    return {
      change: Math.abs(target.minimumScore - previous) - Math.abs(target.minimumScore - last),
      remaining: target.minimumScore - last,
    };
  });

  protected readonly groups = computed(() => {
    const programs = this.programs();
    return programs
      ? [
          { key: 'safe', items: programs.safe },
          { key: 'borderline', items: programs.borderline },
          { key: 'stretch', items: programs.stretch },
        ]
      : [];
  });

  constructor() {
    this.search$
      .pipe(
        debounceTime(SCORE_DEBOUNCE_MS),
        tap(() => this.searchFailed.set(false)),
        switchMap(() => {
          const score = this.score();
          if (score === null || score < 0 || score > MAX_SCORE) {
            this.programs.set(null);
            return of(null);
          }
          this.searching.set(true);
          return this.service.programsForScore(this.scoreType(), score, this.city() || undefined).pipe(
            catchError(() => {
              this.searchFailed.set(true);
              return of(null);
            }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((programs) => {
        this.programs.set(programs);
        this.searching.set(false);
      });
  }

  protected onGoal(goal: TargetGoal): void {
    this.goal.set(goal);
    const types = this.scoreTypes();
    const next = goal.target?.scoreType ?? (types.includes(this.scoreType()) ? this.scoreType() : types[0]);
    // Hedef değişince yazılmış puan ezilmesin; aynı puan türünde yeniden yüklemeye gerek yok.
    if (next !== this.scoreType() || !this.historyLoaded()) {
      this.applyScoreType(next);
    }
  }

  protected changeScoreType(scoreType: string): void {
    this.city.set('');
    this.applyScoreType(scoreType);
  }

  private applyScoreType(scoreType: string): void {
    this.scoreType.set(scoreType);
    this.loadHistory(scoreType);
    this.loadCities(scoreType);
  }

  private loadHistory(scoreType: string): void {
    this.historyLoaded.set(false);
    this.historyFailed.set(false);
    this.service
      .history(scoreType)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (history) => {
          const points = targetProgress(scoreType, history);
          this.points.set(points);
          this.historyLoaded.set(true);
          this.score.set(points.length ? points[points.length - 1].score : this.score());
          this.search$.next();
        },
        error: () => {
          this.historyFailed.set(true);
          this.historyLoaded.set(true);
          this.search$.next();
        },
      });
  }

  private loadCities(scoreType: string): void {
    const exam: TargetExam = scoreType === 'lgs' ? 'lgs' : 'yks';
    this.service
      .programCities(exam)
      .pipe(
        catchError(() => of([] as string[])),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((cities) => this.cities.set(cities));
  }

  protected changeScore(value: string | number | null): void {
    const score = value === null || value === '' ? null : Number(value);
    this.score.set(score !== null && Number.isFinite(score) ? score : null);
    this.search$.next();
  }

  protected changeCity(city: string): void {
    this.city.set(city);
    this.search$.next();
  }

  protected choose(program: TargetProgram): void {
    this.saving.set(true);
    this.saveFailed.set(false);
    this.service
      .setMine(program.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.card()?.reload();
        },
        error: () => {
          this.saving.set(false);
          this.saveFailed.set(true);
        },
      });
  }

  protected place(program: TargetProgram): string {
    return [program.institution, program.district, program.city].filter(Boolean).join(' · ');
  }

  protected number(value: number, digits = 1): string {
    return new Intl.NumberFormat(this.localeService.localeDefinition().angularLocale, { maximumFractionDigits: digits }).format(value);
  }

  protected date(iso: string | null): string {
    if (!iso) return '';
    const hasZone = /(Z|[+-]\d{2}:?\d{2})$/i.test(iso);
    return new Intl.DateTimeFormat(this.localeService.localeDefinition().angularLocale, { dateStyle: 'medium' }).format(new Date(hasZone ? iso : `${iso}Z`));
  }
}
