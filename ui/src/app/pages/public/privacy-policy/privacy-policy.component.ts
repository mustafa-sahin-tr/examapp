import { Component, ChangeDetectionStrategy } from '@angular/core';
import { provideTranslocoScope } from '@jsverse/transloco';

import { LegalPageComponent, LegalSection } from '../legal-page/legal-page.component';
import { PUBLIC_PAGES_SCOPE, usePublicPageMeta } from '../public-page-meta';

/** #881: bölümler `public-pages.privacyPolicy.sections` altında; madde sayısı metinle birlikte güncellenir. */
export const PRIVACY_SECTIONS: readonly LegalSection[] = [{ key: 'controller' }, { key: 'data', items: 7 }, { key: 'purposes', items: 5 }, { key: 'legalBasis', items: 4 }, { key: 'transfer', items: 4 }, { key: 'collection' }, { key: 'children' }, { key: 'retention' }, { key: 'rights', items: 9 }, { key: 'contact' }];

@Component({
  selector: 'app-privacy-policy',
  standalone: true,
  imports: [LegalPageComponent],
  providers: [provideTranslocoScope(PUBLIC_PAGES_SCOPE)],
  template: `<app-legal-page prefix="privacyPolicy" [sections]="sections" />`,
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class PrivacyPolicyComponent {
  protected readonly sections = PRIVACY_SECTIONS;

  constructor() {
    usePublicPageMeta('privacyPolicy');
  }
}
