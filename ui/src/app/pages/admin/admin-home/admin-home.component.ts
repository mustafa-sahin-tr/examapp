import { Component } from '@angular/core';
import { MatTabsModule } from '@angular/material/tabs';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { TaxonomyManagerComponent } from '../taxonomy-manager/taxonomy-manager.component';
import { ClassifierCacheComponent } from '../classifier-cache/classifier-cache.component';
import { TeacherApprovalsComponent } from '../teacher-approvals/teacher-approvals.component';

/** Yönetim ekranlarının ortak Transloco scope'u: `public/i18n/admin/<lang>.json` (issue #183). */
const ADMIN_SCOPE = 'admin';

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

      <mat-tab-group animationDuration="150ms" mat-stretch-tabs="false">
        <mat-tab [label]="t('tabs.taxonomy')">
          <div class="tab-body">
            <app-taxonomy-manager></app-taxonomy-manager>
          </div>
        </mat-tab>
        <mat-tab [label]="t('tabs.classifierCache')">
          <div class="tab-body">
            <app-classifier-cache></app-classifier-cache>
          </div>
        </mat-tab>
        <mat-tab [label]="t('tabs.teacherApplications')">
          <div class="tab-body">
            <app-teacher-approvals></app-teacher-approvals>
          </div>
        </mat-tab>
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
    ClassifierCacheComponent,
    TeacherApprovalsComponent,
    TranslocoDirective,
  ],
})
export class AdminHomeComponent {}
