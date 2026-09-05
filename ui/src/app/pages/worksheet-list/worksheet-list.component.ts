import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import {
  Subject as RxSubject,
  catchError,
  finalize,
  forkJoin,
  from,
  map,
  mergeMap,
  of,
  reduce,
  switchMap,
} from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';

import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';
import { AssignedWorksheet } from '../../models/assignment';
import { Paged, Test } from '../../models/test-instance';
import { Subject } from '../../models/subject';
import {
  DURATION_BUCKET_RANGES,
  DurationBucket,
  QUESTION_BUCKET_RANGES,
  QuestionBucket,
  WORKSHEET_SORT_OPTIONS,
  WorksheetListFilter,
  WorksheetListTab,
  WorksheetSortBy,
  WorksheetStatus,
} from '../../models/worksheet-list-filter';
import { AuthService } from '../../services/auth.service';
import { GradesService } from '../../services/grades.service';
import { SubjectService } from '../../services/subject.service';
import { TestService } from '../../services/test.service';
import { WorksheetListViewCardComponent } from './worksheet-list-view-card.component';

interface GradeOption {
  id: number;
  name: string;
}

@Component({
  selector: 'app-worksheet-list',
  templateUrl: './worksheet-list.component.html',
  styleUrls: ['./worksheet-list.component.scss'],
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    MatIconModule,
    MatMenuModule,
    MatButtonModule,
    MatSlideToggleModule,
    PaginationComponent,
    WorksheetListViewCardComponent,
  ],
})
export class WorksheetListComponent implements OnInit {
  private readonly testService = inject(TestService);
  private readonly subjectService = inject(SubjectService);
  private readonly gradesService = inject(GradesService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);

  readonly sortOptions = WORKSHEET_SORT_OPTIONS;
  readonly durationBuckets = Object.entries(DURATION_BUCKET_RANGES) as [DurationBucket, { label: string }][];
  readonly questionBuckets = Object.entries(QUESTION_BUCKET_RANGES) as [QuestionBucket, { label: string }][];

  readonly isStudent = this.auth.hasRole('Student');
  readonly isTeacher = this.auth.hasRole('Teacher');

  readonly subjects = toSignal(this.subjectService.loadCategories(), { initialValue: [] as Subject[] });
  readonly grades = toSignal(this.gradesService.getGrades(), { initialValue: [] as GradeOption[] });

  readonly viewMode = signal<'grid' | 'list'>('grid');
  readonly tab = signal<WorksheetListTab>('discover');
  readonly sortBy = signal<WorksheetSortBy>('newest');

  // Her veri kaynağının kendi loading/error sinyali var; şablon aktif sekmeye göre birleşik okur.
  readonly discoverLoading = signal(false);
  readonly discoverError = signal<string | null>(null);
  readonly assignmentsLoading = signal(false);
  readonly assignmentsError = signal<string | null>(null);
  readonly bucketsLoading = signal(false);
  readonly bucketsError = signal<string | null>(null);

  readonly paged = signal<Paged<Test>>({ items: [], totalCount: 0, pageNumber: 1, pageSize: 12 });
  readonly pageNumber = signal(1);

  private readonly fetchTrigger = new RxSubject<void>();

  constructor() {
    // Tüm discover fetch'leri tek akıştan geçer: switchMap eskiyen isteği iptal eder,
    // böylece yavaş kalan cevap yeniyi ezmez ve loading erken kapanmaz.
    this.fetchTrigger
      .pipe(
        switchMap(() => {
          this.discoverLoading.set(true);
          this.discoverError.set(null);
          return this.testService.listWorksheets(this.buildFilter()).pipe(
            catchError(() => {
              this.discoverError.set('Testler yüklenirken bir sorun oluştu.');
              return of(null);
            })
          );
        }),
        takeUntilDestroyed()
      )
      .subscribe((result) => {
        this.discoverLoading.set(false);
        if (result) {
          this.paged.set(result);
        }
      });
  }

  /** Aktif sekmeye göre birleşik loading. */
  readonly loading = computed(() => {
    switch (this.tab()) {
      case 'discover':
        return this.discoverLoading();
      case 'assigned':
        return this.assignmentsLoading();
      default:
        return this.bucketsLoading();
    }
  });

