import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';

import { LANDING_LOGIN_URL, LANDING_REGISTER_STUDENT_URL, LANDING_REGISTER_TEACHER_URL } from '../landing-content';

@Component({
  selector: 'app-landing-hero',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, TranslocoDirective],
  templateUrl: './landing-hero.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LandingHeroComponent {
  readonly facts = ['free', 'grades', 'languages', 'themes'] as const;
  readonly loginUrl = LANDING_LOGIN_URL;
  readonly registerStudentUrl = LANDING_REGISTER_STUDENT_URL;
  readonly registerTeacherUrl = LANDING_REGISTER_TEACHER_URL;
}
