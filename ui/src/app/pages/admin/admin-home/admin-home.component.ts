import { Component, ChangeDetectionStrategy } from '@angular/core';
import { MatTabsModule } from '@angular/material/tabs';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { TaxonomyManagerComponent } from '../taxonomy-manager/taxonomy-manager.component';
import { ClassifierStatusComponent } from '../classifier-status/classifier-status.component';

/** Yönetim ekranlarının ortak Transloco scope'u: `public/i18n/admin/<lang>.json` (issue #183). */
const ADMIN_SCOPE = 'admin';

/**
 * Sekme sırası — şablon bu diziden üretilir, bu yüzden bir sekmenin index'i her zaman
 * `ADMIN_HOME_TABS.indexOf(...)` ile doğru bulunur (sabit sayı yok).
 */
// #853: "Öğretmen Başvuruları" sekmesi kalktı — sol menüde kendi sayfası var (/admin/teacher-approvals).
// #897: "Okullar" da kalktı (/admin/schools menüde); "Sınıflandırma Cache" yerine etkin sağlayıcıyı gösteren "Sınıflandırıcı".
export const ADMIN_HOME_TABS = ['taxonomy', 'classifier'] as const;
export type AdminHomeTab = (typeof ADMIN_HOME_TABS)[number];

@Component({
  selector: 'app-admin-home',
  standalone: true,
  providers: [provideTranslocoScope(ADMIN_SCOPE)],
  template: `
    <div class="admin-page" *transloco="let t; prefix: 'admin.home'">
      <header class="admin-header">
        <mat-icon>account_tree</mat-icon>
        <h1>{{ t('title') }}</h1>
      </header>

      <mat-tab-group animationDuration="150ms" mat-stretch-tabs="false">
        @for (tab of tabs; track tab) {
          <mat-tab [label]="t('tabs.' + tab)">
            <div class="tab-body">
              @switch (tab) {
                @case ('taxonomy') {
                  <app-taxonomy-manager></app-taxonomy-manager>
                }
                @case ('classifier') {
                  <app-classifier-status></app-classifier-status>
                }
              }
            </div>
          </mat-tab>
        }
      </mat-tab-group>
    </div>
  `,
  styles: [
    `
      .admin-page {
        padding: 20px 24px 40px;
        max-width: 1200px;
        margin: 0 auto;
      }
      .admin-header {
        display: flex;
        align-items: center;
        gap: 10px;
        margin-bottom: 12px;

        h1 {
          margin: 0;
          font-size: 22px;
          font-weight: 700;
          color: var(--heading-on-dark, #F5F4FF);
        }
        mat-icon {
          color: var(--primaryColor, #6438c3);
        }
      }
      .tab-body {
        padding-top: 20px;
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [
    MatTabsModule,
    MatIconModule,
    TaxonomyManagerComponent,
    ClassifierStatusComponent,
    TranslocoDirective,
  ],
})
export class AdminHomeComponent {
  readonly tabs = ADMIN_HOME_TABS;
}
