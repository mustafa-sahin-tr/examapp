import { Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { MatTabsModule } from '@angular/material/tabs';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { TaxonomyManagerComponent } from '../taxonomy-manager/taxonomy-manager.component';
import { SchoolManagerComponent } from '../school-manager/school-manager.component';
import { ClassifierCacheComponent } from '../classifier-cache/classifier-cache.component';
import { TeacherApprovalsComponent } from '../teacher-approvals/teacher-approvals.component';
import { hasSchoolListFilterParams } from '../school-manager/school-list-filter';

/** Yönetim ekranlarının ortak Transloco scope'u: `public/i18n/admin/<lang>.json` (issue #183). */
const ADMIN_SCOPE = 'admin';

/**
 * Sekme sırası — şablon bu diziden üretilir, bu yüzden bir sekmenin index'i her zaman
 * `ADMIN_HOME_TABS.indexOf(...)` ile doğru bulunur (sabit sayı yok).
 */
export const ADMIN_HOME_TABS = ['taxonomy', 'schools', 'classifierCache', 'teacherApplications'] as const;
export type AdminHomeTab = (typeof ADMIN_HOME_TABS)[number];

@Component({
  selector: 'app-admin-home',
  standalone: true,
  providers: [provideTranslocoScope(ADMIN_SCOPE)],
  template: `
    <div class="admin-page" *transloco="let t; prefix: 'admin.home'">
      <header class="admin-header">
        <mat-icon>admin_panel_settings</mat-icon>
        <h1>{{ t('title') }}</h1>
      </header>

      <!-- Issue #281: URL'de okul filtresi varsa (derin link / yenileme) Okullar sekmesiyle açılır. -->
      <mat-tab-group animationDuration="150ms" mat-stretch-tabs="false" [selectedIndex]="initialTabIndex">
        @for (tab of tabs; track tab) {
          <mat-tab [label]="t('tabs.' + tab)">
            <div class="tab-body">
              @switch (tab) {
                @case ('taxonomy') {
                  <app-taxonomy-manager></app-taxonomy-manager>
                }
                @case ('schools') {
                  <app-school-manager [embedded]="true"></app-school-manager>
                }
                @case ('classifierCache') {
                  <app-classifier-cache></app-classifier-cache>
                }
                @case ('teacherApplications') {
                  <app-teacher-approvals></app-teacher-approvals>
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
  imports: [
    MatTabsModule,
    MatIconModule,
    TaxonomyManagerComponent,
    SchoolManagerComponent,
    ClassifierCacheComponent,
    TeacherApprovalsComponent,
    TranslocoDirective,
  ],
})
export class AdminHomeComponent {
  readonly tabs = ADMIN_HOME_TABS;

  /**
   * Yalnız ilk açılıştaki snapshot'a bakılır: sabit bir değer bağlandığı için kullanıcı sekme
   * değiştirince geri zorlanmaz; filtre değişiklikleri (replaceUrl) sekmeyi etkilemez.
   */
  readonly initialTabIndex = AdminHomeComponent.tabIndex(
    hasSchoolListFilterParams(inject(ActivatedRoute).snapshot.queryParamMap) ? 'schools' : 'taxonomy',
  );

  private static tabIndex(tab: AdminHomeTab): number {
    return ADMIN_HOME_TABS.indexOf(tab);
  }
}
