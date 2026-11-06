import { WorksheetStudentVisibility, WorksheetTeacherSharing } from '../../models/test-instance';
import { ChangeDetectionStrategy, Component, computed, inject, OnDestroy, signal } from '@angular/core';
import { HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatChipsModule } from '@angular/material/chips';
import { MatDividerModule } from '@angular/material/divider';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { catchError, EMPTY, filter, interval, Subscription, switchMap, timer } from 'rxjs';
import { LabelingBook } from '../../models/labeling-book';
import { Grade } from '../../models/student';
import { Subject } from '../../models/subject';
import { Topic } from '../../models/topic';
import { GradesService } from '../../services/grades.service';
import { LabelingBookService } from '../../services/labeling-book.service';
import {
  CatalogImportResult,
  QuestionTransferImportPreview,
  QuestionTransferJob,
  QuestionTransferService,
  QuestionTransferWorksheetListItem,
  QuestionTransferWorksheetResult,
  WorksheetKind,
} from '../../services/question-transfer.service';
import { SubjectService } from '../../services/subject.service';

/** Çeviriler kendi Transloco scope'unda: `public/i18n/question-transfer/<lang>.json` (issue #183). */
const QUESTION_TRANSFER_SCOPE = 'question-transfer';

const JOB_POLL_MS = 5_000;
/** Süren iş yokken liste daha seyrek yenilenir (12 x 5 sn). */
const IDLE_POLL_EVERY = 12;
const MAX_QUESTION_IDS = 5000;
/** Sunucunun bir seferde verdiği en çok kayıt; daha fazlası filtreyle bulunur. */
const WORKSHEET_LIST_TAKE = 200;
const WORKSHEET_SEARCH_DEBOUNCE_MS = 300;

export type ExportMode = 'questions' | 'worksheets';

const CATALOG_ROWS = ['grades', 'subjects', 'gradeSubjects', 'topics', 'subTopics', 'learningOutcomes'] as const;

@Component({
  selector: 'app-question-transfer',
  standalone: true,
  imports: [
    FormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatCheckboxModule,
    MatProgressBarModule,
    MatIconModule,
    MatTooltipModule,
    MatChipsModule,
    MatDividerModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(QUESTION_TRANSFER_SCOPE)],
  templateUrl: './question-transfer.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./question-transfer.component.scss'],
})
export class QuestionTransferComponent implements OnDestroy {
  private readonly transfer = inject(QuestionTransferService);
  private readonly gradesService = inject(GradesService);
  private readonly subjectService = inject(SubjectService);
  private readonly bookService = inject(LabelingBookService);

  readonly catalogRows = CATALOG_ROWS;
  readonly worksheetListTake = WORKSHEET_LIST_TAKE;

  // ---- dışa aktarılacak test ve etkinlikler
  readonly exportMode = signal<ExportMode>('questions');
  readonly worksheetKind = signal<WorksheetKind | null>(null);
  readonly worksheetSearch = signal('');
  readonly worksheets = signal<QuestionTransferWorksheetListItem[]>([]);
  readonly worksheetsLoading = signal(false);
  readonly worksheetsFailed = signal(false);
  /** Seçim filtre değişince korunur: farklı sınıflardan kayıtlar aynı pakete girebilir. Süzülüp görünmeyenin adı da gösterilir. */
  private readonly selectedWorksheets = signal<ReadonlyMap<number, string>>(new Map());
  readonly selectedWorksheetIds = computed<ReadonlySet<number>>(() => new Set(this.selectedWorksheets().keys()));
  readonly selectedWorksheetNames = computed(() => Array.from(this.selectedWorksheets().values()).join(', '));

  // ---- dışa aktarma kapsamı
  readonly grades = signal<Grade[]>([]);
  readonly subjects = signal<Subject[]>([]);
  readonly topics = signal<Topic[]>([]);
  readonly books = signal<LabelingBook[]>([]);
  readonly gradeId = signal<number | null>(null);
  readonly subjectId = signal<number | null>(null);
  readonly topicId = signal<number | null>(null);
  readonly bookId = signal<number | null>(null);
  readonly questionIdsText = signal('');
  readonly hasScope = computed(() => this.gradeId() !== null || this.subjectId() !== null || this.topicId() !== null || this.bookId() !== null);
  readonly questionIds = computed(() => this.parseIds(this.questionIdsText()));
  /** Kapsam ve numara yoksa tüm sorular çıkar: düğme bunu açıkça söyler. */
  readonly exportsEverything = computed(() => !this.hasScope() && this.questionIds().length === 0);
  readonly exportBusy = signal(false);
  readonly exportFailed = signal(false);

