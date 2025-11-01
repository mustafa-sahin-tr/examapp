import { CommonModule } from '@angular/common';
import { Component, inject, Input, OnInit, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute, NavigationExtras, Router } from '@angular/router';
import { Test, TestInstance, TestInstanceQuestion } from '../../models/test-instance';
import { lastValueFrom } from 'rxjs';
import { TestService } from '../../services/test.service';
import { QuestionNavigatorComponent } from '../../shared/components/question-navigator/question-navigator.component';
import { AnswerChoice, QuestionRegion } from '../../models/draws';
import { QuestionCanvasViewComponent } from '../../shared/components/question-canvas-view/question-canvas-view.component';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar } from '@angular/material/snack-bar';
import { IsStudentDirective, IsTeacherDirective } from '../../shared/directives/is-student.directive';
import { GradesService } from '../../services/grades.service';
import { MatChipsModule } from '@angular/material/chips';
import { MatTooltipModule } from '@angular/material/tooltip';
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

interface AssignmentPanelState {
  loading: boolean;
  assignments: TeacherWorksheetAssignment[];
  summary: AssignmentProgressSummary | null;
  lastRefreshed: Date | null;
  error: string | null;
}

@Component({
  selector: 'app-worksheet-detail',
  imports: [
    CommonModule,
    MatIconModule,
    QuestionNavigatorComponent,
    QuestionCanvasViewComponent,
    MatButtonModule,
    MatChipsModule,
    MatTooltipModule,
    MatProgressSpinnerModule,
    MatExpansionModule,
    IsStudentDirective,
    IsTeacherDirective,
  ],
  templateUrl: './worksheet-detail.component.html',
  styleUrl: './worksheet-detail.component.scss',
})
export class WorksheetDetailComponent implements OnInit {
  private snackBar = inject(MatSnackBar);
  private dialog = inject(MatDialog);
  private authService = inject(AuthService);
  private studentService = inject(StudentService);
  @Input() exam!: Test; // Test bilgisi ve sorular
  route = inject(ActivatedRoute);
  testService = inject(TestService);
  gradeService = inject(GradesService);
  router = inject(Router);
  results!: TestInstance;
  gradeName = signal<string>(''); // Sınıf adı
  private grades = signal<Grade[]>([]);
  private studentLookups = signal<StudentLookup[]>([]);
  private readonly isTeacher = this.authService.hasRole('Teacher');
  private teacherPanelInitialized = false;
  protected readonly assignmentPanelState = signal<AssignmentPanelState>({
    loading: false,
    assignments: [],
    summary: null,
    lastRefreshed: null,
    error: null,
  });
  public regions = signal<QuestionRegion[]>([]); // Soru bölgeleri
  public selectedChoices = signal<Map<number, AnswerChoice>>(new Map()); // 🔄 Her soru için seçilen şıkkı sakla
  public correctChoices = signal<Map<number, AnswerChoice>>(new Map()); // 🔄 Her soru için seçilen şıkkı sakla
  testId!: number;
  questions: { status: 'correct' | 'incorrect' | 'unknown' }[] = [];
  public currentIndex = signal(0);
  StartTest(id: number | null) {
    if (id) {
      this.testService.startTest(id).subscribe((response) => {
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

  protected refreshAssignments(): void {
    this.loadAssignments();
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

    dialogRef.afterClosed().subscribe((result: WorksheetAssignmentDialogResult | undefined) => {
      if (!result) {
        return;
      }

      this.assignmentPanelState.update((state) => ({
        ...state,
        loading: true,
        error: null,
      }));

      this.testService.assignWorksheet(result.request).subscribe({
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
        return 'Tamamlandı';
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
        return 'status-chip completed';
      case 'InProgress':
        return 'status-chip in-progress';
      case 'Scheduled':
        return 'status-chip scheduled';
      case 'Expired':
        return 'status-chip expired';
      default:
        return 'status-chip not-started';
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

    this.testService.getWorksheetAssignmentsForTeacher(this.testId).subscribe({
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

    this.studentService.getLookup().subscribe({
      next: (students) => this.studentLookups.set(students ?? []),
      error: () => this.studentLookups.set([]),
    });
  }

  questionSelected(index: number) {
    const question = this.results.testInstanceQuestions[index];
    console.log('Selected question:', question);
    this.currentIndex.set(index);
  }

  ngOnInit() {
    this.route.paramMap.subscribe(async (params) => {
      this.testId = Number(params.get('testId'));
      if (this.testId) {
        this.exam = await lastValueFrom(this.testService.get(this.testId));
        if (this.exam) {
          this.gradeService.getGrades().subscribe((grades) => {
            this.grades.set(grades);
            const grade = grades.find((g) => g.id === this.exam.gradeId);
            this.gradeName.set(grade ? grade.name : 'Bilinmiyor');
          });

          if (this.isTeacher) {
            this.initializeTeacherPanel();
          }
        }
        if (this.exam && this.exam.instance) {
          this.testService.getCanvasTestResults(this.exam.instance.id).subscribe((response: TestInstance) => {
            if (response) {
              this.results = response;
              const data: QuestionRegion[] = this.testService.convertTestInstanceToRegions(this.results);
              this.regions.set(data);

              this.results.testInstanceQuestions.forEach((q: TestInstanceQuestion) => {
                if (q.selectedAnswerId) {
                  const selectedChoice = this.regions()
                    .find((a) => a.id == q.question.id)
                    ?.answers.find((a) => a.id === q.selectedAnswerId);
                  if (selectedChoice) {
                    const updatedChoices = new Map(this.selectedChoices());
                    updatedChoices.set(q.question.id, selectedChoice);
                    this.selectedChoices.set(updatedChoices);
                  }
                }

                const correctChoice = this.regions()
                  .find((a) => a.id == q.question.id)
                  ?.answers.find((a) => a.id === q.question.correctAnswerId);
                const updatedCorrectChoices = new Map(this.correctChoices());
                if (correctChoice) {
                  updatedCorrectChoices.set(q.question.id, correctChoice);
                  this.correctChoices.set(updatedCorrectChoices);
                }
              });

              this.questions = this.results.testInstanceQuestions.map((tiq) => {
                if (tiq.selectedAnswerId === null) {
                  return { status: 'unknown' };
                } else if (tiq.selectedAnswerId === tiq.question.correctAnswerId) {
                  return { status: 'correct' };
                } else {
                  return { status: 'incorrect' };
                }
              });
            }
          });
        }
      }
    });
  }
}
