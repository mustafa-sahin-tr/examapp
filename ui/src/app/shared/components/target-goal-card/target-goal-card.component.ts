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
import { MatSelectModule } from '@angular/material/select';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { EMPTY, Subject, catchError, debounceTime, finalize, of, switchMap, tap } from 'rxjs';

import { TargetExam, TargetGoal, TargetProgram } from '../../../models/target-goal.model';
import { computeTargetGap } from '../../../pages/public/score-calculator/target-gap';
import { LocaleService } from '../../../services/locale.service';
import { TargetGoalService } from '../../../services/target-goal.service';

const SEARCH_DEBOUNCE_MS = 300;
const SEARCH_TAKE = 20;

/** #891: sınava son sınıf (LGS 8, YKS 12) ve geçiş sınıfı (yalnız orada iki sınav da sunulur). */
const LGS_GRADE = 8;
const YKS_GRADE = 12;

/** #891: sınıftan sınav türü; 8. sınıf ve bilinmeyen sınıf seçebilir (null). */
export function examForGrade(level: number | null | undefined): TargetExam | null {
  if (!level || level === LGS_GRADE) return null;
  return level < LGS_GRADE ? 'lgs' : 'yks';
}

/**
 * Issue #695: hedef okul ve tabana kalan fark. Girdi yoksa öğrencinin kendisi (seçip değiştirebilir), `childId` velinin
 * seçili çocuğu (salt okunur). 404'te kart gizlenir: özellik kapalıdır ya da çocuk veliye bağlı değildir.
 */
@Component({
  selector: 'app-target-goal-card',
  standalone: true,
  imports: [FormsModule, MatButtonModule, MatButtonToggleModule, MatCardModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressSpinnerModule, MatSelectModule, TranslocoDirective],
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
  /** #891: il süzgeci ('' = tüm iller) ve katalogdaki iller. */
  protected readonly city = signal('');
  protected readonly cities = signal<string[]>([]);
  /** Son aramanın terimi; boş terimde liste "en çok seçilenler"dir. */
  protected readonly searchedTerm = signal('');

  protected readonly gradeLevel = computed(() => this.goal()?.gradeLevel ?? null);
  /** Sınıftan gelen sınav (null: seçim gösterilir). */
  protected readonly fixedExam = computed(() => examForGrade(this.gradeLevel()));
  /** Başlık satırı için sınava kalan yıl (0: bu yıl); sınıf bilinmiyorsa null. */
  protected readonly yearsToExam = computed(() => {
    const level = this.gradeLevel();
    if (!level) return null;
    const exam = this.fixedExam() ?? this.exam();
    return Math.max(0, (exam === 'lgs' ? LGS_GRADE : YKS_GRADE) - level);
  });
  /** Arama da il de boşken sonuç yoksa katalog yüklenmemiştir. */
  protected readonly catalogEmpty = computed(
    () => !this.searching() && !this.searchFailed() && this.options().length === 0 && !this.searchedTerm() && !this.city(),
  );

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
          this.service.searchPrograms({ exam: this.exam(), city: this.city() || undefined, search: this.term().trim(), take: SEARCH_TAKE }).pipe(
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
        this.searchedTerm.set(this.term().trim());
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
    const fixed = this.fixedExam();
    if (fixed) {
      this.exam.set(fixed);
    } else if (scoreType) {
      this.exam.set(scoreType === 'lgs' ? 'lgs' : 'yks');
    }
    this.term.set('');
    this.searchedTerm.set('');
    this.city.set(this.goal()?.target?.city ?? '');
    this.options.set([]);
    this.picking.set(true);
    this.loadCities();
    this.search$.next();
  }

  protected changeCity(city: string): void {
    this.city.set(city);
    this.search$.next();
  }

  private loadCities(): void {
    this.service
      .programCities(this.exam())
      .pipe(
        catchError(() => of([] as string[])),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((cities) => {
        this.cities.set(cities);
        if (this.city() && !cities.includes(this.city())) this.city.set('');
      });
  }

  protected closePicker(): void {
    this.picking.set(false);
  }

  protected changeExam(exam: TargetExam): void {
    this.exam.set(exam);
    this.city.set('');
    this.loadCities();
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