  // ---- içe alma
  readonly importFile = signal<File | null>(null);
  readonly importPreview = signal<QuestionTransferImportPreview | null>(null);
  readonly previewLoading = signal(false);
  readonly previewError = signal<string | null>(null);
  readonly importBusy = signal(false);
  /** Issue #819: içe alınan test/etkinliklerin paylaşımı; varsayılan bugünkü (Özel + Kısıtlı). */
  readonly importSharingOptions = [
    WorksheetTeacherSharing.Private,
    WorksheetTeacherSharing.PublicView,
    WorksheetTeacherSharing.PublicAssignable,
  ];
  readonly importVisibilityOptions = [WorksheetStudentVisibility.Restricted, WorksheetStudentVisibility.Normal];
  readonly importTeacherSharing = signal(WorksheetTeacherSharing.Private);
  readonly importStudentVisibility = signal(WorksheetStudentVisibility.Restricted);
  readonly importFailed = signal(false);

  // ---- katalog
  readonly catalogBusy = signal(false);
  readonly catalogFileName = signal<string | null>(null);
  readonly catalogPreview = signal<CatalogImportResult | null>(null);
  readonly catalogResult = signal<CatalogImportResult | null>(null);
  readonly catalogErrors = signal<string[]>([]);
  readonly catalogFileInvalid = signal(false);
  private catalogBundle: unknown = null;

  // ---- işler
  readonly jobs = signal<QuestionTransferJob[]>([]);
  readonly jobsFailed = signal(false);
  readonly downloadingJobId = signal<string | null>(null);
  readonly downloadFailed = signal(false);
  private readonly hasActiveJob = computed(() => this.jobs().some((j) => j.status === 'Queued' || j.status === 'Running'));
  private pollSub?: Subscription;
  private previewSub?: Subscription;
  private subjectsSub?: Subscription;
  private topicsSub?: Subscription;
  private worksheetsSub?: Subscription;
  private worksheetSearchSub?: Subscription;

