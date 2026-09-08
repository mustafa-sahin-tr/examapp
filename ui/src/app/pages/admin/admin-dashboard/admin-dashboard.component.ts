import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { finalize } from 'rxjs';
import { AdminService } from '../../../services/admin.service';
import { AdminDashboardSummary } from '../../../models/admin-dashboard.model';

type SummaryCardKey = 'teachers' | 'students' | 'worksheets' | 'questions';

interface SummaryCardViewModel {
  key: SummaryCardKey;
  label: string;
  value: number;
  icon: string;
}

/** Skeleton'da çizilecek kart sayısı: 4 sayaç + 1 AI kartı. */
const SKELETON_CARD_COUNT = 5;

/**
 * Issue #86 — Admin dashboard, Phase 1: sayaç kartları.
 * Öğretmen / öğrenci / test / soru toplamları + AI sınıflandırılmış soru oranı.
 * Trend ve zaman serisi Phase 2'de.
 */
@Component({
  selector: 'app-admin-dashboard',
  standalone: true,
  imports: [DecimalPipe, MatButtonModule, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './admin-dashboard.component.html',
  styleUrls: ['./admin-dashboard.component.scss'],
})
export class AdminDashboardComponent implements OnInit {
  private readonly adminService = inject(AdminService);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly summary = signal<AdminDashboardSummary | null>(null);

  readonly skeletonCards = Array.from({ length: SKELETON_CARD_COUNT }, (_, i) => i);

  /** Platformda hiç veri yoksa: kartlar 0 gösterir, ayırt edici boş-durum metni çıkar. */
  readonly isEmpty = computed(() => {
    const s = this.summary();
    return (
      !!s &&
      s.teacherCount === 0 &&
      s.studentCount === 0 &&
      s.worksheetCount === 0 &&
      s.questionCount === 0
    );
  });

  /** Dört düz sayaç kartı; AI kartı ayrı işlenir (oran + ilerleme çubuğu). */
  readonly cards = computed<SummaryCardViewModel[]>(() => {
    const s = this.summary();
    if (!s) {
      return [];
    }
    return [
      { key: 'teachers', label: 'Öğretmen', value: s.teacherCount, icon: 'school' },
      { key: 'students', label: 'Öğrenci', value: s.studentCount, icon: 'groups' },
      { key: 'worksheets', label: 'Test', value: s.worksheetCount, icon: 'assignment' },
      { key: 'questions', label: 'Soru', value: s.questionCount, icon: 'quiz' },
    ];
  });

  /** Hiç soru yoksa AI kartı oran yerine "Henüz soru yok" gösterir. */
  readonly hasQuestions = computed(() => (this.summary()?.questionCount ?? 0) > 0);

  /** 0..100 arası, ilerleme çubuğu genişliği ve rozet için. Backend 0..1 döner. */
  readonly aiPercent = computed(() => {
    const ratio = this.summary()?.aiClassifiedRatio ?? 0;
    return Math.min(100, Math.max(0, ratio * 100));
  });

  ngOnInit(): void {
    this.loadSummary();
  }

  loadSummary(): void {
    this.loading.set(true);
    this.error.set(null);

    this.adminService
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
