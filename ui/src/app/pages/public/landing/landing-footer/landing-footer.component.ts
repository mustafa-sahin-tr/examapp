import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoPipe } from '@jsverse/transloco';

import { LandingAnchorDirective } from '../landing-anchor.directive';
import {
  LANDING_CONTACT_EMAIL,
  LANDING_LOGIN_URL,
  LANDING_REGISTER_STUDENT_URL,
  LANDING_REGISTER_TEACHER_URL,
} from '../landing-content';

@Component({
  selector: 'app-landing-footer',
  standalone: true,
  imports: [RouterLink, TranslocoDirective, TranslocoPipe, LandingAnchorDirective],
  templateUrl: './landing-footer.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LandingFooterComponent {
  readonly email = LANDING_CONTACT_EMAIL;
  readonly loginUrl = LANDING_LOGIN_URL;
  readonly registerStudentUrl = LANDING_REGISTER_STUDENT_URL;
  readonly registerTeacherUrl = LANDING_REGISTER_TEACHER_URL;
  readonly year = new Date().getFullYear();
  readonly platformLinks = [
    { id: 'ozellikler', key: 'features' },
    { id: 'nasil-calisir', key: 'howItWorks' },
    { id: 'ozel-ders', key: 'tutoring' },
    { id: 'sss', key: 'faq' },
  ] as const;
}
