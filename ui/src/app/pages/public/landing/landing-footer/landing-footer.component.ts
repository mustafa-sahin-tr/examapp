import { Component } from '@angular/core';
import { TranslocoDirective, TranslocoPipe } from '@jsverse/transloco';

@Component({
  selector: 'app-landing-footer',
  imports: [TranslocoDirective, TranslocoPipe],
  templateUrl: './landing-footer.component.html',
  styleUrls: ['./landing-footer.component.scss'],
  standalone: true,
})
export class LandingFooterComponent {

}
