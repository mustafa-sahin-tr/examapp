import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';

import { LANDING_STATS, LANDING_STATS_ARE_SAMPLE } from '../landing-content';

@Component({
  selector: 'app-landing-stats',
  standalone: true,
  imports: [MatIconModule, TranslocoDirective],
  templateUrl: './landing-stats.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LandingStatsComponent {
  private readonly transloco = inject(TranslocoService);

  readonly stats = LANDING_STATS;
  /** Bayrak true iken not kaldırılamaz; yalnız gerçek sayılara geçişte kalkar. */
  readonly showSampleNote: boolean = LANDING_STATS_ARE_SAMPLE;

  format(value: number): string {
    return `${value.toLocaleString(this.transloco.getActiveLang())}+`;
  }
}
