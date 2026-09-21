import { Component } from '@angular/core';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

import { PUBLIC_PAGES_SCOPE, usePublicPageMeta } from '../public-page-meta';

@Component({
  selector: 'app-faq',
  standalone: true,
  imports: [TranslocoDirective],
  providers: [provideTranslocoScope(PUBLIC_PAGES_SCOPE)],
  templateUrl: './faq.component.html',
  styleUrl: './faq.component.scss',
})
export class FaqComponent {
  constructor() {
    usePublicPageMeta('faq');
  }
}
