import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';

import { LANDING_FEATURES } from '../landing-content';

@Component({
  selector: 'app-landing-features',
  standalone: true,
  imports: [MatIconModule, TranslocoDirective],
  templateUrl: './landing-features.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LandingFeaturesComponent {
  readonly features = LANDING_FEATURES;
}
