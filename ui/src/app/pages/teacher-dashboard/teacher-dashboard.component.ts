import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { finalize } from 'rxjs';
import { SectionHeaderComponent } from '../../shared/components/section-header/section-header.component';
import { TeacherService } from '../../services/teacher.service';
import { TeacherDashboardSummary } from '../../models/teacher-dashboard.model';

interface SummaryCardViewModel {
  key: 'worksheets' | 'students';
  label: string;
  value: number;
  icon: string;
}

/**
 * Issue #53 — öğretmen dashboard'u, ilk dilim: özet kartları.
 * Sınavlarım tablosu, geride kalanlar ve aktivite kartları sonraki alt issue'larda (#54-#56).
 */
@Component({
  selector: 'app-teacher-dashboard',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatProgressSpinnerModule, SectionHeaderComponent],
  templateUrl: './teacher-dashboard.component.html',
  styleUrls: ['./teacher-dashboard.component.scss'],
})
export class TeacherDashboardComponent implements OnInit {
  private readonly teacherService = inject(TeacherService);

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

  ngOnInit(): void {
    this.loadSummary();
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
}
