import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { Subject, catchError, of, switchMap } from 'rxjs';

import { ChildSubscriptionPlan, CurrentSubscriptionPlan, FREE_PLAN_KEY, planNameKey } from '../../../models/subscription';
import { LocaleService } from '../../../services/locale.service';
import { SubscriptionService } from '../../../services/subscription.service';
import { PlanPickerComponent } from '../../../shared/components/plan-picker/plan-picker.component';

/** Bitişine bu kadar gün (ya da daha az) kalan plan "yakında bitiyor" diye vurgulanır (#1091 hatırlatmasıyla aynı eşik). */
export const PLAN_ENDING_SOON_DAYS = 3;

/**
 * Issue #1091: veli panelinde seçili çocuğun planı — mevcut plan, bitişi ve "Plan seç" (öğrenci planları, sahte ödeme çocuk adına).
 * 404 (bağlantı kaldırılmış olabilir) panelin diğer bölümleri gibi `notFound` ile bildirilir.
 */
@Component({
  selector: 'app-parent-child-plan',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslocoDirective, PlanPickerComponent],
  providers: [provideTranslocoScope('parent-dashboard', 'subscription')],
  templateUrl: './parent-child-plan.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './parent-child-plan.component.scss',
})
export class ParentChildPlanComponent {
  private readonly subscriptions = inject(SubscriptionService);
  private readonly localeService = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);

  readonly studentId = input.required<number>();
  readonly notFound = output<void>();
  /** Plan alındı (panel çocuğun özelliklerini yeniden okur). */
  readonly changed = output<void>();

  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly plan = signal<ChildSubscriptionPlan | null>(null);
  readonly picking = signal(false);
  /** Son alınan planın scope içi ad anahtarı (onay notu). */
  readonly boughtKey = signal<string | null>(null);

  /** Test için sabitlenebilir "şimdi". */
  now: () => Date = () => new Date();

  readonly hasPlan = computed(() => {
    const key = this.plan()?.planKey;
    return !!key && key !== FREE_PLAN_KEY;
  });
  readonly nameKey = computed(() => planNameKey(this.plan()?.displayNameKey));
  readonly validTo = computed(() => {
    const iso = this.plan()?.validToUtc;
    if (!iso) return '';
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) return '';
    return new Intl.DateTimeFormat(this.localeService.localeDefinition().angularLocale, {
      day: 'numeric', month: 'long', year: 'numeric',
    }).format(date);
  });
  readonly endingSoon = computed(() => {
    const iso = this.plan()?.validToUtc;
    if (!iso || !this.hasPlan()) return false;
    const left = new Date(iso).getTime() - this.now().getTime();
    return left > 0 && left <= PLAN_ENDING_SOON_DAYS * 24 * 60 * 60 * 1000;
  });

  private readonly requests = new Subject<number>();

  constructor() {
    this.requests
      .pipe(
        switchMap((id) => {
          this.loading.set(true);
          this.loadError.set(false);
          return this.subscriptions.childPlan(id).pipe(catchError((err: unknown) => of(err)));
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe((result) => {
        this.loading.set(false);
        if (result instanceof HttpErrorResponse || result instanceof Error) {
          if (result instanceof HttpErrorResponse && result.status === 404) this.notFound.emit();
          else this.loadError.set(true);
          return;
        }
        this.plan.set(result as ChildSubscriptionPlan);
      });

    effect(() => {
      const id = this.studentId();
      untracked(() => {
        this.picking.set(false);
        this.boughtKey.set(null);
        this.requests.next(id);
      });
    });
  }

  reload(): void {
    this.requests.next(this.studentId());
  }

  onCompleted(bought: CurrentSubscriptionPlan | null): void {
    this.picking.set(false);
    if (!bought) return;
    this.boughtKey.set(planNameKey(bought.displayNameKey));
    this.reload();
    this.changed.emit();
  }
}
