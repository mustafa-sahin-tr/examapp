import { Component, ChangeDetectionStrategy } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoDirective } from '@jsverse/transloco';

@Component({
  selector: 'app-education-banner',
  imports: [MatButtonModule, TranslocoDirective],
  templateUrl: './education-banner.component.html',
  styleUrls: ['./education-banner.component.scss'],
  changeDetection: ChangeDetectionStrategy.Eager,
  standalone: true,
})
export class EducationBannerComponent {

}
