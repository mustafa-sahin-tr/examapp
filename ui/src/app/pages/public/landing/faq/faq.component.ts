import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatExpansionModule } from '@angular/material/expansion';
import { TranslocoDirective } from '@jsverse/transloco';

import { LANDING_FAQ_KEYS } from '../landing-content';

@Component({
  selector: 'app-faq',
  standalone: true,
  imports: [MatExpansionModule, TranslocoDirective],
  templateUrl: './faq.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FaqComponent {
  readonly keys = LANDING_FAQ_KEYS;
}