  /** Aktif sekmeye göre birleşik error. */
  readonly error = computed(() => {
    switch (this.tab()) {
      case 'discover':
        return this.discoverError();
      case 'assigned':
        return this.assignmentsError();
      default:
        return this.bucketsError();
    }
  });

  readonly search = signal('');
  readonly selectedSubjectIds = signal<number[]>([]);
  readonly selectedGradeIds = signal<number[]>([]);
  readonly selectedStatuses = signal<WorksheetStatus[]>([]);
  readonly durationBucket = signal<DurationBucket | null>(null);
  readonly questionBucket = signal<QuestionBucket | null>(null);
  readonly practiceOnly = signal(false);
  /** "Başkalarının sınavları" toggle — açıkken diğer öğretmenlerin Public* worksheet'leri de listelenir (issue #11). */
  readonly includeShared = signal(false);

  readonly assignments = signal<AssignedWorksheet[]>([]);
  /** `statuses:[0]` — devam eden testler (tüm sayfalardan). */
  readonly inProgressTests = signal<Test[]>([]);
  /** `statuses:[1]` — tamamlanan testler. */
  readonly completedTests = signal<Test[]>([]);

  readonly showMobileFilters = signal(false);
  readonly selectedIds = signal<Set<number>>(new Set());

  readonly pageSize = computed(() => (this.viewMode() === 'list' ? 20 : 12));

  readonly assignmentByWorksheetId = computed(() => {
    const map = new Map<number, AssignedWorksheet>();
    for (const a of this.assignments()) {
      map.set(a.worksheetId, a);
    }
    return map;
  });

  /** "Kaldığın yerden devam et" şeridi = devam eden testler (worksheet listesinden, instance.status===0). */
  readonly resumeItems = computed<Test[]>(() => this.inProgressTests());

  readonly assignedCount = computed(() => this.assignments().length);
  readonly inProgressCount = computed(() => this.inProgressTests().length);

  readonly subjectMap = computed(() => {
    const map = new Map<number, string>();
    for (const s of this.subjects()) {
      map.set(s.id, s.name);
    }
    return map;
  });

  readonly gradeMap = computed(() => {
    const map = new Map<number, string>();
    for (const g of this.grades()) {
      map.set(g.id, g.name);
    }
    return map;
  });

  /**
   * "discover" sekmesi tek bir sayfalı listeden gelir (issue #14); arama zaten backend'e gönderildiği
   * için (`buildFilter().search`) burada yalnızca `isAssigned` alanına göre "Atanan sınavlar" / "Keşfet"
   * olmak üzere iki gruba ayrılır.
   */
  readonly discoverAssignedTests = computed<Test[]>(() => this.paged().items.filter((t) => t.isAssigned === true));

  readonly discoverExploreTests = computed<Test[]>(() => this.paged().items.filter((t) => t.isAssigned !== true));

  /** Aktif segment sekmesine göre gösterilecek test listesi. */
  readonly visibleTests = computed<Test[]>(() => {
    const tab = this.tab();
    const term = this.search().trim().toLocaleLowerCase('tr');
    let source: Test[];
    switch (tab) {
      case 'discover':
        return this.paged().items;
      case 'inprogress':
        source = this.inProgressTests();
        break;
      case 'completed':
        source = this.completedTests();
        break;
      default:
        source = this.assignments().map((a) => this.mapAssignmentToTest(a));
    }
    return source.filter((t) => !term || (t.name ?? '').toLocaleLowerCase('tr').includes(term));
  });

  readonly totalCount = computed(() =>
    this.tab() === 'discover' ? this.paged().totalCount : this.visibleTests().length
  );

  readonly hasActiveFilters = computed(
    () =>
      this.selectedSubjectIds().length > 0 ||
      this.selectedGradeIds().length > 0 ||
      this.selectedStatuses().length > 0 ||
      this.durationBucket() !== null ||
      this.questionBucket() !== null ||
      this.practiceOnly()
  );

  readonly headerTitle = computed(() => (this.isTeacher ? 'Testlerim' : 'Sınav Kütüphanesi'));
  readonly headerSubtitle = computed(() =>
    this.isTeacher
      ? 'Oluşturduğun testleri yönet, düzenle ve öğrencilere ata.'
      : 'Konu ve zorluğa göre test bul, çöz ve ilerlemeni takip et.'
  );

