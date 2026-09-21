import { Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoDirective } from '@jsverse/transloco';

@Component({
  selector: 'app-education-banner',
  imports: [MatButtonModule, TranslocoDirective],
  templateUrl: './education-banner.component.html',
  styleUrls: ['./education-banner.component.scss'],
  standalone: true,
})
export class EducationBannerComponent {

}
