import { Component } from '@angular/core';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

import { PUBLIC_PAGES_SCOPE, usePublicPageMeta } from '../public-page-meta';

@Component({
  selector: 'app-privacy-policy',
  standalone: true,
  imports: [TranslocoDirective],
  providers: [provideTranslocoScope(PUBLIC_PAGES_SCOPE)],
  templateUrl: './privacy-policy.component.html',
  styleUrl: './privacy-policy.component.scss',
})
export class PrivacyPolicyComponent {
  constructor() {
    usePublicPageMeta('privacyPolicy');
  }
}
