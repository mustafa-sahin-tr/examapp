import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';

import { LANDING_ROLES } from '../landing-content';

@Component({
  selector: 'app-landing-roles',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, TranslocoDirective],
  templateUrl: './landing-roles.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LandingRolesComponent {
  readonly roles = LANDING_ROLES;
  readonly items = [1, 2, 3, 4] as const;
}
