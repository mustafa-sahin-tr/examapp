import { Component } from '@angular/core';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

import { PUBLIC_PAGES_SCOPE, usePublicPageMeta } from '../public-page-meta';

@Component({
  selector: 'app-contact',
  standalone: true,
  imports: [TranslocoDirective],
  providers: [provideTranslocoScope(PUBLIC_PAGES_SCOPE)],
  templateUrl: './contact.component.html',
  styleUrl: './contact.component.scss',
})
export class ContactComponent {
  constructor() {
    usePublicPageMeta('contact');
  }
}
