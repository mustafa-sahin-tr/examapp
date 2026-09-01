import { Component } from '@angular/core';
import { MatTabsModule } from '@angular/material/tabs';
import { MatIconModule } from '@angular/material/icon';
import { TaxonomyManagerComponent } from '../taxonomy-manager/taxonomy-manager.component';
import { ClassifierCacheComponent } from '../classifier-cache/classifier-cache.component';

@Component({
  selector: 'app-admin-home',
  standalone: true,
  template: `
    <div class="admin-page">
      <header class="admin-header">
        <mat-icon>admin_panel_settings</mat-icon>
        <h1>Yönetim</h1>
      </header>

      <mat-tab-group animationDuration="150ms" mat-stretch-tabs="false">
        <mat-tab label="Taksonomi">
          <div class="tab-body">
            <app-taxonomy-manager></app-taxonomy-manager>
          </div>
        </mat-tab>
        <mat-tab label="Sınıflandırma Cache">
          <div class="tab-body">
            <app-classifier-cache></app-classifier-cache>
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
          color: var(--main-foreground-color, #1a1a2e);
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
  imports: [MatTabsModule, MatIconModule, TaxonomyManagerComponent, ClassifierCacheComponent],
})
export class AdminHomeComponent {}
