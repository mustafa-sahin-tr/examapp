import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, output, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { Subject, catchError, debounceTime, of, switchMap, tap } from 'rxjs';
import { LearningOutcome } from '../../../models/learning-outcome';
import { LearningOutcomeService } from '../../../services/learning-outcome.service';

const SEARCH_DEBOUNCE_MS = 300;
export const MAX_LINKED_OUTCOMES = 20;

/**
 * Issue #692: sınıf ve derse göre süzülen kazanım seçici. Seçim üst bileşende tutulur; kazanım seçmek zorunlu değildir,
 * liste boşken de soru kaydedilebilir.
 */
@Component({
  selector: 'app-learning-outcome-picker',
  standalone: true,
  templateUrl: './learning-outcome-picker.component.html',
  styleUrls: ['./learning-outcome-picker.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, MatButtonModule, MatCheckboxModule, MatChipsModule, MatFormFieldModule, MatIconModule, MatInputModule],
  providers: [provideTranslocoScope('learning-outcomes')],
})
export class LearningOutcomePickerComponent {
  private readonly service = inject(LearningOutcomeService);
  private readonly destroyRef = inject(DestroyRef);

  readonly gradeId = input<number | null | undefined>(null);
  readonly subjectId = input<number | null | undefined>(null);
  readonly selected = input<LearningOutcome[]>([]);
  readonly selectedChange = output<LearningOutcome[]>();

  protected readonly term = signal('');
  protected readonly results = signal<LearningOutcome[]>([]);
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  protected readonly maxLinked = MAX_LINKED_OUTCOMES;

  private readonly requests = new Subject<{ gradeId: number; subjectId: number; term: string; immediate: boolean }>();

  constructor() {
    this.requests
      .pipe(
        switchMap((request) =>
          (request.immediate ? of(request) : of(request).pipe(debounceTime(SEARCH_DEBOUNCE_MS))).pipe(
            tap(() => {
              this.loading.set(true);
              this.failed.set(false);
            }),
            switchMap(({ gradeId, subjectId, term }) =>
              this.service.search(gradeId, subjectId, term).pipe(
                catchError(() => {
                  this.failed.set(true);
                  return of([] as LearningOutcome[]);
                })
              )
            )
          )
        ),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe((rows) => {
        this.results.set(rows);
        this.loading.set(false);
      });

    effect(() => {
      this.gradeId();
      this.subjectId();
      untracked(() => {
        this.term.set('');
        this.request(true);
      });
    });
  }

  protected hasScope(): boolean {
    return !!this.gradeId() && !!this.subjectId();
  }

  protected onSearch(value: string): void {
    this.term.set(value);
    this.request(false);
  }

  protected retry(): void {
    this.request(true);
  }

  protected isSelected(outcome: LearningOutcome): boolean {
    return this.selected().some((item) => item.id === outcome.id);
  }

  protected toggle(outcome: LearningOutcome): void {
    this.selectedChange.emit(
      this.isSelected(outcome) ? this.selected().filter((item) => item.id !== outcome.id) : [...this.selected(), outcome]
    );
  }

  private request(immediate: boolean): void {
    const gradeId = this.gradeId();
    const subjectId = this.subjectId();
    if (!gradeId || !subjectId) {
      this.results.set([]);
      return;
    }
    this.requests.next({ gradeId, subjectId, term: this.term(), immediate });
  }
}
