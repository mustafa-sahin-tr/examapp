import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { Router, RouterLink } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { Subscription, timer } from 'rxjs';

import { BookImportDemo, BookImportJob, BookImportQuota, bookImportStatusTone, isBookImportActive } from '../../models/book-import';
import { Grade } from '../../models/student';
import { Subject } from '../../models/subject';
import { AuthService } from '../../services/auth.service';
import { EntitlementStateService } from '../../services/entitlement-state.service';
import { BookImportService } from '../../services/book-import.service';
import { GradesService } from '../../services/grades.service';
import { SubjectService } from '../../services/subject.service';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';

/** Sayfa metinleri kendi Transloco scope'unda: `public/i18n/book-imports/<lang>.json`. */
export const BOOK_IMPORTS_SCOPE = 'book-imports';
/** Maket: liste her 5 sn yenilenir (yalnız süren iş varken). */
export const BOOK_IMPORTS_POLL_MS = 5000;
/** Sunucudaki tavan (`BookImportService.MaxUploadBytesCeiling`). */
const MAX_UPLOAD_BYTES = 200_000_000;

type StatusFilter = 'all' | 'awaiting' | 'failed';

/**
 * Issue #1018 (epic #1017): kitaptan aktarım — PDF yükleme formu ve aktarımlar listesi. Pilot: Admin + QuestionTrainer.
 * Issue #1032: öğretmen yükleme yerine örnek kitapla demo görür.
 * Aşamalar bu dilimde iskelet: iş sayfalar ve kurulumdan iş yapmadan geçip kurulum onayında bekler (#1020, #1021).
 */
