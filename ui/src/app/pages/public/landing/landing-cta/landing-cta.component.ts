import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoDirective } from '@jsverse/transloco';

import { LANDING_REGISTER_STUDENT_URL, LANDING_REGISTER_TEACHER_URL } from '../landing-content';

@Component({
  selector: 'app-landing-cta',
  standalone: true,
  imports: [MatButtonModule, TranslocoDirective],
  templateUrl: './landing-cta.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LandingCtaComponent {
  readonly registerStudentUrl = LANDING_REGISTER_STUDENT_URL;
  readonly registerTeacherUrl = LANDING_REGISTER_TEACHER_URL;
}