  constructor() {
    this.startPolling();
    this.gradesService.getGrades().subscribe({ next: (g) => this.grades.set(g ?? []), error: () => this.grades.set([]) });
    this.subjectService.loadCategories().subscribe({ next: (s) => this.subjects.set(s ?? []), error: () => this.subjects.set([]) });
    this.bookService.list().subscribe({ next: (b) => this.books.set(b ?? []), error: () => this.books.set([]) });
  }

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
    this.previewSub?.unsubscribe();
    this.subjectsSub?.unsubscribe();
    this.topicsSub?.unsubscribe();
    this.worksheetsSub?.unsubscribe();
    this.worksheetSearchSub?.unsubscribe();
  }

  // ---- test ve etkinlik seçimi

  setExportMode(mode: ExportMode): void {
    this.exportMode.set(mode);
    this.exportFailed.set(false);
    this.reloadWorksheets();
  }

  onWorksheetKindChange(kind: WorksheetKind | null): void {
    this.worksheetKind.set(kind);
    this.reloadWorksheets();
  }

  onWorksheetSearchChange(search: string): void {
    this.worksheetSearch.set(search);
    this.worksheetSearchSub?.unsubscribe();
    this.worksheetSearchSub = timer(WORKSHEET_SEARCH_DEBOUNCE_MS).subscribe(() => this.reloadWorksheets());
  }

  isWorksheetSelected(id: number): boolean {
    return this.selectedWorksheetIds().has(id);
  }

  toggleWorksheet(id: number, selected: boolean): void {
    const next = new Map(this.selectedWorksheets());
    if (selected) {
      next.set(id, this.worksheets().find((w) => w.id === id)?.name ?? String(id));
    } else {
      next.delete(id);
    }
    this.selectedWorksheets.set(next);
  }

  clearWorksheetSelection(): void {
    this.selectedWorksheets.set(new Map());
  }

  /** Sorular sekmesindeyken listeye gerek yok: sekmeye dönünce güncel filtreyle yüklenir. */
  reloadWorksheets(): void {
    this.worksheetSearchSub?.unsubscribe();
    // Önceki filtrenin geç gelen yanıtı listeyi ezmesin.
    this.worksheetsSub?.unsubscribe();
    if (this.exportMode() !== 'worksheets') {
      this.worksheetsLoading.set(false);
      return;
    }

    this.worksheetsLoading.set(true);
    this.worksheetsFailed.set(false);
    this.worksheetsSub = this.transfer
      .listWorksheets({
        kind: this.worksheetKind(),
        gradeId: this.gradeId(),
        subjectId: this.subjectId(),
        search: this.worksheetSearch().trim(),
        take: WORKSHEET_LIST_TAKE,
      })
      .subscribe({
        next: (worksheets) => {
          this.worksheetsLoading.set(false);
          this.worksheets.set(worksheets ?? []);
        },
        error: () => {
          this.worksheetsLoading.set(false);
          this.worksheets.set([]);
          this.worksheetsFailed.set(true);
        },
      });
  }

  /** Paketinde test ya da etkinlik olmayan iş bu bölümü göstermez. */
  hasWorksheetCounts(result: QuestionTransferWorksheetResult | null | undefined): boolean {
    return !!result && result.imported + result.alreadyImported + result.blocked > 0;
  }

  // ---- kapsam

  onGradeChange(id: number | null): void {
    this.gradeId.set(id);
    this.topicId.set(null);
    this.loadTopics();
    this.reloadWorksheets();
    const subjects$ = id === null ? this.subjectService.loadCategories() : this.subjectService.getSubjectsByGrade(id);
    // Önceki sınıfın geç gelen yanıtı listeyi ezmesin.
    this.subjectsSub?.unsubscribe();
    this.subjectsSub = subjects$.subscribe({
      next: (subjects) => {
        this.subjects.set(subjects ?? []);
        if (this.subjectId() !== null && !(subjects ?? []).some((s) => s.id === this.subjectId())) {
          this.clearSubject();
          this.reloadWorksheets();
        }
      },
      error: () => {
        // Görünmeyen seçim kapsamı daraltmaya devam etmesin.
        this.subjects.set([]);
        const hadSubject = this.subjectId() !== null;
        this.clearSubject();
        if (hadSubject) {
          this.reloadWorksheets();
        }
      },
    });
  }

  onSubjectChange(id: number | null): void {
    this.subjectId.set(id);
    this.topicId.set(null);
    this.loadTopics();
    this.reloadWorksheets();
  }

  private clearSubject(): void {
    this.topicsSub?.unsubscribe();
    this.subjectId.set(null);
    this.topicId.set(null);
    this.topics.set([]);
  }

  private loadTopics(): void {
    this.topicsSub?.unsubscribe();
    const subjectId = this.subjectId();
    const gradeId = this.gradeId();
    if (subjectId === null) {
      this.topics.set([]);
      return;
    }
    const topics$ = gradeId === null
      ? this.subjectService.getTopicsBySubject(subjectId)
      : this.subjectService.getTopicsBySubjectAndGrade(subjectId, gradeId);
    this.topicsSub = topics$.subscribe({ next: (t) => this.topics.set(t ?? []), error: () => this.topics.set([]) });
  }

  startExport(): void {
    const worksheetIds = this.exportMode() === 'worksheets' ? Array.from(this.selectedWorksheetIds()) : null;
    if (worksheetIds?.length === 0) return;

    this.exportBusy.set(true);
    this.exportFailed.set(false);
    this.transfer
      .startExport(worksheetIds
        // Sınıf ve ders burada yalnız listeyi süzer: paket seçilen kayıtların sorularını taşır.
        ? { questionIds: [], worksheetIds }
        : {
            // Kapsam seçiliyse sunucu listeyi kendi çıkarır.
            questionIds: this.hasScope() ? [] : this.questionIds(),
            gradeId: this.gradeId(),
            subjectId: this.subjectId(),
            topicId: this.topicId(),
            bookId: this.bookId(),
          })
      .subscribe({
        next: (job) => {
          this.exportBusy.set(false);
          // Başlamayan işte seçim kalır: sunucunun açıklaması iş listesinde görünür, admin düzeltip yeniden dener.
          if (worksheetIds && job.status === 'Queued') {
            this.clearWorksheetSelection();
          }
          this.refreshJobs();
        },
        error: () => {
          this.exportBusy.set(false);
          this.exportFailed.set(true);
        },
      });
  }

  // ---- içe alma

  onImportFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.item(0) ?? null;
    // Aynı dosya yeniden seçilebilsin: değer kalırsa tarayıcı change olayı üretmez.
    input.value = '';
    this.previewSub?.unsubscribe();
    this.previewLoading.set(false);
    this.importFile.set(file);
    this.importPreview.set(null);
    this.previewError.set(null);
    this.importFailed.set(false);
    if (!file) return;

    this.previewLoading.set(true);
    // Önceki dosyanın yanıtı bu dosyanın sayıları gibi görünmesin.
    this.previewSub = this.transfer.previewImport(file).subscribe({
      next: (preview) => {
        this.previewLoading.set(false);
        this.importPreview.set(preview);
      },
      error: (err: HttpErrorResponse) => {
        this.previewLoading.set(false);
        this.previewError.set(this.serverMessage(err) ?? '');
      },
    });
  }

  startImport(): void {
    const file = this.importFile();
    if (!file) return;

    this.importBusy.set(true);
    this.importFailed.set(false);
    this.transfer.startImport(file, {
      teacherSharing: this.importTeacherSharing(),
      studentVisibility: this.importStudentVisibility(),
    }).subscribe({
      next: () => {
        this.importBusy.set(false);
        this.importFile.set(null);
        this.importPreview.set(null);
        this.refreshJobs();
      },
      error: () => {
        this.importBusy.set(false);
        this.importFailed.set(true);
      },
    });
  }

  // ---- katalog

  downloadCatalog(): void {
    this.catalogBusy.set(true);
    this.catalogErrors.set([]);
    this.transfer.downloadCatalog().subscribe({
      next: (resp) => {
        this.catalogBusy.set(false);
        this.saveBlob(resp, 'catalog.json');
      },
      error: (err: HttpErrorResponse) => {
        this.catalogBusy.set(false);
        this.catalogErrors.set([this.serverMessage(err) ?? '']);
      },
    });
  }

  async onCatalogFileSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.item(0) ?? null;
    input.value = '';
    this.catalogFileName.set(file?.name ?? null);
    this.catalogPreview.set(null);
    this.catalogResult.set(null);
    this.catalogErrors.set([]);
    this.catalogFileInvalid.set(false);
    this.catalogBundle = null;
    if (!file) return;

    try {
      this.catalogBundle = JSON.parse(await file.text());
    } catch {
      this.catalogFileInvalid.set(true);
      return;
    }
    this.sendCatalog(true);
  }

  applyCatalog(): void {
    this.sendCatalog(false);
  }

  private sendCatalog(dryRun: boolean): void {
    this.catalogBusy.set(true);
    this.catalogErrors.set([]);
    this.transfer.importCatalog(this.catalogBundle, dryRun).subscribe({
      next: (result) => {
        this.catalogBusy.set(false);
        if (dryRun) {
          this.catalogPreview.set(result);
          return;
        }
        this.catalogPreview.set(null);
        this.catalogBundle = null;
        this.catalogResult.set(result);
      },
      error: (err: HttpErrorResponse) => {
        this.catalogBusy.set(false);
        // Yükleme başarısızsa önizleme kalır, tekrar denenebilir.
        if (dryRun) {
          this.catalogPreview.set(null);
        }
        const errors = (err.error as Partial<CatalogImportResult> | null)?.errors;
        this.catalogErrors.set(errors?.length ? errors : [this.serverMessage(err) ?? '']);
      },
    });
  }

  // ---- işler

  /** Dışa aktarma bittiyse paket hazırdır. */
  canDownloadPackage(job: QuestionTransferJob): boolean {
    return job.kind === 'Export' && job.status === 'Completed' && !!job.fileUrl;
  }

  downloadPackage(job: QuestionTransferJob): void {
    this.downloadingJobId.set(job.id);
    this.downloadFailed.set(false);
    this.transfer.downloadPackage(job.sourceKey).subscribe({
      next: (resp) => {
        this.downloadingJobId.set(null);
        this.saveBlob(resp, `question-transfer-${job.sourceKey}-package.zip`);
      },
      error: () => {
        this.downloadingJobId.set(null);
        this.downloadFailed.set(true);
      },
    });
  }

  /** Başarısız iş ve dosyasız biten iş (kapsamda soru yok) sunucunun açıklamasını gösterir. */
  jobMessage(job: QuestionTransferJob): string | null {
    const unfinishedOutcome = job.status === 'Failed' || (job.kind === 'Export' && job.status === 'Completed' && !job.fileUrl);
    return unfinishedOutcome ? (job.message ?? null) : null;
  }

  isJobRunning(job: QuestionTransferJob): boolean {
    return job.status === 'Queued' || job.status === 'Running';
  }

  jobChipColor(job: QuestionTransferJob): 'primary' | 'accent' | 'warn' | undefined {
    if (job.status === 'Completed') return 'primary';
    if (job.status === 'Running') return 'accent';
    if (job.status === 'Failed') return 'warn';
    return undefined;
  }

  progress(job: QuestionTransferJob): number {
    if (!job.totalItems) return 0;
    return Math.round((job.processedItems / job.totalItems) * 100);
  }

  refreshJobs(): void {
    this.transfer.listJobs(10).subscribe({
      next: (jobs) => {
        this.jobsFailed.set(false);
        this.jobs.set(jobs ?? []);
      },
      error: () => this.jobsFailed.set(true),
    });
  }

  openHangfire(): void {
    this.transfer.hangfireLogin().subscribe({
      next: () => window.open(this.transfer.hangfireUrl(), '_blank'),
    });
  }

  private startPolling(): void {
    this.refreshJobs();
    this.pollSub = interval(JOB_POLL_MS)
      .pipe(
        filter((tick) => this.hasActiveJob() || (tick + 1) % IDLE_POLL_EVERY === 0),
        // Tek bir başarısız yenileme aboneliği bitirmesin.
        switchMap(() => this.transfer.listJobs(10).pipe(catchError(() => EMPTY))),
      )
      .subscribe((jobs) => {
        this.jobsFailed.set(false);
        this.jobs.set(jobs ?? []);
      });
  }

  private serverMessage(err: HttpErrorResponse): string | null {
    const message = (err.error as { message?: unknown } | null)?.message;
    return typeof message === 'string' && message.length > 0 ? message : null;
  }

  private saveBlob(resp: HttpResponse<Blob>, fallbackName: string): void {
    if (!resp.body) return;
    const url = window.URL.createObjectURL(resp.body);
    const link = document.createElement('a');
    link.href = url;
    link.download = this.extractFilename(resp.headers.get('content-disposition')) ?? fallbackName;
    link.click();
    window.URL.revokeObjectURL(url);
  }

  private parseIds(value: string): number[] {
    const result = new Set<number>();
    const tokens = (value ?? '').split(/[\s,;]+/g).filter(Boolean);

    for (const token of tokens) {
      const range = /^(\d+)-(\d+)$/.exec(token);
      if (range) {
        const start = Math.min(Number(range[1]), Number(range[2]));
        const end = Math.max(Number(range[1]), Number(range[2]));
        for (let i = Math.max(start, 1); i <= end && result.size < MAX_QUESTION_IDS; i++) result.add(i);
        continue;
      }
      for (const n of token.match(/\d+/g) ?? []) {
        if (Number(n) > 0 && result.size < MAX_QUESTION_IDS) result.add(Number(n));
      }
    }
    return Array.from(result);
  }

  private extractFilename(contentDisposition: string | null): string | null {
    if (!contentDisposition) return null;

    // Handles: attachment; filename="foo.zip" or filename=foo.zip
    const match = /filename\*=UTF-8''([^;]+)|filename="?([^";]+)"?/i.exec(contentDisposition);
    const raw = (match?.[1] ?? match?.[2] ?? '').trim();
    if (!raw) return null;

    try {
      return decodeURIComponent(raw);
    } catch {
      return raw;
    }
  }
}
