import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';
import { finalize } from 'rxjs';
import { SectionHeaderComponent } from '../../shared/components/section-header/section-header.component';
import { TeacherService } from '../../services/teacher.service';
import { TeacherDashboardSummary, TeacherWorksheetOverview } from '../../models/teacher-dashboard.model';

interface SummaryCardViewModel {
  key: 'worksheets' | 'students';
  label: string;
  value: number;
  icon: string;
}

/** Issue #54: tamamlanma yüzdesi bu eşiğin altındaysa uyarı, üstünde/eşitse başarı rengi. */
const COMPLETION_SUCCESS_THRESHOLD = 50;

/**
 * Öğretmen dashboard'u.
 * - Issue #53: özet kartları.
 * - Issue #54: "Sınavlarım" tablosu (mobilde kart listesi).
 * Geride kalanlar ve aktivite kartları sonraki alt issue'larda (#55-#56).
 */
@Component({
  selector: 'app-teacher-dashboard',
  standalone: true,
  imports: [
    DecimalPipe,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatTableModule,
    SectionHeaderComponent,
  ],
  templateUrl: './teacher-dashboard.component.html',
  styleUrls: ['./teacher-dashboard.component.scss'],
})
export class TeacherDashboardComponent implements OnInit {
  private readonly teacherService = inject(TeacherService);
  private readonly router = inject(Router);

  // ── Issue #53: özet kartları ──────────────────────────────────────────────
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly summary = signal<TeacherDashboardSummary | null>(null);

  /** Hiç sınav yoksa "boş" durumu: kartlar 0 gösterir, ek yönlendirme metni çıkar. */
  readonly isEmpty = computed(() => {
    const s = this.summary();
    return !!s && s.totalWorksheets === 0;
  });

  readonly cards = computed<SummaryCardViewModel[]>(() => {
    const s = this.summary();
    if (!s) {
      return [];
    }
    return [
      { key: 'worksheets', label: 'Toplam Sınav', value: s.totalWorksheets, icon: 'assignment' },
      { key: 'students', label: 'Benzersiz Öğrenci', value: s.totalUniqueStudents, icon: 'groups' },
    ];
  });

  // ── Issue #54: Sınavlarım tablosu ─────────────────────────────────────────
  readonly worksheetsLoading = signal(true);
  readonly worksheetsError = signal<string | null>(null);
  readonly worksheets = signal<TeacherWorksheetOverview[]>([]);
  readonly worksheetsEmpty = computed(() => this.worksheets().length === 0);

  readonly displayedColumns: readonly string[] = ['name', 'assignedStudentCount', 'completionPercentage'];

  ngOnInit(): void {
    this.loadSummary();
    this.loadWorksheetsOverview();
  }

  loadSummary(): void {
    this.loading.set(true);
    this.error.set(null);

    this.teacherService
      .getDashboardSummary()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (summary) => this.summary.set(summary),
        error: () => {
          this.summary.set(null);
          this.error.set('Özet bilgileri alınırken bir sorun oluştu.');
        },
      });
  }

  loadWorksheetsOverview(): void {
    this.worksheetsLoading.set(true);
    this.worksheetsError.set(null);

    this.teacherService
      .getWorksheetsOverview()
      .pipe(finalize(() => this.worksheetsLoading.set(false)))
      .subscribe({
        next: (rows) => this.worksheets.set(rows),
        error: () => {
          this.worksheets.set([]);
          this.worksheetsError.set('Sınav listesi alınırken bir sorun oluştu.');
        },
      });
  }

  /** ≥%50 başarı, <%50 uyarı chip'i. */
  completionClass(percentage: number): 'is-success' | 'is-warning' {
    return percentage >= COMPLETION_SUCCESS_THRESHOLD ? 'is-success' : 'is-warning';
  }

  openWorksheet(row: TeacherWorksheetOverview): void {
    void this.router.navigate(['/test', row.worksheetId]);
  }
}
