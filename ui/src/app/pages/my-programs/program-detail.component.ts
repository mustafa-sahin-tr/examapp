import { Component, OnInit, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { CommonModule } from '@angular/common';
import { NgxChartsModule, Color, ScaleType } from '@swimlane/ngx-charts';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatFormFieldModule } from '@angular/material/form-field';
import { FormsModule } from '@angular/forms';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { ProgramService } from '../../services/program.service';
import { StudyPageService } from '../../services/study-page.service';
import {
  ProgramStudyPageScheduleItem,
  UserProgram,
  UserProgramStudyPageSchedule,
} from '../../models/program.interfaces';
import { StudyPage } from '../../models/study-page';
import { AddStudyPagesDialogComponent } from './add-study-pages-dialog.component';
import { ScheduleDetailDialogComponent } from './schedule-detail-dialog.component';

interface ProgramDayDetail {
  date: string;
  done: boolean;
  solved: number;
  minutes: number;
  subjects: { name: string; solved: number; minutes: number }[];
  notes?: string;
  mood?: string;
}

interface ScheduleBar {
  schedule: UserProgramStudyPageSchedule;
  startCol: number;
  endCol: number;
  weekRow: number;
  stack: number;
  offsetY: number;
  color: string;
}

@Component({
  selector: 'app-program-detail',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatIconModule,
    NgxChartsModule,
    MatProgressBarModule,
    MatFormFieldModule,
    MatInputModule,
    FormsModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    MatButtonModule,
    MatDatepickerModule,
    MatNativeDateModule,
    MatSnackBarModule,
    MatCheckboxModule,
    MatDialogModule,
    MatButtonToggleModule,
  ],
  templateUrl: './program-detail.component.html',
  styleUrls: ['./program-detail.component.scss'],
})
export class ProgramDetailComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private programService = inject(ProgramService);
  private studyPageService = inject(StudyPageService);
  private snackBar = inject(MatSnackBar);
  private dialog = inject(MatDialog);

  programId: string | null = null;
  userProgram: UserProgram | null = null;
  calendarSize: 'small' | 'medium' | 'large' = 'medium';

  availableStudyPages: Array<StudyPage & { selected?: boolean; startDate?: Date | null; endDate?: Date | null }> = [];

  weekStart = this.getWeekStart(new Date());
  currentMonth = new Date();

  // Color mapping for subjects/study-pages
  subjectColors: { [key: string]: string } = {
    Matematik: '#3b82f6', // blue
    Fizik: '#ef4444', // red
    Kimya: '#10b981', // green
    Biyoloji: '#f59e0b', // amber
    Türkçe: '#8b5cf6', // purple
    Tarih: '#ec4899', // pink
    Coğrafya: '#14b8a6', // teal
    İngilizce: '#f97316', // orange
  };

  // Gelişmiş mock veri
  program = {
    programName: 'TYT Deneme Programı',
    startDate: '2025-05-01',
    endDate: '2025-05-30',
    studyType: 'question',
    studyDuration: '25-5',
    questionsPerDay: 12,
    subjectsPerDay: 2,
    days: [
      {
        date: '2025-05-01',
        done: true,
        solved: 12,
        minutes: 60,
        subjects: [
          { name: 'Matematik', solved: 6, minutes: 30 },
          { name: 'Fizik', solved: 6, minutes: 30 },
        ],
        notes: 'Harika başladım!',
        mood: '😃',
      },
      {
        date: '2025-05-02',
        done: false,
        solved: 8,
        minutes: 35,
        subjects: [
          { name: 'Matematik', solved: 4, minutes: 20 },
          { name: 'Fizik', solved: 4, minutes: 15 },
        ],
        notes: 'Biraz yorgundum.',
        mood: '😐',
      },
      {
        date: '2025-05-03',
        done: true,
        solved: 15,
        minutes: 70,
        subjects: [
          { name: 'Matematik', solved: 7, minutes: 35 },
          { name: 'Fizik', solved: 8, minutes: 35 },
        ],
        notes: 'Rekor kırdım!',
        mood: '🔥',
      },
      { date: '2025-05-04', done: false, solved: 0, minutes: 0, subjects: [], notes: 'Çalışmadım.', mood: '😞' },
      {
        date: '2025-05-05',
        done: true,
        solved: 12,
        minutes: 60,
        subjects: [
          { name: 'Matematik', solved: 6, minutes: 30 },
          { name: 'Fizik', solved: 6, minutes: 30 },
        ],
        notes: '',
        mood: '🙂',
      },
      {
        date: '2025-05-06',
        done: true,
        solved: 14,
        minutes: 65,
        subjects: [
          { name: 'Matematik', solved: 7, minutes: 35 },
          { name: 'Fizik', solved: 7, minutes: 30 },
        ],
        notes: 'İyi bir gündü.',
        mood: '😃',
      },
      {
        date: '2025-05-07',
        done: true,
        solved: 10,
        minutes: 50,
        subjects: [
          { name: 'Matematik', solved: 5, minutes: 25 },
          { name: 'Fizik', solved: 5, minutes: 25 },
        ],
        notes: '',
        mood: '🙂',
      },
      {
        date: '2025-05-08',
        done: true,
        solved: 16,
        minutes: 80,
        subjects: [
          { name: 'Matematik', solved: 8, minutes: 40 },
          { name: 'Fizik', solved: 8, minutes: 40 },
        ],
        notes: 'Çok verimli!',
        mood: '😃',
      },
      {
        date: '2025-05-09',
        done: false,
        solved: 6,
        minutes: 20,
        subjects: [
          { name: 'Matematik', solved: 3, minutes: 10 },
          { name: 'Fizik', solved: 3, minutes: 10 },
        ],
        notes: 'Düşüşteyim.',
        mood: '😞',
      },
      {
        date: '2025-05-10',
        done: true,
        solved: 13,
        minutes: 65,
        subjects: [
          { name: 'Matematik', solved: 7, minutes: 35 },
          { name: 'Fizik', solved: 6, minutes: 30 },
        ],
        notes: '',
        mood: '🙂',
      },
      // ...daha fazla gün eklenebilir
    ] as ProgramDayDetail[],
  };

  selectedDay: ProgramDayDetail | null = null;

  carouselIndex = 0;
  daysPerView = 7; // Kaç gün bir arada görünsün (responsive için ayarlanabilir)
  get maxCarouselIndex() {
    return Math.max(0, this.program.days.length - this.daysPerView);
  }

  constructor() {
    this.programId = this.route.snapshot.paramMap.get('id');
  }

  ngOnInit(): void {
    const idValue = Number(this.programId);
    if (Number.isNaN(idValue)) {
      return;
    }

    this.loadProgram(idValue);
    this.loadStudyPages();
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
    return this.currentMonth.toLocaleDateString('tr-TR', { month: 'long', year: 'numeric' });
  }

  get nextMonthLabel(): string {
    const next = new Date(this.currentMonth);
    next.setMonth(next.getMonth() + 1);
    return next.toLocaleDateString('tr-TR', { month: 'long', year: 'numeric' });
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

    this.userProgram.studyPageSchedules.forEach((schedule) => {
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
        });
      }
    });

    // Calculate stack positions per week row
    this.assignStackPositions(bars);

    return bars;
  }

  private getScheduleColor(schedule: UserProgramStudyPageSchedule): string {
    for (const [subject, color] of Object.entries(this.subjectColors)) {
      if (schedule.studyPageTitle?.toLowerCase().includes(subject.toLowerCase())) {
        return color;
      }
    }
    return '#6366f1'; // Default indigo
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

  getDayColor(date: Date | null): string {
    if (!date) return 'transparent';

    const schedules = this.getSchedulesForDay(date);
    if (schedules.length === 0) return 'rgba(15, 23, 42, 0.04)';

    // Collect colors for all schedules
    const colors: string[] = [];
    schedules.forEach((schedule) => {
      let colorFound = false;
      for (const [subject, color] of Object.entries(this.subjectColors)) {
        if (schedule.studyPageTitle?.toLowerCase().includes(subject.toLowerCase())) {
          colors.push(color);
          colorFound = true;
          break;
        }
      }
      if (!colorFound) {
        colors.push('#6366f1'); // Default indigo color
      }
    });

    // If multiple colors, use first one (or could create gradient)
    // For gradient: return `linear-gradient(135deg, ${colors.join(', ')})`;
    return colors[0];
  }

  getDayGradient(date: Date | null): string {
    if (!date) return 'transparent';

    const schedules = this.getSchedulesForDay(date);
    if (schedules.length === 0) return 'rgba(15, 23, 42, 0.04)';

    const colors: string[] = [];
    schedules.forEach((schedule) => {
      let colorFound = false;
      for (const [subject, color] of Object.entries(this.subjectColors)) {
        if (schedule.studyPageTitle?.toLowerCase().includes(subject.toLowerCase())) {
          colors.push(color);
          colorFound = true;
          break;
        }
      }
      if (!colorFound) {
        colors.push('#6366f1');
      }
    });

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
        availableStudyPages: this.availableStudyPages.map((p) => ({
          ...p,
          selected: false,
          startDate: null,
          endDate: null,
        })),
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
      this.snackBar.open('En az bir calisma sayfasi secmelisiniz.', 'Tamam', { duration: 2000 });
      return;
    }

    const items: ProgramStudyPageScheduleItem[] = selectedPages.map((p) => ({
      studyPageId: p.id,
      startDate: p.startDate.toISOString(),
      endDate: p.endDate.toISOString(),
    }));

    this.programService.addStudyPages(this.userProgram.id, { items }).subscribe({
      next: (program) => {
        this.userProgram = program;
        this.snackBar.open('Calisma sayfalari programa eklendi.', 'Tamam', { duration: 2000 });
      },
      error: () => {
        this.snackBar.open('Program guncellenemedi.', 'Tamam', { duration: 3000 });
      },
    });
  }

  getSchedulesForDay(day: Date) {
    if (!this.userProgram) return [];
    return this.userProgram.studyPageSchedules.filter((s) => {
      const start = new Date(s.startDate);
      const end = new Date(s.endDate);
      return start <= day && end >= day;
    });
  }

  formatDayLabel(day: Date): string {
    return day.toLocaleDateString('tr-TR', { weekday: 'short', day: '2-digit', month: 'short' });
  }

  private loadProgram(programId: number): void {
    this.programService.getProgramById(programId).subscribe({
      next: (program) => {
        this.userProgram = program;
      },
      error: () => {
        this.snackBar.open('Program bilgisi yuklenemedi.', 'Tamam', { duration: 3000 });
      },
    });
  }

  private loadStudyPages(): void {
    this.studyPageService.getPaged({ pageNumber: 1, pageSize: 200 }).subscribe({
      next: (result) => {
        console.log('Study pages loaded from API:', result);
        this.availableStudyPages = result.items.map((page) => ({
          ...page,
          selected: false,
          startDate: null,
          endDate: null,
        }));
        console.log('Available study pages after mapping:', this.availableStudyPages);
      },
      error: (err) => {
        console.error('Error loading study pages:', err);
        this.snackBar.open("Study-page'ler yuklenemedi.", 'Tamam', { duration: 3000 });
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
    return date.toLocaleDateString('tr-TR', { day: '2-digit', month: 'short' });
  }

  get totalDays() {
    return this.program.days.length;
  }
  get completedDays() {
    return this.program.days.filter((d) => d.done).length;
  }
  get missedDays() {
    return this.program.days.filter((d) => !d.done).length;
  }

  get visibleDays() {
    return this.program.days.slice(this.carouselIndex, this.carouselIndex + this.daysPerView);
  }

  selectDay(day: ProgramDayDetail) {
    this.selectedDay = day;
  }
  closeDayDetail() {
    this.selectedDay = null;
  }

  scrollDays(direction: number) {
    this.carouselIndex = Math.min(Math.max(0, this.carouselIndex + direction), this.maxCarouselIndex);
    // Scroll işlemi için DOM'a erişim gerekirse ViewChild ile eklenebilir
  }

  getRealIndex(i: number) {
    return this.carouselIndex + i;
  }

  // Modern istatistikler için ek mock veriler ve hesaplamalar
  get totalSolved() {
    return this.program.days.reduce((sum, d) => sum + d.solved, 0);
  }
  get totalMinutes() {
    return this.program.days.reduce((sum, d) => sum + d.minutes, 0);
  }
  get successRate() {
    return this.program.days.length > 0 ? Math.round((this.completedDays / this.program.days.length) * 100) : 0;
  }

  // ngx-charts heatmap için örnek veri ve renk şeması
  heatmapData = [
    {
      name: 'Hafta 1',
      series: [
        { name: 'Pzt', value: 12 },
        { name: 'Sal', value: 8 },
        { name: 'Çar', value: 12 },
        { name: 'Per', value: 0 },
        { name: 'Cum', value: 12 },
        { name: 'Cmt', value: 10 },
        { name: 'Paz', value: 0 },
      ],
    },
    {
      name: 'Hafta 2',
      series: [
        { name: 'Pzt', value: 10 },
        { name: 'Sal', value: 12 },
        { name: 'Çar', value: 12 },
        { name: 'Per', value: 12 },
        { name: 'Cum', value: 8 },
        { name: 'Cmt', value: 12 },
        { name: 'Paz', value: 0 },
      ],
    },
    // ...devamı
  ];
  heatmapScheme: Color = {
    name: 'heatmapScheme',
    selectable: false,
    group: ScaleType.Quantile,
    domain: ['#e0f7fa', '#80deea', '#26c6da', '#00acc1', '#00838f', '#005662'],
  };

  // --- EN İYİ GÜN, EN KÖTÜ GÜN, STREAK ---
  get bestDay() {
    if (this.program.days.length === 0) return null;
    return this.program.days.reduce(
      (best: any, d: any) => (!best || d.solved > best.solved ? d : best),
      this.program.days[0]
    );
  }
  get worstDay() {
    const filtered = this.program.days.filter((d: any) => d.solved > 0);
    if (filtered.length === 0) return null;
    return filtered.reduce((worst: any, d: any) => (d.solved < worst.solved ? d : worst), filtered[0]);
  }
  get currentStreak() {
    let streak = 0;
    for (let i = this.program.days.length - 1; i >= 0; i--) {
      if (this.program.days[i].done) streak++;
      else break;
    }
    return streak;
  }
  get maxStreak() {
    let max = 0,
      cur = 0;
    for (const d of this.program.days) {
      if (d.done) cur++;
      else {
        max = Math.max(max, cur);
        cur = 0;
      }
    }
    return Math.max(max, cur);
  }

  // --- DERS BAZINDA İSTATİSTİKLER ---
  get subjectStats() {
    const stats: { [subject: string]: { solved: number; minutes: number } } = {};
    for (const d of this.program.days) {
      for (const s of d.subjects) {
        if (!stats[s.name]) stats[s.name] = { solved: 0, minutes: 0 };
        stats[s.name].solved += s.solved;
        stats[s.name].minutes += s.minutes;
      }
    }
    return Object.entries(stats).map(([name, v]) => ({ name, ...v }));
  }

  // --- PIE CHART DATA ---
  get subjectPieData() {
    return this.subjectStats.map((s) => ({ name: s.name, value: s.solved }));
  }
  subjectPieScheme: Color = {
    name: 'pieScheme',
    selectable: true,
    group: ScaleType.Ordinal,
    domain: ['#1976d2', '#43a047', '#ffb300', '#e53935', '#8e24aa'],
  };

  // --- GÜNLÜK FEEDBACK MESAJI ---
  getMotivation(day: any) {
    if (day.solved === 0) return 'Bugün çalışmadın, yarın telafi edebilirsin!';
    if (day.solved >= this.program.questionsPerDay * 1.2) return 'Mükemmel performans!';
    if (day.solved >= this.program.questionsPerDay) return 'Hedefini tutturdun, harikasın!';
    if (day.solved >= this.program.questionsPerDay * 0.7) return 'Fena değil, biraz daha gayret!';
    return 'Bugün hedefin altında kaldın.';
  }

  // Bar chart için gün bazında hedef ve gerçekleşen soru sayısı
  get barChartData() {
    return this.program.days.map((d, i) => ({
      name: `${i + 1}.Gün`,
      value: d.solved,
    }));
  }
  // En uzun süre günü
  get maxMinutesDay() {
    if (!this.program.days.length) return null;
    return this.program.days.reduce((max, d) => (d.minutes > (max?.minutes ?? 0) ? d : max), this.program.days[0]);
  }
  // En yüksek uyum oranı günü
  get maxRateDay() {
    if (!this.program.days.length) return null;
    return this.program.days.reduce(
      (max, d) => {
        const rate = d.solved / this.program.questionsPerDay;
        const maxRate = max.solved / this.program.questionsPerDay;
        return rate > maxRate ? { ...d, rate: Math.round(rate * 100) } : { ...max, rate: Math.round(maxRate * 100) };
      },
      { ...this.program.days[0], rate: Math.round((this.program.days[0].solved / this.program.questionsPerDay) * 100) }
    );
  }
  // Haftalık toplam soru ve süre (son 7 gün)
  get weeklyTotalSolved() {
    return this.program.days.slice(-7).reduce((sum, d) => sum + d.solved, 0);
  }
  get weeklyTotalMinutes() {
    return this.program.days.slice(-7).reduce((sum, d) => sum + d.minutes, 0);
  }

  // Radar chart için örnek veri
  get radarChartData() {
    // ngx-charts-polar-chart expects an array of objects with a 'name' and a 'series' array
    // Example:
    // [
    //   { name: 'Başarı', series: [
    //     { name: 'Matematik', value: 85 },
    //     { name: 'Türkçe', value: 60 }, ... ] }
    // ]
    return [
      {
        name: 'Başarı',
        series: [
          { name: 'Matematik', value: 85 },
          { name: 'Türkçe', value: 60 },
          { name: 'Fen', value: 40 },
          { name: 'Sosyal Bil.', value: 75 },
          { name: 'İngilizce', value: 90 },
        ],
      },
    ];
  }
}
