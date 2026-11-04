import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

import { LANDING_SUBJECT_GROUPS } from '../landing-content';

@Component({
  selector: 'app-landing-subjects',
  standalone: true,
  imports: [TranslocoDirective],
  templateUrl: './landing-subjects.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LandingSubjectsComponent {
  readonly groups = LANDING_SUBJECT_GROUPS;
}
