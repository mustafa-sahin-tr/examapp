import { Component, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { CommentReportListComponent } from '../../../shared/components/comment-report-list/comment-report-list.component';

/** Yönetim ekranlarının ortak Transloco scope'u: `public/i18n/admin/<lang>.json`. */
const ADMIN_SCOPE = 'admin';

/**
 * Issue #305 — admin: tüm worksheet'lerdeki şikayet edilmiş yorumlar (`GET api/admin/comments/reports`).
 * Liste, sayfalama, gizle/aç ve derin link `app-comment-report-list`'te (worksheet detayındaki bölümle ortak).
 * Route `adminGuard` ile korunur; sunucu da yalnız Admin rolüne açar.
 */
@Component({
  selector: 'app-admin-comment-reports',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, TranslocoDirective, CommentReportListComponent],
  providers: [provideTranslocoScope(ADMIN_SCOPE)],
  templateUrl: './admin-comment-reports.component.html',
  styleUrls: ['./admin-comment-reports.component.scss'],
})
export class AdminCommentReportsComponent {
  private readonly list = viewChild(CommentReportListComponent);

  protected refresh(): void {
    this.list()?.reload();
  }
}
