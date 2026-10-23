import { Component, ChangeDetectionStrategy } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

@Component({
  selector: 'app-landing-courses',
  imports: [TranslocoDirective],
  templateUrl: './landing-courses.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './landing-courses.component.scss'
})
export class LandingCoursesComponent {

}