@Component({
  selector: 'app-book-imports',
  standalone: true,
  imports: [
    DatePipe,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    PageHeaderComponent,
    ReactiveFormsModule,
    RouterLink,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(BOOK_IMPORTS_SCOPE)],
  templateUrl: './book-imports.component.html',
  styleUrls: ['./book-imports.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BookImportsComponent implements OnInit {
  private readonly api = inject(BookImportService);
  private readonly gradesApi = inject(GradesService);
  private readonly subjectsApi = inject(SubjectService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly auth = inject(AuthService);

  private readonly entitlements = inject(EntitlementStateService);

  /** Gerçek yükleme içerik ekibinde ve (#1087) planında `book-import` olan öğretmende; diğerleri demo görür. */
  readonly canUpload = signal(false);
  readonly demo = signal<BookImportDemo | null>(null);
  readonly startingDemo = signal(false);
  readonly demoError = signal<'limitReached' | 'unavailable' | 'startFailed' | null>(null);

  readonly jobs = signal<BookImportJob[]>([]);
  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly quota = signal<BookImportQuota | null>(null);
  readonly grades = signal<Grade[]>([]);
  readonly subjects = signal<Subject[]>([]);
  readonly filter = signal<StatusFilter>('all');

  readonly showForm = signal(false);
  readonly file = signal<File | null>(null);
  readonly fileError = signal<'notPdf' | 'tooLarge' | null>(null);
  readonly dragOver = signal(false);
  readonly submitting = signal(false);
  /** Sunucudan gelen hata kodu (`bookImport.notPdf` …) ya da `generic`. */
  readonly submitError = signal<string | null>(null);
  /** Issue #1122: gönderilen / toplam parça; yükleme sürerken. */
  readonly uploadProgress = signal<{ part: number; parts: number } | null>(null);

  readonly form = new FormGroup({
    bookName: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(200)] }),
    gradeId: new FormControl<number | null>(null, Validators.required),
    subjectId: new FormControl<number | null>(null),
  });

  readonly awaitingCount = computed(() => this.jobs().filter((j) => j.status === 'AwaitingConfirmation').length);
  readonly failedCount = computed(() => this.jobs().filter((j) => j.status === 'Failed').length);
  readonly visibleJobs = computed(() => {
    switch (this.filter()) {
      case 'awaiting':
        return this.jobs().filter((j) => j.status === 'AwaitingConfirmation');
      case 'failed':
        return this.jobs().filter((j) => j.status === 'Failed');
      default:
        return this.jobs();
    }
  });
  readonly quotaBlocked = computed(() => this.quota()?.allowed === false);

  readonly tone = bookImportStatusTone;

  private poll?: Subscription;

  ngOnInit(): void {
    this.load();
    if (this.auth.hasRealmRole('Admin') || this.auth.hasRealmRole('QuestionTrainer')) {
      this.startUploads();
      return;
    }
    this.entitlements
      .load()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        // Plan okunamadıysa `has` true döner; demo yerine yükleme formu, sunucu 403 ile karar verir.
        if (this.entitlements.has('book-import')) this.startUploads();
        else this.loadDemo();
      });
  }

  private startUploads(): void {
    this.canUpload.set(true);
    this.gradesApi
      .getGrades()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (grades) => this.grades.set(grades ?? []), error: () => this.grades.set([]) });
    this.api
      .quota()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (quota) => this.quota.set(quota), error: () => this.quota.set(null) });
    this.form.controls.gradeId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((gradeId) => {
      this.form.controls.subjectId.setValue(null);
      this.subjects.set([]);
      if (gradeId == null) return;
      this.subjectsApi.getSubjectsByGrade(gradeId).subscribe({
        next: (subjects) => this.subjects.set(subjects ?? []),
        error: () => this.subjects.set([]),
      });
    });
  }

  private loadDemo(): void {
    this.api
      .demo()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (demo) => this.demo.set(demo), error: () => this.demo.set(null) });
  }

  /** Bitmemiş demo varsa sunucu onu döner; yeni hak harcanmaz. */
  startDemo(): void {
    if (this.startingDemo()) return;
    this.startingDemo.set(true);
    this.demoError.set(null);
    this.api
      .startDemo()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (job) => {
          this.startingDemo.set(false);
          this.router.navigate(['/book-imports', job.id]);
        },
        error: (error: HttpErrorResponse) => {
          this.startingDemo.set(false);
          const code = error.error?.code;
          this.demoError.set(
            code === 'bookImport.demoLimitReached' ? 'limitReached' : code === 'bookImport.demoUnavailable' ? 'unavailable' : 'startFailed',
          );
          this.loadDemo();
        },
      });
  }

  load(): void {
    this.loadError.set(false);
    this.api
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (jobs) => {
          this.jobs.set(jobs ?? []);
          this.loading.set(false);
          this.schedulePoll();
        },
        error: () => {
          this.loading.set(false);
          this.loadError.set(true);
        },
      });
  }

  /** Süren iş varken liste yenilenir; hepsi durunca yoklama biter. */
  private schedulePoll(): void {
    this.poll?.unsubscribe();
    if (!this.jobs().some((j) => isBookImportActive(j.status))) return;
    this.poll = timer(BOOK_IMPORTS_POLL_MS)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.load());
  }

  openForm(): void {
    this.showForm.set(true);
    this.submitError.set(null);
  }

  cancelForm(): void {
    this.showForm.set(false);
    this.form.reset();
    this.file.set(null);
    this.fileError.set(null);
    this.submitError.set(null);
  }

  onFileInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.pickFile(input.files?.[0] ?? null);
    input.value = '';
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragOver.set(false);
    this.pickFile(event.dataTransfer?.files?.[0] ?? null);
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragOver.set(true);
  }

  pickFile(file: File | null): void {
    this.submitError.set(null);
    if (!file) return;
    const isPdf = file.type === 'application/pdf' || file.name.toLowerCase().endsWith('.pdf');
    if (!isPdf) {
      this.file.set(null);
      this.fileError.set('notPdf');
      return;
    }
    if (file.size > MAX_UPLOAD_BYTES) {
      this.file.set(null);
      this.fileError.set('tooLarge');
      return;
    }
    this.fileError.set(null);
    this.file.set(file);
    if (!this.form.controls.bookName.value) {
      this.form.controls.bookName.setValue(file.name.replace(/\.pdf$/i, ''));
    }
  }

  submit(): void {
    const file = this.file();
    if (!file || this.form.invalid || this.submitting() || this.quotaBlocked()) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    this.submitting.set(true);
    this.submitError.set(null);
    this.uploadProgress.set(null);
    this.api
      .start({ file, bookName: value.bookName.trim(), gradeId: value.gradeId!, subjectId: value.subjectId })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (event) => {
          if (event.type === 'progress') {
            this.uploadProgress.set({ part: event.part, parts: event.parts });
            return;
          }
          this.submitting.set(false);
          this.uploadProgress.set(null);
          this.router.navigate(['/book-imports', event.job.id]);
        },
        error: (error: HttpErrorResponse) => {
          this.submitting.set(false);
          this.uploadProgress.set(null);
          const code = typeof error.error?.code === 'string' ? (error.error.code as string) : null;
          this.submitError.set(code ?? 'generic');
        },
      });
  }

  /** Hata kodunu scope'taki anahtara çevirir (`bookImport.notPdf` → `errors.notPdf`). */
  errorKey(code: string): string {
    const known = ['invalidBookName', 'invalidGrade', 'invalidSubject', 'notPdf', 'fileTooLarge', 'uploadNotFound', 'uploadIncomplete'];
    const name = code.replace(/^bookImport\./, '');
    if (code === 'entitlement.limitReached') return 'errors.quotaReached';
    return known.includes(name) ? `errors.${name}` : 'errors.generic';
  }

  /** Dosya boyutu: 1 MB altı KB ile (küçük dosya "0 MB" görünmesin). */
  fileSize(bytes: number): string {
    return bytes < 1_000_000 ? `${Math.max(1, Math.round(bytes / 1000))} KB` : `${(bytes / 1_000_000).toFixed(1)} MB`;
  }

  progress(job: BookImportJob): number {
    return job.stageTotal > 0 ? Math.round((100 * job.stageProcessed) / job.stageTotal) : 0;
  }

  /** Satır eylemi duruma göre (maket). */
  actionKey(job: BookImportJob): string {
    switch (job.status) {
      case 'AwaitingConfirmation':
        return 'actions.openSetup';
      case 'ReadyForReview':
        return 'actions.review';
      default:
        return 'actions.open';
    }
  }
}
