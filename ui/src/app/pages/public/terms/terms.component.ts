import { Component, ChangeDetectionStrategy } from '@angular/core';
import { provideTranslocoScope } from '@jsverse/transloco';

import { LegalPageComponent, LegalSection } from '../legal-page/legal-page.component';
import { PUBLIC_PAGES_SCOPE, usePublicPageMeta } from '../public-page-meta';

/** #881: bölümler `public-pages.terms.sections` altında; madde sayısı metinle birlikte güncellenir. */
export const TERMS_SECTIONS: readonly LegalSection[] = [{ key: 'service' }, { key: 'accounts', items: 3 }, { key: 'rules', items: 5 }, { key: 'content' }, { key: 'ai' }, { key: 'tutoring' }, { key: 'plans', items: 4 }, { key: 'suspension' }, { key: 'liability' }, { key: 'changes' }, { key: 'contact' }];

@Component({
  selector: 'app-terms',
  standalone: true,
  imports: [LegalPageComponent],
  providers: [provideTranslocoScope(PUBLIC_PAGES_SCOPE)],
  template: `<app-legal-page prefix="terms" [sections]="sections" />`,
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class TermsComponent {
  protected readonly sections = TERMS_SECTIONS;

  constructor() {
    usePublicPageMeta('terms');
  }
}