  ngOnInit(): void {
    const resolved = this.route.snapshot.data['worksheets'] as Paged<Test> | undefined;
    if (resolved) {
      this.paged.set(resolved);
    }

    // Arama ve bölüm tamamen query param üzerinden yürür. Global header (enhanced-layout)
    // aramayı `?search=` ile, "Yeni"/"Popüler" butonlarını `?section=` ile buraya yönlendirir.
    let first = true;
    this.route.queryParams.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const search = ((params['search'] as string) ?? '').trim();
      const section = (params['section'] as string) ?? '';

      this.search.set(search);
      if (section === 'newest') {
        this.sortBy.set('newest');
      } else if (section === 'popular' || section === 'hot') {
        this.sortBy.set('popular');
      }
      this.pageNumber.set(1);

      // Resolver ilk listeyi (arama dahil) zaten getirdi. `?section=` sortBy'ı değiştirdiği için
      // yalnızca o durumda ilk emisyonda da fetch gerekir; diğer hallerde çift istek olmaz.
      if (first) {
        first = false;
        if (!section) {
          return;
        }
      }
      this.fetch();
    });

    this.loadAssignments();
    if (this.isStudent) {
      this.loadStatusBuckets();
    }
  }

  // ---------- data loading ----------

  private buildFilter(): WorksheetListFilter {
    const duration = this.durationBucket() ? DURATION_BUCKET_RANGES[this.durationBucket()!] : undefined;
    const question = this.questionBucket() ? QUESTION_BUCKET_RANGES[this.questionBucket()!] : undefined;
    return {
      search: this.search() || undefined,
      subjectIds: this.selectedSubjectIds(),
      gradeIds: this.selectedGradeIds(),
      statuses: this.isStudent ? this.selectedStatuses() : undefined,
      minDurationSeconds: duration?.min,
      maxDurationSeconds: duration?.max,
      minQuestionCount: question?.min,
      maxQuestionCount: question?.max,
      isPracticeTest: this.practiceOnly() ? true : undefined,
      includeShared: this.isTeacher && this.includeShared() ? true : undefined,
      sortBy: this.sortBy(),
      sortDir: this.sortBy() === 'alphabetical' ? 'asc' : undefined,
      pageNumber: this.pageNumber(),
      pageSize: this.pageSize(),
    };
  }

  fetch(): void {
    if (this.tab() !== 'discover') {
      return;
    }
    this.fetchTrigger.next();
  }

  /** Aktif sekmenin veri kaynağını yeniden yükler (error dalındaki "Tekrar dene"). */
  retry(): void {
    switch (this.tab()) {
      case 'discover':
        this.fetch();
        break;
      case 'assigned':
        this.loadAssignments();
        break;
      default:
        this.loadStatusBuckets();
        break;
    }
  }

  private loadAssignments(): void {
    this.assignmentsLoading.set(true);
    this.assignmentsError.set(null);
    this.testService
      .getActiveAssignments()
      .pipe(
        finalize(() => this.assignmentsLoading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (items) => this.assignments.set(items ?? []),
        error: () => this.assignmentsError.set('Atanmış testler yüklenirken bir sorun oluştu.'),
      });
  }

  /** Öğrenci durum sekmeleri (Devam Edenler / Tamamlananlar) + devam şeridi için ayrı fetch. */
  private loadStatusBuckets(): void {
    this.bucketsLoading.set(true);
    this.bucketsError.set(null);
    forkJoin({
      inProgress: this.testService
        .listWorksheets({ statuses: [0], pageSize: 50, sortBy: 'recent' })
        .pipe(catchError(() => of(null))),
      completed: this.testService
        .listWorksheets({ statuses: [1], pageSize: 50, sortBy: 'recent' })
        .pipe(catchError(() => of(null))),
    })
      .pipe(
        finalize(() => this.bucketsLoading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe(({ inProgress, completed }) => {
        if (inProgress) {
          this.inProgressTests.set(inProgress.items ?? []);
        }
        if (completed) {
          this.completedTests.set(completed.items ?? []);
        }
        if (!inProgress && !completed) {
          this.bucketsError.set('Testler yüklenirken bir sorun oluştu.');
        }
      });
  }

  // ---------- shell interactions ----------

  setViewMode(mode: 'grid' | 'list'): void {
    if (this.viewMode() === mode) {
      return;
    }
    this.viewMode.set(mode);
    this.pageNumber.set(1);
    this.fetch();
  }

  setTab(tab: WorksheetListTab): void {
    if (this.tab() === tab) {
      return;
    }
    this.tab.set(tab);
    this.pageNumber.set(1);
    this.selectedIds.set(new Set());
    if (tab === 'discover') {
      this.fetch();
    } else if (this.isStudent) {
      this.loadStatusBuckets();
    }
  }

  setSort(sort: WorksheetSortBy): void {
    if (this.sortBy() === sort) {
      return;
    }
    this.sortBy.set(sort);
    this.pageNumber.set(1);
    this.fetch();
  }

  currentSortLabel(): string {
    return this.sortOptions.find((o) => o.value === this.sortBy())?.label ?? 'Sırala';
  }

  changePage(page: number): void {
    this.pageNumber.set(page);
    this.fetch();
    if (typeof window !== 'undefined') {
      window.scrollTo({ top: 0, behavior: 'smooth' });
    }
  }

  toggleMobileFilters(): void {
    this.showMobileFilters.update((v) => !v);
  }

  // ---------- filters ----------

  private toggleInArray<T>(sig: { (): T[]; set(v: T[]): void }, value: T): void {
    const current = sig();
    sig.set(current.includes(value) ? current.filter((v) => v !== value) : [...current, value]);
    this.pageNumber.set(1);
    this.fetch();
  }

  toggleSubject(id: number): void {
    this.toggleInArray(this.selectedSubjectIds, id);
  }

  toggleGrade(id: number): void {
    this.toggleInArray(this.selectedGradeIds, id);
  }

  toggleStatus(status: WorksheetStatus): void {
    this.toggleInArray(this.selectedStatuses, status);
  }

  setDurationBucket(bucket: DurationBucket): void {
    this.durationBucket.set(this.durationBucket() === bucket ? null : bucket);
    this.pageNumber.set(1);
    this.fetch();
  }

  setQuestionBucket(bucket: QuestionBucket): void {
    this.questionBucket.set(this.questionBucket() === bucket ? null : bucket);
    this.pageNumber.set(1);
    this.fetch();
  }

  togglePracticeOnly(): void {
    this.practiceOnly.update((v) => !v);
    this.pageNumber.set(1);
    this.fetch();
  }

  toggleIncludeShared(): void {
    this.includeShared.update((v) => !v);
    this.pageNumber.set(1);
    this.fetch();
  }

  clearFilters(): void {
    this.resetFilterSignals();
    this.fetch();
  }

  /** Boş-durum "Filtreleri temizle": filtre + arama tek fetch ile sıfırlanır. */
  resetAll(): void {
    this.resetFilterSignals();
    if (this.search()) {
      // navigation → queryParams aboneliği tek fetch tetikler
      this.clearSearch();
    } else {
      this.fetch();
    }
  }

  private resetFilterSignals(): void {
    this.selectedSubjectIds.set([]);
    this.selectedGradeIds.set([]);
    this.selectedStatuses.set([]);
    this.durationBucket.set(null);
    this.questionBucket.set(null);
    this.practiceOnly.set(false);
    this.pageNumber.set(1);
  }

  clearSearch(): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { search: null, section: null },
      queryParamsHandling: 'merge',
    });
  }

  isSubjectSelected(id: number): boolean {
    return this.selectedSubjectIds().includes(id);
  }

  isGradeSelected(id: number): boolean {
    return this.selectedGradeIds().includes(id);
  }

  isStatusSelected(status: WorksheetStatus): boolean {
    return this.selectedStatuses().includes(status);
  }

  statusLabel(status: WorksheetStatus): string {
    return status === -1 ? 'Başlanmadı' : status === 0 ? 'Devam ediyor' : 'Tamamlandı';
  }

  subjectName(id?: number): string {
    return id != null ? this.subjectMap().get(id) ?? '' : '';
  }

  gradeName(id?: number): string {
    return id != null ? this.gradeMap().get(id) ?? '' : '';
  }

  durationBucketLabel(bucket: DurationBucket): string {
    return DURATION_BUCKET_RANGES[bucket].label;
  }

  questionBucketLabel(bucket: QuestionBucket): string {
    return QUESTION_BUCKET_RANGES[bucket].label;
  }

  // ---------- teacher / bulk actions ----------

  assignmentFor(worksheetId: number | null): AssignedWorksheet | null {
    return worksheetId != null ? this.assignmentByWorksheetId().get(worksheetId) ?? null : null;
  }

  onAssign(): void {
    this.snackBar.open('Atama ekranı yakında kullanıma açılacak.', 'Tamam', { duration: 3000 });
  }

  onImportExcel(): void {
    this.snackBar.open('Excel içe aktarma yakında kullanıma açılacak.', 'Tamam', { duration: 3000 });
  }

  onCreate(): void {
    this.router.navigate(['/exam']);
  }

  onDeleteWorksheet(id: number): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      width: '480px',
      maxWidth: '90vw',
      data: {
        title: 'Testi Sil',
        message: 'Bu testi silmek istediğinize emin misiniz? Bu işlem geri alınamaz.',
        confirmText: 'Evet, Sil',
        cancelText: 'İptal',
        icon: 'delete_forever',
        confirmColor: 'warn',
      },
    });
    ref.afterClosed().subscribe((ok) => {
      if (ok) {
        this.performDelete([id]);
      }
    });
  }

  toggleRowSelection(id: number): void {
    const next = new Set(this.selectedIds());
    next.has(id) ? next.delete(id) : next.add(id);
    this.selectedIds.set(next);
  }

  isRowSelected(id: number): boolean {
    return this.selectedIds().has(id);
  }

  clearSelection(): void {
    this.selectedIds.set(new Set());
  }

  bulkDelete(): void {
    const ids = [...this.selectedIds()];
    if (ids.length === 0) {
      return;
    }
    const ref = this.dialog.open(ConfirmDialogComponent, {
      width: '480px',
      maxWidth: '90vw',
      data: {
        title: `${ids.length} test silinsin mi?`,
        message: 'Seçili testlerin tümü kalıcı olarak silinecek. Bu işlem geri alınamaz.',
        confirmText: 'Evet, Sil',
        cancelText: 'İptal',
        icon: 'delete_forever',
        confirmColor: 'warn',
      },
    });
    ref.afterClosed().subscribe((ok) => {
      if (ok) {
        this.performDelete(ids);
      }
    });
  }

  private performDelete(ids: number[]): void {
    this.discoverLoading.set(true);
    from(ids)
      .pipe(
        mergeMap(
          (id) =>
            this.testService.delete(id).pipe(
              map(() => true),
              catchError(() => of(false))
            ),
          4
        ),
        reduce((acc, ok) => (ok ? { ...acc, ok: acc.ok + 1 } : { ...acc, fail: acc.fail + 1 }), { ok: 0, fail: 0 }),
        finalize(() => this.discoverLoading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe(({ ok, fail }) => {
        const message = fail === 0 ? `${ok} test silindi.` : `${ok} test silindi, ${fail} tanesi başarısız.`;
        this.snackBar.open(message, fail === 0 ? 'Tamam' : 'Kapat', { duration: 4000 });
        this.clearSelection();
        if (ok > 0 && this.paged().items.length === ok && this.pageNumber() > 1) {
          this.pageNumber.update((p) => p - 1);
        }
        this.fetch();
      });
  }

  // ---------- helpers ----------

  trackTest = (_: number, test: Test): number => test.id ?? _;

  goToWorksheet(worksheetId: number): void {
    this.router.navigate(['/test', worksheetId]);
  }

  private mapAssignmentToTest(a: AssignedWorksheet): Test {
    return {
      id: a.worksheetId,
      name: a.name,
      description: a.description,
      gradeId: a.gradeId,
      maxDurationSeconds: a.maxDurationSeconds,
      isPracticeTest: a.isPracticeTest,
      imageUrl: a.imageUrl ?? undefined,
      subtitle: a.subtitle ?? undefined,
      badgeText: a.badgeText ?? undefined,
      bookId: a.bookId ?? undefined,
      bookTestId: a.bookTestId ?? undefined,
      questionCount: a.questionCount,
      subjectId: a.subjectId ?? undefined,
      topicId: a.topicId ?? undefined,
      subTopicId: a.subTopicId ?? undefined,
      instanceCount: 0,
    };
  }

}
