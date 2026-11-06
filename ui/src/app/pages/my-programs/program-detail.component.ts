import { Component, DestroyRef, OnInit, inject, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { combineLatest, take } from 'rxjs';
import { ActivatedRoute } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { ProgramService } from '../../services/program.service';
import {
  ProgramStudyPageScheduleItem,
  UserProgram,
  UserProgramStudyPageSchedule,
} from '../../models/program.interfaces';
import { StudyPage } from '../../models/study-page';
import { describeStudyItem, studyItemTypeIcon } from '../../shared/utils/study-item-display.util';
import { formatDateOnly } from '../../shared/utils/date-only.util';
import { AddStudyPagesDialogComponent } from './add-study-pages-dialog.component';
import { ScheduleDetailDialogComponent } from './schedule-detail-dialog.component';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { LocaleService } from '../../services/locale.service';
import { MY_PROGRAMS_SCOPE } from './my-programs-scope';
import { subjectColorFor } from '../../shared/utils/subject-color';

/** Sözlükte karşılığı olan program türleri; bunun dışındaki değer backend'den geldiği gibi gösterilir. */
const KNOWN_STUDY_TYPES = ['intensive', 'regular', 'flexible', 'weekend', 'exam', 'question'];

interface ScheduleBar {
  schedule: UserProgramStudyPageSchedule;
  startCol: number;
  endCol: number;
  weekRow: number;
  stack: number;
  offsetY: number;
  color: string;
  /** Etkinlik tipi rozeti (Görsel / Link / Kitap Sayfası) — bar üzerinde ikon, tooltip'te etiket. */
  typeIcon: string;
  typeLabel: string;
}

@Component({
  selector: 'app-program-detail',
  standalone: true,
  imports: [
    CommonModule,
    MatIconModule,
    FormsModule,
    MatTooltipModule,
    MatButtonModule,
    MatSnackBarModule,
    MatDialogModule,
    MatButtonToggleModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(MY_PROGRAMS_SCOPE)],
  templateUrl: './program-detail.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./program-detail.component.scss'],
})
export class ProgramDetailComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private programService = inject(ProgramService);
  private snackBar = inject(MatSnackBar);
  private dialog = inject(MatDialog);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);

  programId: string | null = null;
  userProgram: UserProgram | null = null;
  calendarSize: 'small' | 'medium' | 'large' = 'medium';

  weekStart = this.getWeekStart(new Date());
  currentMonth = new Date();


  constructor() {
    this.programId = this.route.snapshot.paramMap.get('id');
  }

  ngOnInit(): void {
    const idValue = Number(this.programId);
    if (Number.isNaN(idValue)) {
      return;
    }

    this.loadProgram(idValue);
  }

  get weekDays(): Date[] {
    const days: Date[] = [];
    for (let i = 0; i < 7; i += 1) {
      const day = new Date(this.weekStart);
      day.setDate(this.weekStart.getDate() + i);
      days.push(day);
    }
    return days;
  }

  get weekRangeLabel(): string {
    const start = this.weekStart;
    const end = new Date(this.weekStart);
    end.setDate(start.getDate() + 6);
    return `${this.formatShortDate(start)} - ${this.formatShortDate(end)}`;
  }

  get currentMonthLabel(): string {
    return this.currentMonth.toLocaleDateString(this.intlLocale, { month: 'long', year: 'numeric' });
  }

  get nextMonthLabel(): string {
    const next = new Date(this.currentMonth);
    next.setMonth(next.getMonth() + 1);
    return next.toLocaleDateString(this.intlLocale, { month: 'long', year: 'numeric' });
  }

  /**
   * Issue #386: başlıktaki program tarih aralığı — ham ISO yerine aktif dilde gün biçimi. Tarih-yalnız yorumlandığı
   * için UTC gece yarısı değerleri yerel saat diliminde önceki güne kaymaz. Tek uç okunabiliyorsa şablon yalnız onu
   * gösterir (tire yok); iki uç da okunamıyorsa `null` (gizli).
   */
  get dateRange(): { start: string; end: string } | null {
    const start = formatDateOnly(this.userProgram?.startDate, this.intlLocale);
    const end = formatDateOnly(this.userProgram?.endDate, this.intlLocale);
    return start || end ? { start, end } : null;
  }

  /** Intl çağrılarında kullanılacak aktif dil (issue #183). */
  private get intlLocale(): string {
    return this.localeService.localeDefinition().angularLocale;
  }

  /** Pazartesi başlangıçlı kısa gün adları — aktif dile göre üretilir. */
  get weekdayNames(): string[] {
    return this.buildWeekdayNames('short');
  }

  /** Mini takvimin tek harflik gün başlıkları. */
  get weekdayInitials(): string[] {
    return this.buildWeekdayNames('narrow');
  }

  /**
   * Program türünün okunur adı. Backend kod döndürür (`question`, `intensive`…); sözlükte
   * karşılığı yoksa ham değer gösterilir ki yeni bir tür sessizce kaybolmasın.
   */
  get studyTypeLabel(): string {
    const raw = this.userProgram?.studyType ?? '';
    const code = raw.toLowerCase();
    if (!KNOWN_STUDY_TYPES.includes(code)) {
      return raw;
    }
    return this.transloco.translate<string>(`${MY_PROGRAMS_SCOPE}.studyType.${code}`) ?? raw;
  }

  /** 2024-01-01 bir pazartesidir; haftanın yedi günü oradan türetilir. */
  private buildWeekdayNames(weekday: 'short' | 'narrow'): string[] {
    const format = new Intl.DateTimeFormat(this.intlLocale, { weekday });
    return Array.from({ length: 7 }, (_, i) => format.format(new Date(2024, 0, 1 + i)));
  }

  get monthCalendarDays(): (Date | null)[] {
    const year = this.currentMonth.getFullYear();
    const month = this.currentMonth.getMonth();
    const firstDay = new Date(year, month, 1);
    const lastDay = new Date(year, month + 1, 0);

    const days: (Date | null)[] = [];

    // Add empty cells for days before the 1st
    const firstDayOfWeek = firstDay.getDay();
    const startOffset = firstDayOfWeek === 0 ? 6 : firstDayOfWeek - 1; // Monday = 0
    for (let i = 0; i < startOffset; i++) {
      days.push(null);
    }

    // Add all days of the month
    for (let day = 1; day <= lastDay.getDate(); day++) {
      days.push(new Date(year, month, day));
    }

    return days;
  }

  get monthCalendarWeeks(): number {
    return Math.ceil(this.monthCalendarDays.length / 7);
  }

  get monthScheduleBars(): ScheduleBar[] {
    if (!this.userProgram) return [];

    const bars: ScheduleBar[] = [];
    const calendarDays = this.monthCalendarDays;

    if (calendarDays.length === 0) return bars;

    this.userProgram.studyItemSchedules.forEach((schedule) => {
      const scheduleStart = new Date(schedule.startDate);
      const scheduleEnd = new Date(schedule.endDate);

      // Normalize to start of day for comparison
      scheduleStart.setHours(0, 0, 0, 0);
      scheduleEnd.setHours(0, 0, 0, 0);

      const monthStartIndex = calendarDays.findIndex((d) => d !== null);
      const monthEndIndex = calendarDays.length - 1 - [...calendarDays].reverse().findIndex((d) => d !== null);

      if (monthStartIndex === -1 || monthEndIndex === -1) return;

      // Find start and end indices in the grid (including empty cells)
      let startIndex = -1;
      let endIndex = -1;

      for (let i = 0; i < calendarDays.length; i++) {
        const day = calendarDays[i];
        if (!day) continue;

        const dayDate = new Date(day);
        dayDate.setHours(0, 0, 0, 0);

        if (dayDate >= scheduleStart && startIndex === -1) {
          startIndex = i;
        }
        if (dayDate <= scheduleEnd) {
          endIndex = i;
        }
      }

      if (startIndex === -1 || endIndex === -1) return;

      // Clamp to month range
      startIndex = Math.max(startIndex, monthStartIndex);
      endIndex = Math.min(endIndex, monthEndIndex);

      const startRow = Math.floor(startIndex / 7);
      const endRow = Math.floor(endIndex / 7);
      const color = this.getScheduleColor(schedule);
      const typeIcon = studyItemTypeIcon(schedule.contentType);
      const typeLabel = this.studyItemTypeLabel(schedule);

      for (let row = startRow; row <= endRow; row++) {
        const rowStart = row * 7;
        const rowEnd = rowStart + 6;
        const segmentStart = Math.max(startIndex, rowStart);
        const segmentEnd = Math.min(endIndex, rowEnd);
        const startCol = (segmentStart % 7) + 1; // CSS grid is 1-indexed
        const endCol = (segmentEnd % 7) + 2; // span to include end day

        bars.push({
          schedule,
          startCol,
          endCol,
          weekRow: row,
          stack: 0,
          offsetY: 0,
          color,
          typeIcon,
          typeLabel,
        });
      }
    });

    // Calculate stack positions per week row
    this.assignStackPositions(bars);

    return bars;
  }

  private studyItemTypeLabel(schedule: UserProgramStudyPageSchedule): string {
    return describeStudyItem(schedule, (key, params) => this.transloco.translate<string>(key, params) ?? '').typeLabel;
  }

  private getScheduleColor(schedule: UserProgramStudyPageSchedule): string {
    // Ders adına göre takvim rengi (#725; harita shared/utils/subject-color).
    return subjectColorFor(schedule.studyItemTitle);
  }


  private assignStackPositions(bars: ScheduleBar[]): void {
    const barsByWeek = new Map<number, ScheduleBar[]>();

    bars.forEach((bar) => {
      if (!barsByWeek.has(bar.weekRow)) {
        barsByWeek.set(bar.weekRow, []);
      }
      barsByWeek.get(bar.weekRow)?.push(bar);
    });

    barsByWeek.forEach((weekBars) => {
      // Sort by start column, then by duration (longer first)
      weekBars.sort((a, b) => {
        if (a.startCol !== b.startCol) return a.startCol - b.startCol;
        return b.endCol - b.startCol - (a.endCol - a.startCol);
      });

      const stacks: Array<{ endCol: number }> = [];

      weekBars.forEach((bar) => {
        let assignedStack = -1;
        for (let i = 0; i < stacks.length; i++) {
          if (stacks[i].endCol < bar.startCol) {
            assignedStack = i;
            stacks[i].endCol = bar.endCol;
            break;
          }
        }

        if (assignedStack === -1) {
          assignedStack = stacks.length;
          stacks.push({ endCol: bar.endCol });
        }

        bar.stack = assignedStack;
        bar.offsetY = assignedStack * 26; // 24px bar height + 2px gap
      });
    });
  }

  get nextMonthCalendarDays(): (Date | null)[] {
    const next = new Date(this.currentMonth);
    next.setMonth(next.getMonth() + 1);
    const year = next.getFullYear();
    const month = next.getMonth();
    const firstDay = new Date(year, month, 1);
    const lastDay = new Date(year, month + 1, 0);

    const days: (Date | null)[] = [];

    const firstDayOfWeek = firstDay.getDay();
    const startOffset = firstDayOfWeek === 0 ? 6 : firstDayOfWeek - 1;
    for (let i = 0; i < startOffset; i++) {
      days.push(null);
    }

    for (let day = 1; day <= lastDay.getDate(); day++) {
      days.push(new Date(year, month, day));
    }

    return days;
  }

  previousMonth(): void {
    const prev = new Date(this.currentMonth);
    prev.setMonth(prev.getMonth() - 1);
    this.currentMonth = prev;
  }

  nextMonth(): void {
    const next = new Date(this.currentMonth);
    next.setMonth(next.getMonth() + 1);
    this.currentMonth = next;
  }

  getDayGradient(date: Date | null): string {
    if (!date) return 'transparent';

    // Planı olmayan gün satır içi renk almaz, temanın yüzeyini (SCSS) kullanır.
    const schedules = this.getSchedulesForDay(date);
    if (schedules.length === 0) return '';

    const colors = schedules.map((schedule) => this.getScheduleColor(schedule));

    if (colors.length === 1) {
      return colors[0];
    }

    // Create diagonal gradient for multiple subjects
    return `linear-gradient(135deg, ${colors.join(', ')})`;
  }

  previousWeek(): void {
    const next = new Date(this.weekStart);
    next.setDate(next.getDate() - 7);
    this.weekStart = this.getWeekStart(next);
  }

  nextWeek(): void {
    const next = new Date(this.weekStart);
    next.setDate(next.getDate() + 7);
    this.weekStart = this.getWeekStart(next);
  }

  toggleAddStudyPages(): void {
    this.openAddStudyPagesDialog(null);
  }

  onDayClick(date: Date | null, event?: MouseEvent): void {
    if (event) {
      event.stopPropagation();
    }
    if (!date) return;
    this.openAddStudyPagesDialog(date);
  }

  private openAddStudyPagesDialog(selectedDate: Date | null): void {
    const dialogRef = this.dialog.open(AddStudyPagesDialogComponent, {
      width: '800px',
      maxWidth: '90vw',
      data: {
        selectedDate: selectedDate,
      },
      panelClass: 'add-study-pages-dialog',
    });

    dialogRef.afterClosed().subscribe((result) => {
      if (result && result.selectedPages) {
        this.saveStudyPages(result.selectedPages);
      }
    });
  }

  onScheduleClick(bar: ScheduleBar, event?: MouseEvent): void {
    if (event) {
      event.stopPropagation();
    }

    this.dialog.open(ScheduleDetailDialogComponent, {
      width: '480px',
      maxWidth: '90vw',
      data: {
        schedule: bar.schedule,
        color: bar.color,
      },
    });
  }

  private saveStudyPages(
    selectedPages: Array<StudyPage & { selected: boolean; startDate: Date; endDate: Date }>
  ): void {
    if (!this.userProgram) return;

    if (!selectedPages.length) {
      this.notify('detail.selectAtLeastOnePage', 2000);
      return;
    }

    const items: ProgramStudyPageScheduleItem[] = selectedPages.map((p) => ({
      studyItemId: p.id,
      startDate: p.startDate.toISOString(),
      endDate: p.endDate.toISOString(),
    }));

    this.programService.addStudyPages(this.userProgram.id, { items }).subscribe({
      next: (program) => {
        this.userProgram = program;
        this.notify('detail.pagesAdded', 2000);
      },
      error: () => {
        this.notify('detail.updateFailed');
      },
    });
  }

  getSchedulesForDay(day: Date) {
    if (!this.userProgram) return [];
    return this.userProgram.studyItemSchedules.filter((s) => {
      const start = new Date(s.startDate);
      const end = new Date(s.endDate);
      return start <= day && end >= day;
    });
  }

  formatDayLabel(day: Date): string {
    return day.toLocaleDateString(this.intlLocale, { weekday: 'short', day: '2-digit', month: 'short' });
  }

  /**
   * Snackbar metni ve aksiyon etiketi şablon dışında üretildiği için ikisi de `selectTranslate`
   * ile okunur; bu çağrı scope sözlüğünü yükler ve dil değişiminde doğru metni verir.
   */
  private notify(messageKey: string, duration = 3000): void {
    combineLatest([
      this.transloco.selectTranslate<string>(messageKey, {}, MY_PROGRAMS_SCOPE),
      this.transloco.selectTranslate<string>('actions.ok', {}, MY_PROGRAMS_SCOPE),
    ])
      .pipe(take(1), takeUntilDestroyed(this.destroyRef))
      .subscribe(([message, action]) => {
        this.snackBar.open(message, action, { duration });
      });
  }

  private loadProgram(programId: number): void {
    this.programService.getProgramById(programId).subscribe({
      next: (program) => {
        this.userProgram = program;
      },
      error: () => {
        this.notify('detail.loadFailed');
      },
    });
  }

  private getWeekStart(date: Date): Date {
    const result = new Date(date);
    const day = result.getDay();
    const diff = (day === 0 ? -6 : 1) - day; // Monday start
    result.setDate(result.getDate() + diff);
    result.setHours(0, 0, 0, 0);
    return result;
  }

  private formatShortDate(date: Date): string {
    return date.toLocaleDateString(this.intlLocale, { day: '2-digit', month: 'short' });
  }
}
