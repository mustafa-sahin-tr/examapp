import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { QuestionReportItem, QuestionReportListStatus } from '../../models/question-report';
import { QuestionReportService } from '../../services/question-report.service';

const QUESTION_REPORTS_SCOPE = 'question-reports';
const PAGE_SIZE = 20;

/** Issue #781: öğretmen kendi sorularına, admin tüm sorulara gelen bildirimleri görür; bildiren görünmez. */
@Component({
  selector: 'app-question-reports',
  standalone: true,
  imports: [DatePipe, RouterLink, MatButtonModule, MatButtonToggleModule, MatIconModule, MatPaginatorModule, TranslocoDirective],
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

  protected markReviewed(item: QuestionReportItem): void {
    this.actionFailed.set(false);
    this.reports.markReviewed(item.id).subscribe({
      next: () => {
        // Sayfanın son satırı gittiyse boş sayfada kalınmaz.
        if (this.items().length === 1 && this.page() > 1) {
          this.page.update((page) => page - 1);
        }
        this.load();
      },
      error: () => this.actionFailed.set(true),
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
