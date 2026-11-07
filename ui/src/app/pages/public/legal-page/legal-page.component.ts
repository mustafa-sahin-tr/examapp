import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';

import { LANDING_CONTACT_EMAIL } from '../landing/landing-content';

/** Bir yasal bölüm: başlık + paragraf, isteğe bağlı madde listesi (`items.1..n`). */
export interface LegalSection {
  key: string;
  items?: number;
}

/**
 * #881: gizlilik ve kullanım koşulları için ortak sayfa — geri dönüş bağlantısı, sürüm / tarih satırı, numaralı
 * bölümler. Metin `public-pages.<prefix>` altında: `title`, `version`, `updated`, `sections.<key>.{title,body,items}`.
 */
@Component({
  selector: 'app-legal-page',
  standalone: true,
  imports: [MatIconModule, RouterLink, TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './legal-page.component.scss',
  template: `
    <article class="legal" *transloco="let t; prefix: 'public-pages.' + prefix()">
      <a class="legal__back" routerLink="/welcome" *transloco="let c; prefix: 'public-pages.legal'">
        <mat-icon aria-hidden="true">arrow_back</mat-icon>{{ c('back') }}
      </a>
      <h1>{{ t('title') }}</h1>
      <p class="legal__meta" data-testid="legal-meta">{{ t('version') }} · {{ t('updated') }}</p>
      <p class="legal__intro">{{ t('intro') }}</p>
      <ol class="legal__sections">
        @for (section of sections(); track section.key) {
          <li [attr.id]="section.key">
            <h2>{{ t('sections.' + section.key + '.title') }}</h2>
            <p>{{ t('sections.' + section.key + '.body', { email }) }}</p>
            @if (section.items) {
              <ul>
                @for (i of range(section.items); track i) {
                  <li>{{ t('sections.' + section.key + '.items.' + i, { email }) }}</li>
                }
              </ul>
            }
          </li>
        }
      </ol>
    </article>
  `,
})
export class LegalPageComponent {
  readonly prefix = input.required<'privacyPolicy' | 'terms'>();
  readonly sections = input.required<readonly LegalSection[]>();
  protected readonly email = LANDING_CONTACT_EMAIL;

  protected range(count: number): number[] {
    return Array.from({ length: count }, (_, i) => i + 1);
  }
}
