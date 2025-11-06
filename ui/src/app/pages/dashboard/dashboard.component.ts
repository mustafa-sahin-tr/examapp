import { CommonModule } from '@angular/common';
import { Component, ElementRef, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { SectionHeaderComponent } from '../../shared/components/section-header/section-header.component';
import { WorksheetCardComponent } from '../worksheet-card/worksheet-card.component';
import { TestService } from '../../services/test.service';
import { AssignedWorksheet } from '../../models/assignment';
import { Test } from '../../models/test-instance';
import { NgxChartsModule, Color, ScaleType } from '@swimlane/ngx-charts';
import { BadgeService, UserActivityResponse } from '../../services/badge.service';
import { finalize } from 'rxjs';

interface AssignmentCardViewModel {
  assignment: AssignedWorksheet;
  test: Test;
}

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, SectionHeaderComponent, WorksheetCardComponent, NgxChartsModule],
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.scss'],
})
export class DashboardComponent implements OnInit {
  private readonly testService = inject(TestService);
  private readonly router = inject(Router);
  private readonly badgeService = inject(BadgeService);

  private readonly assignments = signal<AssignmentCardViewModel[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly activityDataFromApi = signal<
    Array<{ name: string; series: Array<{ name: string; value: number; extra?: any }> }>
  >([]);
  readonly activityApiLoading = signal(false);
  readonly activityApiError = signal(false);

  readonly assignmentCards = computed(() => this.assignments());

  readonly scrollDistance = 600;
  private readonly demoActivityUserId = 16;
  private activityLastMonthDisplayed = '';

  @ViewChild('assignmentContainer', { static: false }) assignmentContainer?: ElementRef<HTMLDivElement>;

  readonly activityColorScheme: Color = {
    name: 'sunset',
    selectable: false,
    group: ScaleType.Linear,
    domain: ['#FFE5B4', '#FFB370', '#FF7F3F', '#E84F3B', '#A6282E'],
  };

  ngOnInit(): void {
    this.testService.getActiveAssignments().subscribe({
      next: (assignments) => {
        const viewModels = assignments.map((assignment) => ({
          assignment,
          test: this.mapToTest(assignment),
        }));
        this.assignments.set(viewModels);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Atanmış testler alınırken bir sorun oluştu.');
        this.loading.set(false);
      },
    });

    const resolvedUserId = this.getUserIdFromLocalStorage() ?? this.demoActivityUserId;
    this.loadUserActivityHeatmap(resolvedUserId);
  }

  trackAssignment(index: number, item: AssignmentCardViewModel): number {
    return item.assignment.assignmentId;
  }

  onCardClick(assignment: AssignmentCardViewModel): void {
    this.router.navigate(['/test', assignment.assignment.worksheetId]);
  }

  handleLeftNavigation(): void {
    if (!this.assignmentContainer) {
      return;
    }
    const container = this.assignmentContainer.nativeElement;
    container.scrollBy({ left: -this.scrollDistance, behavior: 'smooth' });
  }

  handleRightNavigation(): void {
    if (!this.assignmentContainer) {
      return;
    }
    const container = this.assignmentContainer.nativeElement;
    container.scrollBy({ left: this.scrollDistance, behavior: 'smooth' });
  }

  getDueLabel(assignment: AssignedWorksheet): string | null {
    if (!assignment.endAt) {
      return null;
    }
    const dueDate = new Date(assignment.endAt);
    return `Bitiş: ${dueDate.toLocaleDateString()}`;
  }

  activityXAxisTickFormatting = (value: string): string => {
    const parts = value.trim().split(' ');
    if (parts.length < 2) {
      return value;
    }

    const monthName = parts[1];
    if (this.activityLastMonthDisplayed === monthName) {
      return '';
    }

    this.activityLastMonthDisplayed = monthName;
    return monthName;
  };

  activityHeatmapTooltip = (tooltip: any): string => {
    if (!tooltip) {
      return '';
    }

    const cell = tooltip.cell ?? tooltip.data ?? tooltip;
    const extra = cell?.extra ?? {};
    const date = extra.date ? new Date(extra.date) : new Date();
    const dateLabel = extra.label ?? date.toLocaleDateString('tr-TR', { day: '2-digit', month: 'short' });
    const duration = this.formatDuration(extra.totalTimeSeconds ?? 0);

    return `${dateLabel}\nSoru: ${extra.questionCount ?? 0}\nDoğru: ${
      extra.correctCount ?? 0
    }\nSüre: ${duration}\nAktivite Skoru: ${cell?.value ?? 0}`;
  };

  private mapToTest(assignment: AssignedWorksheet): Test {
    return {
      id: assignment.worksheetId,
      name: assignment.name,
      description: assignment.description,
      gradeId: assignment.gradeId,
      maxDurationSeconds: assignment.maxDurationSeconds,
      isPracticeTest: assignment.isPracticeTest,
      imageUrl: assignment.imageUrl ?? undefined,
      subtitle: assignment.subtitle ?? undefined,
      badgeText: assignment.badgeText ?? undefined,
      bookId: assignment.bookId ?? undefined,
      bookTestId: assignment.bookTestId ?? undefined,
      questionCount: assignment.questionCount,
      subjectId: assignment.subjectId ?? undefined,
      topicId: assignment.topicId ?? undefined,
      subtopicId: assignment.subTopicId ?? undefined,
    };
  }

  private loadUserActivityHeatmap(userId: number): void {
    if (!userId || userId <= 0) {
      return;
    }

    this.activityApiLoading.set(true);
    this.activityApiError.set(false);

    this.badgeService
      .getUserActivity(userId)
      .pipe(finalize(() => this.activityApiLoading.set(false)))
      .subscribe({
        next: (response) => {
          this.activityLastMonthDisplayed = '';
          this.activityDataFromApi.set(this.transformActivityToHeatmap(response));
        },
        error: (error) => {
          console.error('Öğrenci aktivite verisi alınamadı', error);
          this.activityApiError.set(true);
          this.activityDataFromApi.set([]);
        },
      });
  }

  private transformActivityToHeatmap(response: UserActivityResponse): Array<{ name: string; series: any[] }> {
    const totalWeeks = 52;
    const daysPerWeek = 7;

    const rawEndDate = response?.endDateUtc ? this.normalizeDate(response.endDateUtc) : this.normalizeDate(new Date());
    const alignedEndDate = this.endOfWeek(rawEndDate);
    const alignedStartDate = this.addDays(alignedEndDate, -(totalWeeks * daysPerWeek - 1));

    const activityMap = new Map<string, (typeof response.days)[number]>();
    (response?.days ?? []).forEach((day) => {
      activityMap.set(this.toIsoDateKey(this.normalizeDate(day.dateUtc)), day);
    });

    const heatmap: Array<{ name: string; series: any[] }> = [];

    for (let weekIndex = 0; weekIndex < totalWeeks; weekIndex++) {
      const weekStart = this.addDays(alignedStartDate, weekIndex * daysPerWeek);
      const weekSeries = Array.from({ length: daysPerWeek }, (_, dayOffset) => {
        const currentDate = this.addDays(weekStart, dayOffset);
        const key = this.toIsoDateKey(currentDate);
        const dayActivity = activityMap.get(key);
        const activityScore = dayActivity?.activityScore ?? 0;

        if (currentDate > rawEndDate) {
          return null;
        }

        return {
          name: this.formatDayLabel(currentDate),
          value: activityScore,
          extra: {
            date: currentDate.toISOString(),
            label: currentDate.toLocaleDateString('tr-TR', { day: '2-digit', month: 'short' }),
            questionCount: dayActivity?.questionCount ?? 0,
            correctCount: dayActivity?.correctCount ?? 0,
            totalTimeSeconds: dayActivity?.totalTimeSeconds ?? 0,
            totalPoints: dayActivity?.totalPoints ?? 0,
          },
        };
      }).filter((entry): entry is { name: string; value: number; extra: any } => entry !== null);

      heatmap.push({
        name: this.formatWeekLabel(weekStart),
        series: weekSeries.slice().reverse(),
      });
    }

    return heatmap;
  }

  private addDays(date: Date, days: number): Date {
    const result = new Date(date);
    result.setDate(result.getDate() + days);
    result.setHours(0, 0, 0, 0);
    return result;
  }

  private normalizeDate(date: string | Date): Date {
    const normalized = new Date(date);
    normalized.setHours(0, 0, 0, 0);
    return normalized;
  }

  private toIsoDateKey(date: Date): string {
    const utc = Date.UTC(date.getFullYear(), date.getMonth(), date.getDate());
    return new Date(utc).toISOString().split('T')[0];
  }

  private formatDayLabel(date: Date): string {
    return date.toLocaleDateString('en-US', { weekday: 'short' });
  }

  private formatWeekLabel(weekStart: Date): string {
    return weekStart.toLocaleDateString('tr-TR', { day: '2-digit', month: 'short' });
  }

  private formatDuration(totalSeconds: number): string {
    if (!totalSeconds) {
      return '0 sn';
    }

    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;

    if (minutes && seconds) {
      return `${minutes} dk ${seconds} sn`;
    }

    if (minutes) {
      return `${minutes} dk`;
    }

    return `${seconds} sn`;
  }

  private endOfWeek(date: Date): Date {
    const target = new Date(date);
    const distanceToSunday = (7 - target.getDay()) % 7;
    return this.addDays(target, distanceToSunday);
  }

  private getUserIdFromLocalStorage(): number | null {
    if (typeof window === 'undefined') {
      return null;
    }

    try {
      const stored = window.localStorage.getItem('user');
      if (!stored) {
        return null;
      }

      const parsed = JSON.parse(stored);
      const userId = Number(parsed?.id);
      return Number.isFinite(userId) && userId > 0 ? userId : null;
    } catch (error) {
      console.warn('DashboardComponent: localStorage user verisi okunamadı', error);
      return null;
    }
  }
}
