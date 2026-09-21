import { Component } from '@angular/core';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

import { PUBLIC_PAGES_SCOPE, usePublicPageMeta } from '../public-page-meta';

@Component({
  selector: 'app-terms',
  standalone: true,
  imports: [TranslocoDirective],
  providers: [provideTranslocoScope(PUBLIC_PAGES_SCOPE)],
  templateUrl: './terms.component.html',
  styleUrls: ['./terms.component.scss'],
})
export class TermsComponent {
  constructor() {
    usePublicPageMeta('terms', [{ name: 'robots', content: 'index, follow' }]);
  }
}
