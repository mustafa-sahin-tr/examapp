import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, OutputRefSubscription, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { COACH_INACTIVE_DAYS, COACH_MANY_MISTAKES, CoachStudent } from '../../models/coach.model';
import { CoachGoalCounts, CoachStudentComponent } from './coach-student.component';
import { CoachService } from '../../services/coach.service';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';

export interface CoachSignal {
  key: string;
  params?: Record<string, number>;
  warn: boolean;
}

/** `yyyy-MM-dd` günleri arasındaki takvim günü farkı (saat dilimi kaymasın diye UTC'de). */
export function daysBetween(from: string, to: string): number {
  return Math.round((Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000);
}

/** Tarayıcının yerel bugünü `yyyy-MM-dd`. */
export function localToday(now = new Date()): string {
  const pad = (n: number): string => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

/** Sunucunun `NeedsAttention` kuralı; hedef sayısı panelde değişince satır yeniden hesaplanır. */
export function needsAttention(student: CoachStudent, today: string): boolean {
  const inactive = student.lastActiveDay === null || daysBetween(student.lastActiveDay, today) >= COACH_INACTIVE_DAYS;
  return inactive || (student.mistakeCount ?? 0) >= COACH_MANY_MISTAKES || student.goalCount === 0;
}

/** Issue #893: satırdaki sinyaller — son etkinlik, çözülmemiş yanlış, bu haftanın hedefleri; eşik aşanlar uyarı renginde. */
export function coachSignals(student: CoachStudent, today: string): CoachSignal[] {
  const signals: CoachSignal[] = [];
  if (student.lastActiveDay === null) {
    signals.push({ key: 'neverActive', warn: true });
  } else {
    const days = Math.max(0, daysBetween(student.lastActiveDay, today));
    if (days === 0) signals.push({ key: 'activeToday', warn: false });
    else if (days === 1) signals.push({ key: 'activeYesterday', warn: false });
    else if (days >= COACH_INACTIVE_DAYS) signals.push({ key: 'inactiveDays', params: { days }, warn: true });
    else signals.push({ key: 'activeDaysAgo', params: { days }, warn: false });
  }
  if (student.mistakeCount) {
    signals.push({ key: 'mistakes', params: { count: student.mistakeCount }, warn: student.mistakeCount >= COACH_MANY_MISTAKES });
  }
  signals.push(
    student.goalCount === 0
      ? { key: 'noGoals', warn: true }
      : { key: 'goals', params: { done: student.goalsDone, count: student.goalCount }, warn: false },
  );
  return signals;
}

/**
 * Issue #700: koçun öğrencileri (kendi okulu + onaylı özel ders). Sıra ("dikkat isteyen önce", #893) ve kapsam sunucudan
 * gelir. Issue #893: iki panel — seçili öğrenci sağdaki `<router-outlet>`'te açılır; dar ekranda tek panel görünür.
 */
@Component({
  selector: 'app-coach-students',
  standalone: true,
  imports: [
    PageHeaderComponent,
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope('coach')],
  templateUrl: './coach-students.component.html',
  styleUrl: './coach-students.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CoachStudentsComponent implements OnInit {
  private readonly coach = inject(CoachService);
  private readonly destroyRef = inject(DestroyRef);

  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly students = signal<CoachStudent[]>([]);
  readonly query = signal('');
  /** Sağ panelde öğrenci açık mı (dar ekranda liste gizlenir). */
  readonly hasSelection = signal(false);
  private readonly today = localToday();
  private goalSubscription: OutputRefSubscription | null = null;

  readonly filtered = computed(() => {
    const query = this.query().trim().toLocaleLowerCase();
    const list = query ? this.students().filter((s) => s.fullName.toLocaleLowerCase().includes(query)) : this.students();
    return list.map((student) => ({ student, signals: coachSignals(student, this.today) }));
  });

  /** Sağ paneldeki öğrenci sayfasının hedef değişikliklerini satıra yansıtır; sıra değişmez (göz önündeki satır kaymasın). */
  onActivate(component: unknown): void {
    this.hasSelection.set(true);
    this.goalSubscription?.unsubscribe();
    if (component instanceof CoachStudentComponent) {
      this.goalSubscription = component.currentWeekGoalsChanged.subscribe((counts) => this.applyGoalCounts(counts));
    }
  }

  onDeactivate(): void {
    this.hasSelection.set(false);
    this.goalSubscription?.unsubscribe();
    this.goalSubscription = null;
  }

  private applyGoalCounts({ studentId, goalCount, goalsDone }: CoachGoalCounts): void {
    this.students.update((list) =>
      list.map((s) => {
        if (s.id !== studentId) return s;
        const updated = { ...s, goalCount, goalsDone };
        return { ...updated, needsAttention: needsAttention(updated, this.today) };
      }),
    );
  }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.coach
      .listStudents()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (students) => {
          this.students.set(students);
          this.loading.set(false);
        },
        error: () => {
          this.loadFailed.set(true);
          this.loading.set(false);
        },
      });
  }
}
