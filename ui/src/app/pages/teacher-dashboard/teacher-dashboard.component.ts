import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';
import { TranslocoDirective, TranslocoPipe, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { finalize, take } from 'rxjs';
import { SectionHeaderComponent } from '../../shared/components/section-header/section-header.component';
import { TeacherService } from '../../services/teacher.service';
import {
  TeacherDashboardSummary,
  TeacherActiveStudent,
  TeacherLaggingStudent,
  TeacherOwnActivitySummary,
  TeacherStudentsActivitySummary,
  TeacherWorksheetOverview,
} from '../../models/teacher-dashboard.model';
import { LocaleService } from '../../services/locale.service';
import { DurationLabel, accuracyPercent, formatActivityDuration, formatActivityPeriod } from './activity-format';

interface SummaryCardViewModel {
  key: 'worksheets' | 'students';
  /** Scope'a göreli çeviri anahtarı; şablonda `t()` ile çözülür. */
  labelKey: string;
  value: number;
  icon: string;
}

/** Issue #56: aktivite kartlarının "tile" metriği. Değer ya sayı ya da okunur süre anahtarıdır. */
interface ActivityMetricViewModel {
  key: string;
  /** Scope'a göreli çeviri anahtarı. */
  labelKey: string;
  value: number | null;
  duration: DurationLabel | null;
  /** Doğru sayısının yanındaki doğruluk yüzdesi; 0'a bölme durumunda null. */
  accuracy: number | null;
}

/** Issue #56: "En Aktif Öğrenciler" ilk üç sıra rozetleri (maket). */
const RANK_MEDALS: readonly string[] = ['🥇', '🥈', '🥉'];

/** Issue #56: aktivite penceresi (gün). Backend 1..90 kabul eder. */
export const ACTIVITY_WINDOW_DAYS = 7;

/** Issue #54: tamamlanma yüzdesi bu eşiğin altındaysa uyarı, üstünde/eşitse başarı rengi. */
const COMPLETION_SUCCESS_THRESHOLD = 50;

/**
 * Öğretmen dashboard'u.
 * - Issue #53: özet kartları.
 * - Issue #54: "Sınavlarım" tablosu (mobilde kart listesi).
 * - Issue #55: "Geride Kalan Öğrenciler" tablosu (etiketli).
 * - Issue #56: "Benim Aktivitem" / "Öğrenci Aktivitesi" kartları + "En Aktif Öğrenciler" tablosu.
 *
 * Çeviriler kendi Transloco scope'unda: `public/i18n/teacher-dashboard/<lang>.json` (issue #183).
 */
const TEACHER_DASHBOARD_SCOPE = 'teacher-dashboard';

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
    TranslocoDirective,
    TranslocoPipe,
  ],
  providers: [provideTranslocoScope(TEACHER_DASHBOARD_SCOPE)],
  templateUrl: './teacher-dashboard.component.html',
  styleUrls: ['./teacher-dashboard.component.scss'],
})
export class TeacherDashboardComponent implements OnInit {
  private readonly teacherService = inject(TeacherService);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);

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
      { key: 'worksheets', labelKey: 'summary.cards.worksheets', value: s.totalWorksheets, icon: 'assignment' },
      { key: 'students', labelKey: 'summary.cards.students', value: s.totalUniqueStudents, icon: 'groups' },
    ];
  });

  // ── Issue #54: Sınavlarım tablosu ─────────────────────────────────────────
  readonly worksheetsLoading = signal(true);
  readonly worksheetsError = signal<string | null>(null);
  readonly worksheets = signal<TeacherWorksheetOverview[]>([]);
  readonly worksheetsEmpty = computed(() => this.worksheets().length === 0);

  readonly displayedColumns: readonly string[] = ['name', 'assignedStudentCount', 'completionPercentage'];

  // ── Issue #55: Geride Kalan Öğrenciler tablosu ────────────────────────────
  readonly laggingStudentsLoading = signal(true);
  readonly laggingStudentsError = signal<string | null>(null);
  readonly laggingStudents = signal<TeacherLaggingStudent[]>([]);
  readonly laggingStudentsEmpty = computed(() => this.laggingStudents().length === 0);

  readonly laggingDisplayedColumns: readonly string[] = ['studentName', 'worksheetName', 'flags'];

  // ── Issue #56: aktivite kartları + En Aktif Öğrenciler ────────────────────
  readonly activityDays = ACTIVITY_WINDOW_DAYS;

  /** Kartların altındaki dönem satırı (ör. "17–23 Eylül 2026"); dil değişince yeniden biçimlenir. */
  readonly activityPeriod = computed(() =>
    formatActivityPeriod(this.activityDays, new Date(), this.localeService.localeDefinition().angularLocale),
  );

  readonly ownActivityLoading = signal(true);
  readonly ownActivityError = signal<string | null>(null);
  readonly ownActivity = signal<TeacherOwnActivitySummary | null>(null);

  readonly studentsActivityLoading = signal(true);
  readonly studentsActivityError = signal<string | null>(null);
  readonly studentsActivity = signal<TeacherStudentsActivitySummary | null>(null);

  readonly ownActivityMetrics = computed<ActivityMetricViewModel[]>(() => {
    const a = this.ownActivity();
    if (!a) {
      return [];
    }
    return [
      this.metric('worksheetsCreated', 'activity.own.worksheetsCreated', a.worksheetsCreated),
      this.metric('assignmentsCreated', 'activity.own.assignmentsCreated', a.assignmentsCreated),
      this.metric('activeStudents', 'activity.own.activeStudents', a.activeStudents),
    ];
  });

  readonly studentsActivityMetrics = computed<ActivityMetricViewModel[]>(() => {
    const a = this.studentsActivity();
    if (!a) {
      return [];
    }
    return [
      this.metric('totalQuestionsSolved', 'activity.students.questionsSolved', a.totalQuestionsSolved),
      {
        ...this.metric('totalCorrectCount', 'activity.students.correctCount', a.totalCorrectCount),
        accuracy: accuracyPercent(a.totalCorrectCount, a.totalQuestionsSolved),
      },
      {
        ...this.metric('totalTimeSeconds', 'activity.students.time', null),
        duration: formatActivityDuration(a.totalTimeSeconds),
      },
    ];
  });

  /** Backend'in sırası (çözülen soru azalan) korunur; UI yeniden sıralamaz. */
  readonly topStudents = computed<TeacherActiveStudent[]>(() => this.studentsActivity()?.topStudents ?? []);
  readonly topStudentsEmpty = computed(() => this.topStudents().length === 0);

  readonly topStudentsDisplayedColumns: readonly string[] = [
    'rank',
    'studentName',
    'questionsSolved',
    'correctCount',
    'timeSeconds',
  ];

  constructor() {
    this.preloadScope();
  }

  ngOnInit(): void {
    this.loadSummary();
    this.loadWorksheetsOverview();
    this.loadLaggingStudents();
    this.loadOwnActivity();
    this.loadStudentsActivity();
  }

  loadOwnActivity(): void {
    this.ownActivityLoading.set(true);
    this.ownActivityError.set(null);

    this.teacherService
      .getOwnActivitySummary(this.activityDays)
      .pipe(finalize(() => this.ownActivityLoading.set(false)))
      .subscribe({
        next: (activity) => this.ownActivity.set(activity),
        error: () => {
          this.ownActivity.set(null);
          this.ownActivityError.set(this.text('activity.own.error'));
        },
      });
  }

  /** Hem "Öğrenci Aktivitesi" kartını hem "En Aktif Öğrenciler" tablosunu besler (tek response). */
  loadStudentsActivity(): void {
    this.studentsActivityLoading.set(true);
    this.studentsActivityError.set(null);

    this.teacherService
      .getStudentsActivitySummary(this.activityDays)
      .pipe(finalize(() => this.studentsActivityLoading.set(false)))
      .subscribe({
        next: (activity) => this.studentsActivity.set(activity),
        error: () => {
          this.studentsActivity.set(null);
          this.studentsActivityError.set(this.text('activity.students.error'));
        },
      });
  }

  /** Şablonda satır bazlı süre/doğruluk için; saf yardımcıların ince sarmalayıcıları. */
  durationOf(seconds: number): DurationLabel {
    return formatActivityDuration(seconds);
  }

  accuracyOf(row: TeacherActiveStudent): number | null {
    return accuracyPercent(row.correctCount, row.questionsSolved);
  }

  trackTopStudent(_index: number, row: TeacherActiveStudent): number {
    return row.studentId;
  }

  /** Maketteki sıra gösterimi: ilk üç sıra madalya, sonrası sıra numarası (1 tabanlı). */
  rankLabel(index: number): string {
    return RANK_MEDALS[index] ?? String(index + 1);
  }

  private metric(key: string, labelKey: string, value: number | null): ActivityMetricViewModel {
    return { key, labelKey, value, duration: null, accuracy: null };
  }

  loadLaggingStudents(): void {
    this.laggingStudentsLoading.set(true);
    this.laggingStudentsError.set(null);

    this.teacherService
      .getLaggingStudents()
      .pipe(finalize(() => this.laggingStudentsLoading.set(false)))
      .subscribe({
        next: (rows) => this.laggingStudents.set(rows),
        error: () => {
          this.laggingStudents.set([]);
          this.laggingStudentsError.set(this.text('lagging.error'));
        },
      });
  }

  /** Satır anahtarı: aynı öğrenci birden fazla worksheet'te geride kalabilir. */
  trackLaggingRow(_index: number, row: TeacherLaggingStudent): string {
    return `${row.studentId}-${row.worksheetId}`;
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
          this.error.set(this.text('summary.error'));
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
          this.worksheetsError.set(this.text('worksheets.error'));
        },
      });
  }

  /** ≥%50 başarı, <%50 uyarı chip'i. */
  completionClass(percentage: number): 'is-success' | 'is-warning' {
    return percentage >= COMPLETION_SUCCESS_THRESHOLD ? 'is-success' : 'is-warning';
  }

  /** Scope'a göreli anahtarı senkron çözer; sözlük şablon render edilirken yüklenmiş olur. */
  private text(key: string): string {
    return this.transloco.translate<string>(`${TEACHER_DASHBOARD_SCOPE}.${key}`) ?? '';
  }

  openWorksheet(row: TeacherWorksheetOverview): void {
    void this.router.navigate(['/test', row.worksheetId]);
  }

  /**
   * Şablon dışı metinler (snackbar, dialog, hata mesajı) senkron `translate()` ile okunur;
   * sözlük şablon render edilmeden de hazır olsun diye scope burada yüklenir.
   */
  private preloadScope(): void {
    this.transloco
      .load(`${TEACHER_DASHBOARD_SCOPE}/${this.transloco.getActiveLang()}`)
      .pipe(take(1))
      .subscribe();
  }
}
