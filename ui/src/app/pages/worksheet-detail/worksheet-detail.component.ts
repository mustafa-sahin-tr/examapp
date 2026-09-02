import { CommonModule } from '@angular/common';
import { Component, computed, DestroyRef, inject, Input, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute, NavigationExtras, Router } from '@angular/router';
import { Test, TestInstance, TestInstanceQuestion } from '../../models/test-instance';
import { lastValueFrom } from 'rxjs';
import { TestService } from '../../services/test.service';
import { AnswerChoice, QuestionRegion } from '../../models/draws';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar } from '@angular/material/snack-bar';
import { GradesService } from '../../services/grades.service';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatDialog } from '@angular/material/dialog';
import {
  AssignmentProgressSummary,
  AssignmentStudentStatus,
  TeacherAssignmentStudentSummary,
  TeacherWorksheetAssignment,
} from '../../models/assignment';
import { StudentService } from '../../services/student.service';
import { AuthService } from '../../services/auth.service';
import { Grade, StudentLookup } from '../../models/student';
import {
  WorksheetAssignmentDialogComponent,
  WorksheetAssignmentDialogResult,
} from './components/assignment-dialog/worksheet-assignment-dialog.component';
import { IsStudentDirective, IsTeacherDirective } from '../../shared/directives/is-student.directive';
import { QuestionCanvasViewComponent } from '../../shared/components/question-canvas-view/question-canvas-view.component';
import { QuestionNavigatorComponent } from '../../shared/components/question-navigator/question-navigator.component';
import { WorksheetAttempt, WorksheetDetail } from '../../models/worksheet-detail';

interface AssignmentPanelState {
  loading: boolean;
  assignments: TeacherWorksheetAssignment[];
  summary: AssignmentProgressSummary | null;
  lastRefreshed: Date | null;
  error: string | null;
}

type WorksheetView = 'teacher' | 'completed' | 'start';

@Component({
  selector: 'app-worksheet-detail',
  standalone: true,
  imports: [
    CommonModule,
    MatIconModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    MatExpansionModule,
    IsTeacherDirective,
    IsStudentDirective,
    QuestionCanvasViewComponent,
    QuestionNavigatorComponent,
  ],
  templateUrl: './worksheet-detail.component-dlms.html',
  styleUrls: ['./worksheet-detail.component-dlms.scss'],
})
export class WorksheetDetailComponent implements OnInit {
  private snackBar = inject(MatSnackBar);
  private dialog = inject(MatDialog);
  private authService = inject(AuthService);
  private studentService = inject(StudentService);
  private destroyRef = inject(DestroyRef);
  @Input() exam!: Test; // Test bilgisi ve sorular
  route = inject(ActivatedRoute);
  testService = inject(TestService);
  gradeService = inject(GradesService);
  router = inject(Router);
  results!: TestInstance;
  gradeName = signal<string>(''); // Sınıf adı
  private grades = signal<Grade[]>([]);
  private studentLookups = signal<StudentLookup[]>([]);
  protected readonly isTeacher = this.authService.hasRole('Teacher');
  private teacherPanelInitialized = false;
  protected readonly assignmentPanelState = signal<AssignmentPanelState>({
    loading: false,
    assignments: [],
    summary: null,
    lastRefreshed: null,
    error: null,
  });

  // Worksheet detail (yeni tasarım verisi)
  protected readonly detail = signal<WorksheetDetail | null>(null);
  protected readonly detailLoading = signal(false);
  protected readonly detailError = signal<string | null>(null);
  protected readonly fromMistakesLoading = signal(false);

  protected readonly completedResult = computed(() => this.detail()?.completedResult ?? null);

  protected readonly view = computed<WorksheetView>(() => {
    if (this.isTeacher) {
      return 'teacher';
    }
    return this.completedResult() ? 'completed' : 'start';
  });

  /** Denemelerin skorlarından türetilen sparkline path (viewBox 0 0 100 32). */
  protected readonly sparkline = computed(() => this.sparklinePath(this.detail()?.attempts ?? []));

  /** Skor halkası için stroke-dashoffset. */
  protected readonly donutDashoffset = computed(() => this.donutOffset(this.completedResult()?.scorePercent ?? 0));

  public regions = signal<QuestionRegion[]>([]); // Soru bölgeleri
  public selectedChoices = signal<Map<number, AnswerChoice>>(new Map());
  public correctChoices = signal<Map<number, AnswerChoice>>(new Map());
  testId!: number;
  questions: { status: 'correct' | 'incorrect' | 'unknown' }[] = [];
  public currentIndex = signal(0);

