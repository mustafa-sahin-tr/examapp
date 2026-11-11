import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

/** Maddeler `subscription.terms.*`; kullanım koşullarındaki `plans` bölümüyle birlikte güncellenir. */
export const PURCHASE_TERMS = ['monthly', 'long', 'price', 'credits'] as const;

/**
 * Issue #1116: satın alma koşulları (iptal, 14 gün iade, fiyat değişikliği bildirimi, sayılı haklar). Ödeme ekranında ödemeden önce,
 * plan sayfalarında açılır kutu olarak gösterilir.
 */
@Component({
  selector: 'app-purchase-terms',
  standalone: true,
  imports: [RouterLink, TranslocoDirective],
  providers: [provideTranslocoScope('subscription')],
  template: `
    <details class="purchase-terms" data-testid="purchase-terms" *transloco="let t; prefix: 'subscription'" [open]="open()">
      <summary>{{ t('terms.title') }}</summary>
      <ul>
        @for (key of terms; track key) {
          <li>{{ t('terms.' + key) }}</li>
        }
      </ul>
      <a routerLink="/terms" target="_blank" rel="noopener">{{ t('terms.link') }}</a>
    </details>
  `,
  styles: `
    :host {
      display: block;
    }

    .purchase-terms {
      font-family: var(--paragraphFontFamily);
      font-size: 13px;
      color: var(--ms-text-medium);

      summary {
        cursor: pointer;
        font-weight: 600;
        color: var(--ms-text-strong);
      }

      ul {
        margin: 8px 0;
        padding-left: 20px;
        display: flex;
        flex-direction: column;
        gap: 4px;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PurchaseTermsComponent {
  protected readonly terms = PURCHASE_TERMS;

  /** Ödeme ekranında açık gelir. */
  readonly open = input(false);
}
