import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import {
  QUESTION_REPORT_NOTE_MAX_LENGTH,
  QuestionReportItem,
  QuestionReportListStatus,
  QuestionReportOutcome,
} from '../../models/question-report';
import { QuestionReportService } from '../../services/question-report.service';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';

const QUESTION_REPORTS_SCOPE = 'question-reports';
const PAGE_SIZE = 20;

/**
 * Issue #781: öğretmen kendi sorularına, admin tüm sorulara gelen bildirimleri görür; bildiren görünmez.
 * #865: satır soru başına birleşik (kaç bildirim, hangi testler, kaç öğrenci cevapladı); iki karar — "Soruyu düzelt"
 * soruyu yeni sekmede açar ve satırda "Düzelttim" kapatmasını gösterir, "Sorun yok" kısa not ister.
 */
@Component({
  selector: 'app-question-reports',
  standalone: true,
  imports: [PageHeaderComponent, DatePipe, RouterLink, MatButtonModule, MatButtonToggleModule, MatFormFieldModule, MatIconModule, MatInputModule, MatPaginatorModule, TranslocoDirective],
  providers: [provideTranslocoScope(QUESTION_REPORTS_SCOPE)],
  templateUrl: './question-reports.component.html',
  styleUrl: './question-reports.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuestionReportsComponent implements OnInit {
  private readonly reports = inject(QuestionReportService);

  protected readonly pageSize = PAGE_SIZE;
  protected readonly status = signal<QuestionReportListStatus>('Open');
  protected readonly page = signal(1);
  protected readonly items = signal<QuestionReportItem[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly actionFailed = signal(false);
  protected readonly noteMaxLength = QUESTION_REPORT_NOTE_MAX_LENGTH;
  /** Düzeltmek için açılan soruların satırları: "Düzelttim" kapatması görünür. */
  protected readonly fixing = signal<ReadonlySet<number>>(new Set());
  /** "Sorun yok" notu yazılan satır. */
  protected readonly noIssueFor = signal<number | null>(null);
  protected readonly noIssueNote = signal('');
  protected readonly saving = signal(false);

  ngOnInit(): void {
    this.load();
  }

  protected changeStatus(status: QuestionReportListStatus): void {
    this.status.set(status);
    this.page.set(1);
    this.load();
  }

  protected changePage(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    this.load();
  }

  protected startFix(item: QuestionReportItem): void {
    this.fixing.update((ids) => new Set(ids).add(item.id));
  }

  protected askNoIssue(item: QuestionReportItem): void {
    this.noIssueFor.set(item.id);
    this.noIssueNote.set('');
  }

  protected cancelNoIssue(): void {
    this.noIssueFor.set(null);
  }

  protected markReviewed(item: QuestionReportItem, outcome: QuestionReportOutcome): void {
    if (this.saving()) {
      return;
    }
    this.actionFailed.set(false);
    this.saving.set(true);
    const note = outcome === 'noIssue' ? this.noIssueNote().trim() || null : null;
    this.reports.markReviewed(item.id, { outcome, note }).subscribe({
      next: () => {
        this.saving.set(false);
        this.noIssueFor.set(null);
        // Sayfanın son satırı gittiyse boş sayfada kalınmaz.
        if (this.items().length === 1 && this.page() > 1) {
          this.page.update((page) => page - 1);
        }
        this.load();
      },
      error: () => {
        this.saving.set(false);
        this.actionFailed.set(true);
      },
    });
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.reports.list(this.status(), this.page(), PAGE_SIZE).subscribe({
      next: (result) => {
        this.items.set(result.items);
        this.total.set(result.totalCount);
        this.loading.set(false);
      },
      error: () => {
        this.items.set([]);
        this.total.set(0);
        this.loading.set(false);
        this.loadFailed.set(true);
      },
    });
  }
}
