import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslocoDirective } from '@jsverse/transloco';

import { LANDING_STEP_ROLES } from '../landing-content';

@Component({
  selector: 'app-how-it-works',
  standalone: true,
  imports: [MatTabsModule, TranslocoDirective],
  templateUrl: './how-it-works.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HowItWorksComponent {
  readonly roles = LANDING_STEP_ROLES;
  readonly steps = [1, 2, 3, 4] as const;
}
