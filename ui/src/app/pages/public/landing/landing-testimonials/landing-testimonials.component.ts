import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';

import { LANDING_TESTIMONIALS, LANDING_TESTIMONIALS_ARE_FICTIONAL } from '../landing-content';

@Component({
  selector: 'app-landing-testimonials',
  standalone: true,
  imports: [MatIconModule, TranslocoDirective],
  templateUrl: './landing-testimonials.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LandingTestimonialsComponent {
  readonly testimonials = LANDING_TESTIMONIALS;
  /** Bayrak true iken not kaldırılamaz; yalnız tümü izinli gerçek yorum olunca kalkar. */
  readonly showDisclaimer: boolean = LANDING_TESTIMONIALS_ARE_FICTIONAL;
}
