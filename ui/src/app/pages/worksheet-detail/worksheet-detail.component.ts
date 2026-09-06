import { CommonModule } from '@angular/common';
import { Component, computed, DestroyRef, inject, Input, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute, NavigationExtras, Router } from '@angular/router';
import { Test, TestInstance, TestInstanceQuestion, WorksheetTeacherSharing } from '../../models/test-instance';
import { finalize, lastValueFrom } from 'rxjs';
import { TestService } from '../../services/test.service';
import { AnswerChoice, QuestionRegion } from '../../models/draws';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar } from '@angular/material/snack-bar';
import { GradesService } from '../../services/grades.service';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatDialog } from '@angular/material/dialog';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule, MAT_DATE_LOCALE } from '@angular/material/core';
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
import {
  AssignmentPermissionDialogComponent,
  AssignmentPermissionDialogResult,
} from './components/assignment-permission-dialog/assignment-permission-dialog.component';
import { QuestionCanvasViewComponent } from '../../shared/components/question-canvas-view/question-canvas-view.component';
import { QuestionNavigatorComponent } from '../../shared/components/question-navigator/question-navigator.component';
import { WorksheetAttempt, WorksheetDetail, WorksheetReminder } from '../../models/worksheet-detail';

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
    FormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatDatepickerModule,
    MatNativeDateModule,
  ],
  providers: [{ provide: MAT_DATE_LOCALE, useValue: 'tr-TR' }],
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
  protected readonly copyLoading = signal(false);

  /** Atama izni talebi (issue #13): bu öğretmenin bu sınav için bekleyen talebi var mı. */
  protected readonly accessRequestPending = signal(false);

  /** Salt-görüntüleme şeridinde "Atama izni iste" aksiyonu gösterilsin mi. */
  protected readonly canRequestAssignPermission = computed(
    () => this.isSharedReadOnlyForTeacher() && !this.canAssignWorksheet()
  );

  protected readonly completedResult = computed(() => this.detail()?.completedResult ?? null);

  /**
   * Bu worksheet için düzenleme yetkisi (backend `canEdit`; alan yoksa izin ver).
   * Öğretmen backend'de zaten başkasının worksheet'ine erişemiyor; bu guard admin
   * ve ileride public worksheet senaryosu için duruyor.
   */
  protected readonly canEditWorksheet = computed(() => this.detail()?.worksheet?.canEdit !== false);

  /**
   * Bu worksheet'e öğrenci/sınıf atama yetkisi (backend `canAssign`; alan yoksa izin ver).
   * Sahip/admin için her zaman true; sahibi olmayan öğretmen için worksheet
   * `TeacherSharing === PublicAssignable` ise backend bunu true döner (issue #12).
   */
  protected readonly canAssignWorksheet = computed(() => this.detail()?.worksheet?.canAssign !== false);

  /**
   * Paylaşılan (Public*) bir worksheet'i, sahibi olmayan bir öğretmen görüntülüyorsa true.
   * Bu durumda düzenleme/atama yönetimi gizlenir, salt-görüntüleme şeridi gösterilir (issue #11).
   */
  protected readonly isSharedReadOnlyForTeacher = computed(
    () =>
      this.isTeacher &&
      this.detail()?.worksheet?.isOwner === false &&
      this.detail()?.worksheet?.teacherSharing !== WorksheetTeacherSharing.Private
  );

  protected readonly sharedOwnerName = computed(() => this.detail()?.worksheet?.ownerName ?? null);

  protected readonly view = computed<WorksheetView>(() => {
    if (this.isTeacher) {
      return 'teacher';
    }
    return this.completedResult() ? 'completed' : 'start';
  });

  // Planla & Hatırlat
  protected readonly reminder = signal<WorksheetReminder | null>(null);
  protected readonly reminderSaving = signal(false);
  protected readonly reminderEditing = signal(false);
  protected reminderDate: Date | null = null;
  protected reminderTime = '09:00';
  protected remindBeforeMinutes = 60;
  protected readonly minReminderDate = new Date();
  protected readonly remindBeforeOptions = [15, 30, 60, 120];

  protected readonly hasActiveReminder = computed(() => {
    const current = this.reminder();
    return !!current && current.status !== 'Cancelled';
  });

  protected readonly showReminderForm = computed(() => !this.hasActiveReminder() || this.reminderEditing());

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

  /** Başkasının public sınavını kendi hesabına kopyalar ve düzenleme ekranına gider (issue #16). */
  protected copyWorksheet(): void {
    const worksheetId = this.detail()?.worksheet?.id;
    if (!worksheetId || this.copyLoading()) {
      return;
    }

    this.copyLoading.set(true);
    this.testService
      .copyWorksheet(worksheetId)
      .pipe(
        finalize(() => this.copyLoading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (result) => {
          if (result?.worksheetId) {
            this.snackBar.open('Sınav kendi hesabına kopyalandı', 'Tamam', { duration: 3000 });
            this.router.navigate(['/exam', result.worksheetId]);
          } else {
            this.snackBar.open('Kopyalama başarısız', 'Tamam', { duration: 3000 });
          }
        },
        error: (error) => {
          this.snackBar.open(error?.error?.message ?? 'Kopyalama başarısız', 'Tamam', { duration: 3000 });
        },
      });
  }

  /** "Atama izni iste" dialogunu açar (issue #13). 409 → "Talebiniz bekleniyor" durumuna geçer. */
  protected openAssignPermissionDialog(): void {
    const worksheet = this.detail()?.worksheet;
    if (!worksheet?.id || this.accessRequestPending()) {
      return;
    }

    const dialogRef = this.dialog.open(AssignmentPermissionDialogComponent, {
      width: '420px',
      data: {
        worksheetId: worksheet.id,
        worksheetName: worksheet.name,
        ownerName: this.sharedOwnerName(),
      },
    });

    dialogRef
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((result: AssignmentPermissionDialogResult | undefined) => {
        if (!result) {
          return;
        }
        this.accessRequestPending.set(true);
        if (result.submitted) {
          this.snackBar.open('Atama izni talebiniz gönderildi.', 'Tamam', { duration: 3000 });
        }
      });
  }

  protected openSimilarWorksheet(id: number): void {
    this.router.navigate(['/test', id]);
  }

  /** İki alandan (tarih + saat) birleşik yerel Date üretir. */
  private buildScheduledDate(): Date | null {
    if (!this.reminderDate) {
      return null;
    }
    const [hours, minutes] = (this.reminderTime || '00:00').split(':').map((v) => Number(v));
    const combined = new Date(this.reminderDate);
    combined.setHours(hours || 0, minutes || 0, 0, 0);
    return combined;
  }

  protected startEditReminder(): void {
    const current = this.reminder();
    if (current) {
      const scheduled = new Date(current.scheduledFor);
      this.reminderDate = scheduled;
      this.reminderTime = `${String(scheduled.getHours()).padStart(2, '0')}:${String(
        scheduled.getMinutes()
      ).padStart(2, '0')}`;
      this.remindBeforeMinutes = current.remindBeforeMinutes;
    }
    this.reminderEditing.set(true);
  }

  protected cancelEditReminder(): void {
    this.reminderEditing.set(false);
  }

  protected saveReminder(): void {
    if (this.reminderSaving()) {
      return;
    }
    const worksheetId = this.testId;
    const scheduled = this.buildScheduledDate();
    if (!worksheetId || !scheduled) {
      this.snackBar.open('Lütfen tarih ve saat seçin.', 'Tamam', { duration: 3000 });
      return;
    }
    if (scheduled.getTime() <= Date.now()) {
      this.snackBar.open('Geçmiş bir tarih seçemezsin.', 'Tamam', { duration: 3000 });
      return;
    }

    this.reminderSaving.set(true);
    this.testService
      .putWorksheetReminder(worksheetId, {
        scheduledFor: scheduled.toISOString(),
        remindBeforeMinutes: this.remindBeforeMinutes,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (reminder) => {
          this.reminderSaving.set(false);
          this.reminder.set(reminder);
          this.reminderEditing.set(false);
          this.snackBar.open('Hatırlatıcı kuruldu.', 'Tamam', { duration: 3000 });
        },
        error: (error) => {
          this.reminderSaving.set(false);
          this.snackBar.open(error?.error?.message ?? 'Hatırlatıcı kaydedilemedi.', 'Tamam', { duration: 3000 });
          if (error?.status === 409) {
            this.loadDetail();
          }
        },
      });
  }

  protected removeReminder(): void {
    if (this.reminderSaving() || !this.testId) {
      return;
    }
    this.reminderSaving.set(true);
    this.testService
      .deleteWorksheetReminder(this.testId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.reminderSaving.set(false);
          this.reminder.set(null);
          this.reminderEditing.set(false);
          this.snackBar.open('Hatırlatıcı iptal edildi.', 'Tamam', { duration: 3000 });
        },
        error: (error) => {
          this.reminderSaving.set(false);
          this.snackBar.open(error?.error?.message ?? 'Hatırlatıcı iptal edilemedi.', 'Tamam', { duration: 3000 });
        },
      });
  }

  protected reminderWhenLabel(iso: string): string {
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) {
      return '—';
    }
    return date.toLocaleString('tr-TR', {
      weekday: 'short',
      day: 'numeric',
      month: 'short',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  protected remindBeforeLabel(minutes: number): string {
    if (minutes % 60 === 0) {
      return `${minutes / 60} saat önce`;
    }
    return `${minutes} dakika önce`;
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
          const planned = detail?.plannedReminder ?? null;
          this.reminder.set(planned && planned.status !== 'Cancelled' ? planned : null);
          this.reminderEditing.set(false);
          const completedInstanceId = detail?.completedResult?.instanceId;
          if (!this.isTeacher && completedInstanceId) {
            this.loadResultsForInstance(completedInstanceId);
          }
        },
        error: (error) => {
          this.detailLoading.set(false);
          if (error?.status === 404) {
            this.snackBar.open('Bu teste erişiminiz yok veya test bulunamadı.', 'Tamam', { duration: 4000 });
            this.router.navigate(['/tests']);
            return;
          }
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
      try {
        this.exam = await lastValueFrom(this.testService.get(this.testId));
      } catch {
        // 404 / erişim yok — loadDetail() error handler'ı yönlendirmeyi üstlenir.
        return;
      }
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
