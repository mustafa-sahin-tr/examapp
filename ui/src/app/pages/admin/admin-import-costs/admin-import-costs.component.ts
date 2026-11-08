import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

import { BookImportCostFilter, BookImportCostJob, BookImportCostKind, BookImportCostReport, BookImportCostUser } from '../../../models/book-import';
import { BookImportCostsService } from '../../../services/book-import-costs.service';
import { PageHeaderComponent } from '../../../shared/components/page-header/page-header.component';

/** Parası ödenmiş ama sonuçlanmamış işler tabloda işaretlenir. */
const UNFINISHED_STATUSES = ['Failed', 'Cancelled'];

function isoDate(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

/**
 * Issue #1056: kitap aktarımı ve hızlı eklemenin AI maliyet raporu. Üstte tarih / tür süzgeci, özet kutuları, kullanıcı başına
 * tablo (satıra tıklamak o kullanıcıya süzer), sayfalı iş tablosu ve CSV, aşama / model dökümü.
 */
@Component({
  selector: 'app-admin-import-costs',
  standalone: true,
  imports: [
    CurrencyPipe,
    DatePipe,
    DecimalPipe,
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatPaginatorModule,
    MatSelectModule,
    PageHeaderComponent,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope('book-imports')],
  templateUrl: './admin-import-costs.component.html',
  styleUrl: './admin-import-costs.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminImportCostsComponent implements OnInit {
  private readonly api = inject(BookImportCostsService);

  readonly from = signal<Date | null>(null);
  readonly to = signal<Date | null>(null);
  readonly kind = signal<BookImportCostKind | null>(null);
  readonly selectedUser = signal<{ id: number; name: string } | null>(null);

  readonly pageIndex = signal(0);
  readonly pageSize = signal(20);
  readonly pageSizeOptions = [20, 50, 100];

  readonly report = signal<BookImportCostReport | null>(null);
  readonly loading = signal(false);
  readonly exporting = signal(false);
  /** Kapsam içi hata anahtarı (`costReport.errors.*`). */
  readonly error = signal<string | null>(null);

  readonly validRange = computed(() => {
    const from = this.from();
    const to = this.to();
    return !!from && !!to && from <= to;
  });
  readonly isEmpty = computed(() => this.report()?.jobCount === 0);

  ngOnInit(): void {
    const now = new Date();
    this.from.set(new Date(now.getFullYear(), now.getMonth(), 1));
    this.to.set(new Date(now.getFullYear(), now.getMonth() + 1, 0));
    this.load();
  }

  applyFilters(): void {
    this.pageIndex.set(0);
    this.load();
  }

  selectUser(user: BookImportCostUser): void {
    this.selectedUser.set({ id: user.userId, name: user.fullName ?? `#${user.userId}` });
    this.applyFilters();
  }

  clearUser(): void {
    this.selectedUser.set(null);
    this.applyFilters();
  }

  onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    this.load();
  }

  exportCsv(): void {
    const filter = this.filter();
    if (!filter || this.exporting()) return;
    this.exporting.set(true);
    this.error.set(null);
    this.api.exportJobs(filter).subscribe({
      next: (blob) => {
        this.exporting.set(false);
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `aktarim-maliyetleri-${filter.from}-${filter.to}.csv`;
        link.click();
        URL.revokeObjectURL(url);
      },
      error: () => {
        this.exporting.set(false);
        this.error.set('costReport.errors.export');
      },
    });
  }

  jobLink(job: BookImportCostJob): string[] {
    return [job.kind === 'QuickAdd' ? '/quick-add' : '/book-imports', job.id];
  }

  isUnfinished(job: BookImportCostJob): boolean {
    return UNFINISHED_STATUSES.includes(job.status);
  }

  private filter(): BookImportCostFilter | null {
    const from = this.from();
    const to = this.to();
    if (!from || !to || !this.validRange()) return null;
    return { from: isoDate(from), to: isoDate(to), kind: this.kind(), userId: this.selectedUser()?.id ?? null };
  }

  private load(): void {
    const filter = this.filter();
    if (!filter) return;
    this.loading.set(true);
    this.error.set(null);
    this.api.report(filter, this.pageIndex() + 1, this.pageSize()).subscribe({
      next: (report) => {
        this.report.set(report);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.error.set('costReport.errors.load');
      },
    });
  }
}
