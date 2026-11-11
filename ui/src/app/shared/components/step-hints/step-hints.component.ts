import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { Observable, switchMap, of } from 'rxjs';

import { HintState } from '../../../models/hints.model';
import { HintsService } from '../../../services/hints.service';

/**
 * Issue #1113: bir sorunun adım adım ipuçları. İlk tıklamada açılmış adımlar okunur (ücretsiz); hiç yoksa ilk adım açılır. Plan / hak
 * sınırı (403 / 429 `entitlement.*`) yükseltme penceresini interceptor açar; burada yalnız diğer hatalar yazılır.
 */
@Component({
  selector: 'app-step-hints',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, TranslocoDirective],
  providers: [provideTranslocoScope('hints')],
  templateUrl: './step-hints.component.html',
  styleUrl: './step-hints.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StepHintsComponent {
  private readonly service = inject(HintsService);
  private readonly destroyRef = inject(DestroyRef);

  readonly questionId = input.required<number>();

  readonly state = signal<HintState | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  readonly canReveal = computed(() => {
    const state = this.state();
    if (!state) return true;
    return !state.unavailable && (state.totalSteps === null || state.steps.length < state.totalSteps);
  });

  open(): void {
    this.run(
      this.service
        .get(this.questionId())
        .pipe(switchMap((s) => (s.steps.length || s.unavailable ? of(s) : this.service.next(this.questionId()))))
    );
  }

  next(): void {
    this.run(this.service.next(this.questionId()));
  }

  private run(request: Observable<HintState>): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (state) => {
        this.state.set(state);
        this.busy.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.busy.set(false);
        const code = typeof err.error?.code === 'string' ? err.error.code : '';
        if (code.startsWith('entitlement.')) return;
        this.error.set(typeof err.error?.message === 'string' ? err.error.message : 'failed');
      },
    });
  }
}
