import { Component } from '@angular/core';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

import { PUBLIC_PAGES_SCOPE, usePublicPageMeta } from '../public-page-meta';

@Component({
  selector: 'app-about',
  standalone: true,
  imports: [TranslocoDirective],
  providers: [provideTranslocoScope(PUBLIC_PAGES_SCOPE)],
  templateUrl: './about.component.html',
  styleUrl: './about.component.scss',
})
export class AboutComponent {
  constructor() {
    usePublicPageMeta('about');
  }
}