  StartTest(id: number | null) {
    if (id) {
      this.testService
        .startTest(id)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe((response) => {
        if (response.success) {
          this.router.navigate(['/testsolve', response.instanceId]);
        } else {
          this.snackBar.open(response.message, 'Tamam', { duration: 2000 });
        }
      });
    }
  }

  get instanceStatus(): number | undefined {
    return this.exam.instance?.status;
  }

  get instanceCompleted(): boolean {
    return this.exam.instance?.status === 1;
  }

  get instanceStarted(): boolean {
    return this.exam.instance?.status === 0;
  }

  editWorksheet(id: number | null) {
    if (id) {
      this.router.navigate(['/exam', id]);
    }
  }

  navigateToQuestionCanvas() {
    const navigationExtras: NavigationExtras = {
      state: {
        subjectId: null,
        topicId: null,
        subtopicId: null,
        testId: this.exam.subtitle,
        bookId: this.exam.bookId,
        bookTestId: this.exam.bookTestId,
        testValue: this.exam.id,
      },
    };

    setTimeout(() => {
      this.router.navigate(['/questioncanvas'], navigationExtras);
    }, 1000);
  }

  protected createFromMistakes(): void {
    const instanceId = this.completedResult()?.instanceId;
    if (!instanceId || this.fromMistakesLoading()) {
      return;
    }

    this.fromMistakesLoading.set(true);
    this.testService
      .createWorksheetFromMistakes(instanceId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.fromMistakesLoading.set(false);
          if (result?.worksheetId) {
            this.router.navigate(['/test', result.worksheetId]);
          }
        },
        error: (error) => {
          this.fromMistakesLoading.set(false);
          const message = error?.error?.message ?? 'Test oluşturulamadı.';
          this.snackBar.open(message, 'Tamam', { duration: 3000 });
        },
      });
  }

  protected openSimilarWorksheet(id: number): void {
    this.router.navigate(['/test', id]);
  }

  protected refreshAssignments(): void {
    this.loadAssignments();
  }

  formatDuration(totalSeconds: number): string {
    if (!totalSeconds || totalSeconds <= 0) {
      return '0 dakika';
    }

    const totalMinutes = Math.floor(totalSeconds / 60);
    const hours = Math.floor(totalMinutes / 60);
    const minutes = totalMinutes % 60;

    if (hours > 0 && minutes > 0) {
      return `${hours} saat ${minutes} dakika`;
    }

    if (hours > 0) {
      return `${hours} saat`;
    }

    return `${totalMinutes} dakika`;
  }

  protected formatMinutes(totalSeconds: number): string {
    const minutes = Math.round((totalSeconds ?? 0) / 60);
    return `${minutes} dk`;
  }

  protected formatDurationDetailed(totalSeconds: number): string {
    const safe = Math.max(0, Math.round(totalSeconds ?? 0));
    const minutes = Math.floor(safe / 60);
    const seconds = safe % 60;
    if (minutes > 0 && seconds > 0) {
      return `${minutes} dk ${seconds} sn`;
    }
    if (minutes > 0) {
      return `${minutes} dk`;
    }
    return `${seconds} sn`;
  }

  protected formatShortDate(value: string | null): string {
    if (!value) {
      return '—';
    }
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return '—';
    }
    return date.toLocaleDateString('tr-TR', { day: 'numeric', month: 'long' });
  }

  protected percentLabel(value: number | null | undefined): string {
    return value === null || value === undefined ? '—' : `%${value}`;
  }

  /** Donut halkası için stroke-dashoffset (r = 64, çevre ≈ 402). */
  protected readonly donutCircumference = 2 * Math.PI * 64;

  protected donutOffset(percent: number): number {
    const clamped = Math.min(100, Math.max(0, percent ?? 0));
    return this.donutCircumference * (1 - clamped / 100);
  }

  /** Denemelerin skorlarından bir sparkline path (viewBox 0 0 100 32). */
  protected sparklinePath(attempts: WorksheetAttempt[]): string {
    if (!attempts?.length) {
      return '';
    }
    const scores = [...attempts]
      .sort((a, b) => new Date(a.completedDate ?? 0).getTime() - new Date(b.completedDate ?? 0).getTime())
      .map((a) => a.scorePercent);
    if (scores.length === 1) {
      return 'M0 16 L100 16';
    }
    const step = 100 / (scores.length - 1);
    return scores
      .map((score, index) => {
        const x = index * step;
        const y = 30 - (Math.min(100, Math.max(0, score)) / 100) * 28;
        return `${index === 0 ? 'M' : 'L'}${x.toFixed(1)} ${y.toFixed(1)}`;
      })
      .join(' ');
  }

  protected openAssignmentDialog(scope: 'grade' | 'student'): void {
    if (!this.isTeacher || !this.exam?.id) {
      return;
    }

    const dialogRef = this.dialog.open(WorksheetAssignmentDialogComponent, {
      width: '520px',
      data: {
        worksheetId: this.exam.id,
        scope,
        grades: this.grades(),
        students: this.studentLookups(),
      },
    });

    dialogRef
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((result: WorksheetAssignmentDialogResult | undefined) => {
      if (!result) {
        return;
      }

      this.assignmentPanelState.update((state) => ({
        ...state,
        loading: true,
        error: null,
      }));

      this.testService
        .assignWorksheet(result.request)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
        next: (response) => {
          const success = response?.success ?? false;
          const message = response?.message ?? (success ? 'Atama oluşturuldu.' : 'Atama oluşturulamadı.');
          this.snackBar.open(message, 'Tamam', { duration: 3000 });
          if (success) {
            this.loadAssignments();
          } else {
            this.assignmentPanelState.update((state) => ({
              ...state,
              loading: false,
            }));
          }
        },
        error: (error) => {
          const message = error?.error?.message ?? 'Atama oluşturulurken bir hata oluştu.';
          this.snackBar.open(message, 'Kapat', { duration: 4000 });
          this.assignmentPanelState.update((state) => ({
            ...state,
            loading: false,
            error: message,
          }));
        },
      });
    });
  }

  protected statusLabel(status: AssignmentStudentStatus): string {
    switch (status) {
      case 'Completed':
        return 'Tamamladı';
      case 'InProgress':
        return 'Devam ediyor';
      case 'Scheduled':
        return 'Planlandı';
      case 'Expired':
        return 'Süresi doldu';
      default:
        return 'Başlamadı';
    }
  }

  protected statusClass(status: AssignmentStudentStatus): string {
    switch (status) {
      case 'Completed':
        return 'st done';
      case 'InProgress':
        return 'st prog';
      case 'Scheduled':
        return 'st wait';
      case 'Expired':
        return 'st late';
      default:
        return 'st wait';
    }
  }

  protected pendingCount(assignment: TeacherWorksheetAssignment): number {
    return assignment.notStartedCount + assignment.scheduledCount;
  }

  protected summaryPendingCount(summary: AssignmentProgressSummary | null): number {
    if (!summary) {
      return 0;
    }

    return summary.notStartedCount + summary.scheduledCount;
  }

  protected assignmentPanel(): 'idle' | 'loading' | 'error' | 'empty' | 'loaded' {
    const state = this.assignmentPanelState();

    if (state.loading) {
      return 'loading';
    }

    if (state.error) {
      return 'error';
    }

    if (!state.assignments.length) {
      return 'empty';
    }

    return 'loaded';
  }

  protected teacherAssignments(): TeacherWorksheetAssignment[] {
    return this.assignmentPanelState().assignments;
  }

  protected teacherSummary(): AssignmentProgressSummary | null {
    return this.assignmentPanelState().summary;
  }

  protected lastRefreshed(): Date | null {
    return this.assignmentPanelState().lastRefreshed;
  }

  protected teacherError(): string | null {
    return this.assignmentPanelState().error;
  }

  protected gradeNameById(gradeId?: number | null): string {
    if (!gradeId) {
      return '';
    }

    const grade = this.grades().find((g) => g.id === gradeId);
    return grade?.name ?? '';
  }

  protected studentGradeLabel(student: TeacherAssignmentStudentSummary): string {
    if (student.gradeName) {
      return student.gradeName;
    }

    return this.gradeNameById(student.gradeId);
  }

  protected lastActivityLabel(lastActivity?: string | null): string {
    if (!lastActivity) {
      return '—';
    }

    const date = new Date(lastActivity);
    if (Number.isNaN(date.getTime())) {
      return '—';
    }

    return date.toLocaleString('tr-TR', {
      dateStyle: 'short',
      timeStyle: 'short',
    });
  }

  private initializeTeacherPanel(): void {
    if (!this.isTeacher || this.teacherPanelInitialized) {
      return;
    }

    this.teacherPanelInitialized = true;
    this.loadStudentLookup();
    this.loadAssignments();
  }

  private loadAssignments(): void {
    if (!this.isTeacher || !this.testId) {
      return;
    }

    this.assignmentPanelState.update((state) => ({
      ...state,
      loading: true,
      error: null,
    }));

    this.testService
      .getWorksheetAssignmentsForTeacher(this.testId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
      next: (overview) => {
        const retrievedAt = overview?.retrievedAt ? new Date(overview.retrievedAt) : new Date();
        this.assignmentPanelState.set({
          loading: false,
          assignments: overview?.assignments ?? [],
          summary: overview?.summary ?? null,
          lastRefreshed: retrievedAt,
          error: null,
        });
      },
      error: (error) => {
        if (error?.status === 403) {
          this.assignmentPanelState.set({
            loading: false,
            assignments: [],
            summary: null,
            lastRefreshed: null,
            error: null,
          });
          return;
        }

        const message = error?.error?.message ?? 'Atama bilgileri getirilemedi.';
        this.assignmentPanelState.set({
          loading: false,
          assignments: [],
          summary: null,
          lastRefreshed: null,
          error: message,
        });
      },
    });
  }

  private loadStudentLookup(): void {
    if (!this.isTeacher) {
      return;
    }

    this.studentService
      .getLookup()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (students) => this.studentLookups.set(students ?? []),
        error: () => this.studentLookups.set([]),
      });
  }

  private loadDetail(): void {
    if (!this.testId) {
      return;
    }

    this.detailLoading.set(true);
    this.detailError.set(null);
    this.testService
      .getWorksheetDetail(this.testId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => {
          this.detail.set(detail ?? null);
          this.detailLoading.set(false);
          const completedInstanceId = detail?.completedResult?.instanceId;
          if (!this.isTeacher && completedInstanceId) {
            this.loadResultsForInstance(completedInstanceId);
          }
        },
        error: (error) => {
          this.detailLoading.set(false);
          this.detailError.set(error?.error?.message ?? 'Sınav detayları getirilemedi.');
        },
      });
  }

  /** "Tamamlandı" görünümündeki soru dökümünü completedResult.instanceId üzerinden yükler. */
  private loadResultsForInstance(instanceId: number): void {
    this.testService
      .getCanvasTestResults(instanceId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((response: TestInstance) => {
        if (!response) {
          return;
        }
        this.results = response;
        this.regions.set(this.testService.convertTestInstanceToRegions(this.results));

        const selected = new Map<number, AnswerChoice>();
        const correct = new Map<number, AnswerChoice>();
        this.results.testInstanceQuestions.forEach((q: TestInstanceQuestion) => {
          const region = this.regions().find((a) => a.id == q.question.id);
          if (q.selectedAnswerId) {
            const choice = region?.answers.find((a) => a.id === q.selectedAnswerId);
            if (choice) {
              selected.set(q.question.id, choice);
            }
          }
          const correctChoice = region?.answers.find((a) => a.id === q.question.correctAnswerId);
          if (correctChoice) {
            correct.set(q.question.id, correctChoice);
          }
        });
        this.selectedChoices.set(selected);
        this.correctChoices.set(correct);

        this.questions = this.results.testInstanceQuestions.map((tiq) => {
          if (tiq.selectedAnswerId === null) {
            return { status: 'unknown' };
          }
          return tiq.selectedAnswerId === tiq.question.correctAnswerId
            ? { status: 'correct' }
            : { status: 'incorrect' };
        });
      });
  }

  protected reloadDetail(): void {
    this.loadDetail();
  }

  questionSelected(index: number) {
    this.currentIndex.set(index);
  }

  protected trackByTopicId = (_: number, topic: { topicId: number | null }): number =>
    topic.topicId ?? -1;

  ngOnInit() {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(async (params) => {
      this.testId = Number(params.get('testId'));
      if (!this.testId) {
        return;
      }

      this.detail.set(null);
      this.regions.set([]);
      this.selectedChoices.set(new Map());
      this.correctChoices.set(new Map());
      this.questions = [];

      this.loadDetail();
      this.exam = await lastValueFrom(this.testService.get(this.testId));
      if (this.exam) {
        this.gradeService
          .getGrades()
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe((grades) => {
            this.grades.set(grades);
            const grade = grades.find((g) => g.id === this.exam.gradeId);
            this.gradeName.set(grade ? grade.name : 'Bilinmiyor');
          });

        if (this.isTeacher) {
          this.initializeTeacherPanel();
        }
      }
    });
  }
}
