import {
  Component,
  inject,
  OnInit,
  Input,
  Output,
  EventEmitter,
  DestroyRef,
  signal,
  ChangeDetectionStrategy
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { BookService } from '../../services/book.service';
import { GradesService } from '../../services/grades.service';
import { SubjectService } from '../../services/subject.service';
import { MatSnackBar } from '@angular/material/snack-bar';

import { MatIconModule } from '@angular/material/icon';
import { MatSnackBarModule } from '@angular/material/snack-bar';
import { TestFormComponent } from '../test-form/test-form.component';
import { TestService } from '../../services/test.service';
import { Test, WorksheetStudentVisibility, WorksheetTeacherSharing } from '../../models/test-instance';
import { ActivatedRoute, NavigationExtras, Router } from '@angular/router';
import { EMPTY, Observable, of, switchMap, tap, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import {
  VisibilityChange,
  VisibilitySectionComponent,
} from '../../shared/components/visibility-section/visibility-section.component';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { createStorageImageResolver } from '../../shared/utils/storage-image-refresh.util';
import { StorageImageRetryDirective } from '../../shared/directives/storage-image-retry.directive';

/**
 * Test oluşturma/düzenleme akışının çevirileri tek scope'ta toplanır (issue #183):
 * `public/i18n/test-create/<lang>.json`. `app-test-form` alt komponenti bunu devralır.
 */
const TEST_CREATE_SCOPE = 'test-create';

@Component({
  selector: 'app-test-create-enhanced',
  standalone: true,
  templateUrl: './test-create-enhanced.component.html',
  styleUrls: ['./test-create-enhanced.component.scss'],
  imports: [
    MatIconModule,
    MatSnackBarModule,
    TestFormComponent,
    VisibilitySectionComponent,
    TranslocoDirective,
    StorageImageRetryDirective
],
  changeDetection: ChangeDetectionStrategy.Eager,
  providers: [provideTranslocoScope(TEST_CREATE_SCOPE)],
})
export class TestCreateEnhancedComponent implements OnInit {
  private readonly transloco = inject(TranslocoService);

  constructor() {
    this.warmTranslationScope();
  }

  /**
   * Şablon dışından (snackbar, dialog, hesaplanan etiket) çağrılan senkron `translate()`
   * çağrılarının çalışabilmesi için scope sözlüğünü render'dan bağımsız yükler.
   */
  private warmTranslationScope(): void {
    this.transloco
      .selectTranslate('snackbar.close', {}, TEST_CREATE_SCOPE)
      .pipe(takeUntilDestroyed())
      .subscribe();
  }

  /** Scope'lu anahtarı tam adıyla senkron çevirir; şablon yardımcıları `*transloco` bloğu içinde çalışır. */
  private tr(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${TEST_CREATE_SCOPE}.${key}`, params) ?? '';
  }

  @Input() mode: string = 'default'; // 'default' | 'miniform'
  /** miniform modunda "Oluştur ve devam et" sonrası tetiklenir (examId). */
  @Output() createdAndContinue = new EventEmitter<number>();

  get isMiniform(): boolean {
    return this.mode === 'miniform';
  }

  creatingInline = false;

  /** miniform: hızlı test oluştur ve devam et. */
  createAndContinue(): void {
    if (this.creatingInline) {
      return;
    }
    this.creatingInline = true;
    this.onCreateAsync(false)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (res) => {
          this.creatingInline = false;
          this.snackBar.open(this.tr('snackbar.testCreated'), this.tr('snackbar.close'), { duration: 2000 });
          // NOT: reloadComponent çağırma — parent (onQuickCreated) form senkronunu yönetir,
          // aksi halde testForm FormGroup yeniden kurulup parent'ın patch'lediği referans uçar.
          this.createdAndContinue.emit(res.examId);
        },
        error: (err) => {
          this.creatingInline = false;
          console.error('createAndContinue failed:', err);
          this.snackBar.open(this.toUserErrorMessage(err), this.tr('snackbar.close'), { duration: 3000 });
        },
      });
  }

  /** Ham backend hata gövdesini kullanıcıya basmadan okunur mesaja çevirir. */
  private toUserErrorMessage(err: any): string {
    if (err?.status >= 500) {
      return this.tr('snackbar.genericError');
    }
    if (typeof err?.error === 'string') {
      return err.error;
    }
    return err?.error?.message || err?.message || this.tr('snackbar.createFailed');
  }

  id!: number | null;
  exam!: Test;
  isEditMode: boolean = false;
  /** Yeni oluşturmada herkes düzenleyebilir; düzenleme modunda backend `canEdit` alanı belirler. */
  readonly canEdit = signal(true);
  readonly loadError = signal<string | null>(null);
  /** Görünürlük eksenleri (issue #10) — yalnız düzenleme modunda backend'den doldurulur. */
  readonly teacherSharing = signal<WorksheetTeacherSharing>(WorksheetTeacherSharing.Private);
  readonly studentVisibility = signal<WorksheetStudentVisibility>(WorksheetStudentVisibility.Normal);
  readonly isSavingVisibility = signal(false);
  testForm!: FormGroup;
  books: any[] = [];
  bookTests: any[] = [];
  grades: any[] = [];
  subjects: any[] = [];
  topics: any[] = [];
  subtopics: any[] = [];
  showAddBookInput = false;
  showAddBookTestInput = false;
  testService = inject(TestService);
  /**
   * issue #365 (S3): kayıtlı kapak görseli yüklenemezse (imzalı URL'nin süresi doldu) worksheet yeniden çekilir ve
   * taze imzalı URL ile bir kez yeniden denenir. Yeni seçilen dosyanın önizlemesi (blob:/data:) etkilenmez.
   */
  readonly coverImageRefresh = createStorageImageResolver(
    () => (this.id ? this.testService.get(this.id) : EMPTY),
    { cacheKey: () => this.id }
  );

  bookService = inject(BookService);
  gradeService = inject(GradesService);
  subjectService = inject(SubjectService);
  snackBar = inject(MatSnackBar);
  fb = inject(FormBuilder);
  route = inject(ActivatedRoute);
  router = inject(Router);
  destroyRef = inject(DestroyRef);

  /** Kapak görseli yüklemesi için seçili dosya önizlemesi. */
  imagePreviewUrl: string | null = null;
  isImageUploading = false;

  ngOnInit(): void {
    this.id = this.route.snapshot.paramMap.get('id') ? Number(this.route.snapshot.paramMap.get('id')) : null;
    if (!this.id) {
      const navigation = this.router.currentNavigation();
      this.id = history.state.testValue;
    }
    this.reloadComponent(this.id!);
  }

  public set thisId(value: number | null) {
    this.id = value;
  }

  public reloadComponent(id: number) {
    this.thisId = id;
    this.isEditMode = this.id !== null && this.id !== undefined && this.id > 0;
    this.canEdit.set(true);
    this.testForm = this.fb.group({
      id: [this.id || null],
      name: ['', Validators.required],
      description: [''],
      gradeId: ['', Validators.required],
      maxDurationMinutes: [10, [Validators.required, Validators.min(1)]],
      isPracticeTest: [false],
      // Issue #105: öğrenci yorum/soru anahtarı — varsayılan açık.
      commentsEnabled: [true],
      subtitle: [''],
      imageUrl: [''],
      newBookName: [''],
      newBookTestName: [''],
      bookTestId: [''],
      bookId: [''],
      subjectId: [''],
      topicId: [''],
      subtopicId: [''],
      questionCount: [0],
    });
    // Issue #7: sınıf seçilmeden ders/konu/alt konu seçilemez.
    this.applyClassificationEnabledState();
    this.testForm
      .get('gradeId')!
      .valueChanges.pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((gradeId) => {
        if (gradeId) {
          ['subjectId', 'topicId', 'subtopicId'].forEach((n) => this.testForm.get(n)?.enable({ emitEvent: false }));
        } else {
          this.testForm.patchValue({ subjectId: '', topicId: '', subtopicId: '' }, { emitEvent: false });
          ['subjectId', 'topicId', 'subtopicId'].forEach((n) => this.testForm.get(n)?.disable({ emitEvent: false }));
        }
      });

    this.loadBooks();
    this.loadGrades();
    this.loadSubjects();
    if (this.isEditMode) {
      this.loadTest();
    }
  }

  /** Issue #7: gradeId yoksa ders/konu/alt konu kontrollerini disable, varsa enable eder. */
  private applyClassificationEnabledState(): void {
    if (!this.testForm) {
      return;
    }
    if (this.isEditMode && !this.canEdit()) {
      return;
    }
    const hasGrade = !!this.testForm.get('gradeId')?.value;
    ['subjectId', 'topicId', 'subtopicId'].forEach((name) => {
      const control = this.testForm.get(name);
      if (!control) {
        return;
      }
      if (hasGrade && control.disabled) {
        control.enable({ emitEvent: false });
      } else if (!hasGrade && control.enabled) {
        control.disable({ emitEvent: false });
      }
    });
  }

  public syncClassification(subjectId: number | null, topicId: number | null, subtopicId: number | null) {
    if (!this.testForm) {
      return;
    }

    const gradeId = this.testForm.value.gradeId;

    // Normalize to form-friendly values
    const sId = subjectId && subjectId > 0 ? subjectId : '';
    const tId = topicId && topicId > 0 ? topicId : '';
    const stId = subtopicId && subtopicId > 0 ? subtopicId : '';

    // If no subject selected, clear dependent lists + values.
    if (!sId) {
      this.topics = [];
      this.subtopics = [];
      this.testForm.patchValue({ subjectId: '', topicId: '', subtopicId: '' }, { emitEvent: false });
      return;
    }

    // Patch subject immediately; then load topics, and only after that set topic/subtopic.
    this.testForm.patchValue({ subjectId: sId }, { emitEvent: false });

    if (!gradeId) {
      // Without grade, we can't fetch topics; still apply values to keep UI state consistent.
      this.testForm.patchValue({ topicId: tId, subtopicId: stId }, { emitEvent: false });
      return;
    }

    this.subjectService
      .getTopicsBySubjectAndGrade(sId, gradeId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((topics) => {
        this.topics = topics;

        // Set topic after list is loaded.
        this.testForm.patchValue({ topicId: tId }, { emitEvent: false });

        if (!tId) {
          this.subtopics = [];
          this.testForm.patchValue({ subtopicId: '' }, { emitEvent: false });
          return;
        }

        this.subjectService
          .getSubTopicsByTopic(tId)
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe((subtopics) => {
            this.subtopics = subtopics;
            this.testForm.patchValue({ subtopicId: stId }, { emitEvent: false });
          });
      });
  }

  onImageSelected(event: Event) {
    if (!this.canEdit()) {
      return;
    }
    const input = event.target as HTMLInputElement;
    const file = input.files && input.files[0];
    if (!file) {
      return;
    }
    if (!file.type.startsWith('image/')) {
      this.snackBar.open(this.tr('snackbar.pickImage'), this.tr('snackbar.close'), { duration: 3000 });
      input.value = '';
      return;
    }
    if (file.size > 2 * 1024 * 1024) {
      this.snackBar.open(this.tr('snackbar.imageTooLarge'), this.tr('snackbar.close'), { duration: 3000 });
      input.value = '';
      return;
    }

    const reader = new FileReader();
    reader.onload = (e) => {
      this.imagePreviewUrl = (e.target?.result as string) ?? null;
    };
    reader.readAsDataURL(file);

    if (this.isEditMode && this.id) {
      const previousImageUrl: string | null = this.testForm.value.imageUrl || null;
      this.isImageUploading = true;
      this.testService
        .updateWorksheetBackgroundImage(this.id, file)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (res) => {
            this.isImageUploading = false;
            if (res?.imageUrl) {
              this.testForm.patchValue({ imageUrl: res.imageUrl });
              this.imagePreviewUrl = res.imageUrl;
            }
            this.snackBar.open(this.tr('snackbar.coverUpdated'), this.tr('snackbar.close'), { duration: 2500 });
          },
          error: () => {
            this.isImageUploading = false;
            this.imagePreviewUrl = previousImageUrl;
            this.snackBar.open(this.tr('snackbar.coverFailed'), this.tr('snackbar.close'), { duration: 3000 });
          },
        });
    }
    input.value = '';
  }

  /** Görünürlük bileşeninden gelen değişikliği bağımsız PUT ile kaydeder (issue #10). */
  onVisibilityChange(change: VisibilityChange): void {
    if (!this.isEditMode || !this.id || !this.canEdit() || this.isSavingVisibility()) {
      return;
    }
    const previousTeacherSharing = this.teacherSharing();
    const previousStudentVisibility = this.studentVisibility();
    // İyimser güncelleme: alt bileşenin gösterdiği değer parent signal'a hemen yansır; hata dalındaki
    // `set(previous)` böylece gerçekten bir değişiklik olur ve input üzerinden alt bileşene akar
    // (aksi hâlde no-op kalır, radio "Sadece okulum"da takılırken sunucu eski değerde olur).
    this.teacherSharing.set(change.teacherSharing);
    this.studentVisibility.set(change.studentVisibility);
    this.isSavingVisibility.set(true);
    this.testService
      .updateVisibility(this.id, change)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.isSavingVisibility.set(false);
          this.teacherSharing.set(updated.teacherSharing ?? change.teacherSharing);
          this.studentVisibility.set(updated.studentVisibility ?? change.studentVisibility);
          this.snackBar.open(this.tr('snackbar.visibilityUpdated'), this.tr('snackbar.close'), { duration: 2000 });
        },
        error: (err: HttpErrorResponse) => {
          this.isSavingVisibility.set(false);
          this.teacherSharing.set(previousTeacherSharing);
          this.studentVisibility.set(previousStudentVisibility);
          if (err?.status === 403) {
            this.snackBar.open(this.tr('snackbar.visibilityForbidden'), this.tr('snackbar.close'), {
              duration: 3000,
            });
          } else if (err?.status === 404) {
            this.snackBar.open(this.tr('snackbar.testNotFound'), this.tr('snackbar.close'), { duration: 3000 });
          } else if (err?.status === 400 && typeof err.error?.message === 'string' && err.error.message) {
            // issue #191: backend doğrulama mesajı (örn. okulsuz sahip "Sadece okulum" seçti) localize gelir.
            this.snackBar.open(err.error.message, this.tr('snackbar.close'), { duration: 4000 });
          } else {
            this.snackBar.open(this.tr('snackbar.visibilityFailed'), this.tr('snackbar.close'), { duration: 3000 });
          }
        },
      });
  }

  loadTest() {
    this.loadError.set(null);
    this.testService
      .get(this.id!)
      .pipe(
        tap((exam) => {
          // `get()` erişilemeyen/olmayan worksheet'te boş liste döndürür → undefined.
          if (!exam) {
            throw new HttpErrorResponse({ status: 404 });
          }
          // Yetkiyi cevap gelir gelmez uygula — alt çağrılar hata verse bile
          // sahibi olmayan kullanıcı düzenlenebilir form görmesin.
          this.canEdit.set(exam.canEdit !== false);
          if (!this.canEdit()) {
            this.testForm.disable({ emitEvent: false });
          }
          this.teacherSharing.set(exam.teacherSharing ?? WorksheetTeacherSharing.Private);
          this.studentVisibility.set(exam.studentVisibility ?? WorksheetStudentVisibility.Normal);
        }),
        switchMap((exam) =>
          this.bookService.getTestsByBook(exam.bookId || 0).pipe(
            switchMap((bookTests) => {
              this.bookTests = bookTests;
              this.testForm.patchValue(
                {
                  bookTestId: null,
                  newBookTestName: '',
                  name: exam.name,
                  description: exam.description,
                  gradeId: exam.gradeId,
                  maxDurationMinutes: exam.maxDurationSeconds / 60,
                  isPracticeTest: exam.isPracticeTest,
                  commentsEnabled: exam.commentsEnabled !== false,
                  subtitle: exam.subtitle,
                  imageUrl: exam.imageUrl,
                  bookId: exam.bookId,
                  questionCount: exam.questionCount,
                },
                { emitEvent: false }
              );
              this.testForm.patchValue({ bookTestId: exam.bookTestId ?? null }, { emitEvent: false });
              this.imagePreviewUrl = exam.imageUrl || null;

              return this.subjectService.getSubjectsByGrade(exam.gradeId!).pipe(
                switchMap((subjects) => {
                  this.subjects = subjects;
                  this.testForm.patchValue({ subjectId: exam.subjectId || null }, { emitEvent: false });

                  if (!(exam.subjectId && exam.gradeId)) {
                    this.topics = [];
                    this.subtopics = [];
                    this.testForm.patchValue({ topicId: null, subtopicId: null }, { emitEvent: false });
                    return of(null);
                  }

                  return this.subjectService.getTopicsBySubjectAndGrade(exam.subjectId, exam.gradeId).pipe(
                    switchMap((topics) => {
                      this.topics = topics;
                      this.testForm.patchValue(
                        { topicId: exam.topicId ?? null, subtopicId: null },
                        { emitEvent: false }
                      );
                      return this.subjectService.getSubTopicsByTopic(exam.topicId || 0).pipe(
                        tap((subtopics) => {
                          this.subtopics = subtopics;
                          this.testForm.patchValue({ subtopicId: exam.subTopicId || null }, { emitEvent: false });
                        })
                      );
                    })
                  );
                })
              );
            })
          )
        ),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: () => {
          // Edit modunda grade patchValue emitEvent:false ile geldiği için valueChanges tetiklenmez;
          // enable/disable durumunu burada elle uygula.
          this.applyClassificationEnabledState();
        },
        error: (err) => {
          if (err?.status === 404) {
            this.snackBar.open(this.tr('snackbar.noAccess'), this.tr('snackbar.close'), { duration: 4000 });
            this.router.navigate(['/tests']);
            return;
          }
          this.loadError.set(this.tr('snackbar.loadFailed'));
          this.snackBar.open(this.tr('snackbar.loadFailed'), this.tr('snackbar.close'), { duration: 3000 });
        },
      });
  }

  loadBooks() {
    this.bookService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((data) => {
        this.books = data;
      });
  }
  loadGrades() {
    this.gradeService
      .getGrades()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((data) => {
        this.grades = data;
      });
  }
  loadSubjects(gradeId?: number) {
    const source$ = gradeId
      ? this.subjectService.getSubjectsByGrade(gradeId)
      : this.subjectService.loadCategories();
    source$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((data) => {
      this.subjects = data;
    });
  }
  onBookChange(bookId: any) {
    this.showAddBookInput = false;
    this.bookService
      .getTestsByBook(bookId.value)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((data) => {
        this.bookTests = data;
        this.testForm.patchValue({ bookTestId: null, newBookTestName: '' }, { emitEvent: false });
      });
  }
  openNewBookAdd() {
    this.showAddBookInput = true;
    this.testForm.patchValue({ bookId: null }, { emitEvent: false });
    this.bookTests = [];
    this.openNewBookTestAdd();
  }
  onNewBookBlur() {
    const name = this.testForm.get('newBookName')?.value;
    if (!name) {
      this.showAddBookInput = false;
      this.showAddBookTestInput = false;
    }
  }
  openNewBookTestAdd() {
    this.showAddBookTestInput = true;
    this.testForm.patchValue({ bookTestId: null }, { emitEvent: false });
  }
  onSubjectChange(subjectId: any) {
    const gradeId = this.testForm.value.gradeId;
    if (subjectId && gradeId) {
      this.subjectService
        .getTopicsBySubjectAndGrade(subjectId, gradeId)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe((topics) => {
          this.topics = topics;
          this.testForm.patchValue({ topicId: null, subtopicId: null }, { emitEvent: false });
        });
    } else {
      this.topics = [];
      this.testForm.patchValue({ topicId: null, subtopicId: null }, { emitEvent: false });
    }
  }
  onTopicChange(topicId: any) {
    this.subjectService
      .getSubTopicsByTopic(topicId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((subtopics) => {
        this.subtopics = subtopics;
        this.testForm.patchValue({ subtopicId: null }, { emitEvent: false });
      });
  }

  private createTestPayload(): Test {
    return {
      id: this.id,
      name: this.testForm.value.name,
      description: this.testForm.value.description,
      gradeId: this.testForm.value.gradeId,
      maxDurationSeconds: +this.testForm.value.maxDurationMinutes * 60,
      isPracticeTest: this.testForm.value.isPracticeTest,
      commentsEnabled: this.testForm.value.commentsEnabled !== false,
      imageUrl: this.testForm.value.imageUrl,
      subtitle: this.testForm.value.subtitle,
      newBookName: this.testForm.value.newBookName,
      newBookTestName: this.testForm.value.newBookTestName,
      bookTestId: !this.testForm.value.bookTestId ? 0 : this.testForm.value.bookTestId,
      bookId: !this.testForm.value.bookId ? 0 : this.testForm.value.bookId,
      subjectId: this.testForm.value.subjectId ? this.testForm.value.subjectId : null,
      topicId: this.testForm.value.topicId ? this.testForm.value.topicId : null,
      subTopicId: this.testForm.value.subtopicId ? this.testForm.value.subtopicId : null,
    };
  }

  onSubmit() {
    if (!this.testForm.valid) {
      this.snackBar.open(this.tr('snackbar.formInvalid'), this.tr('snackbar.close'), { duration: 2000 });
      return;
    }

    this.testService
      .create(this.createTestPayload())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((response) => {
        this.snackBar.open(
          this.tr(this.isEditMode ? 'snackbar.changesSaved' : 'snackbar.testCreated'),
          this.tr('snackbar.close'),
          { duration: 2000 }
        );
        if (this.isEditMode) {
          this.reloadComponent(response.examId);
        } else {
          this.router.navigate(['/exam', response.examId]);
        }
      });
  }

  public onCreateAsync(requireSubtopic: boolean = true): Observable<{ message: string; examId: number }> {
    var payload = this.createTestPayload();
    if (requireSubtopic && !payload.subTopicId) {
      // Observable error olarak dönmek için throwError kullanılır
      return throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            statusText: this.tr('snackbar.subtopicRequired'),
            error: this.tr('snackbar.subtopicRequired'),
          })
      );
    }
    payload = { ...payload, id: null }; // id'yi null yaparak yeni bir test oluşturulacağını belirt
    return this.testService.create(payload);
  }

  navigateToTestList() {
    // Örneğin bir router ile test listesine yönlendirme yapılabilir
    // this.router.navigate(['/test-list']);
    this.snackBar.open(this.tr('snackbar.navigatingToList'), this.tr('snackbar.close'), { duration: 2000 });
  }

  get getSelectedGradeName(): string {
    const selectedGrade = this.grades.find((grade) => grade.id === this.testForm.value.gradeId);
    return selectedGrade ? selectedGrade.name : this.tr('preview.gradePlaceholder');
  }

  private lookupName(list: any[], id: any): string {
    const found = list.find((x) => x.id === id);
    return found ? found.name : '';
  }

  get previewName(): string {
    return this.testForm?.value.name || this.tr('preview.defaultName');
  }

  get previewSubtitle(): string {
    return this.testForm?.value.subtitle || '';
  }

  get previewChips(): string[] {
    const v = this.testForm?.value ?? {};
    return [
      this.lookupName(this.grades, v.gradeId),
      this.lookupName(this.subjects, v.subjectId),
      this.lookupName(this.topics, v.topicId),
    ].filter((x) => !!x);
  }

  get previewDuration(): number {
    return +(this.testForm?.value.maxDurationMinutes || 0);
  }

  get previewQuestionCount(): number {
    return +(this.testForm?.value.questionCount || 0);
  }

  get previewIsPractice(): boolean {
    return !!this.testForm?.value.isPracticeTest;
  }

  get previewImage(): string | null {
    return this.imagePreviewUrl || this.testForm?.value.imageUrl || null;
  }

  navigateToQuestionCanvas() {
    // Form valid ise soru ekleme adımına geçiş
    if (this.testForm.valid) {
      // Eğer bir router ve testService varsa, aşağıdaki gibi kullanılabilir:
      // this.testService.create(this.createTestPayload()).subscribe(() => {
      //   const navigationExtras = { ... };
      //   this.router.navigate(['/questioncanvas'], navigationExtras);
      // });
      this.snackBar.open(this.tr('snackbar.navigatingToQuestions'), this.tr('snackbar.close'), { duration: 1000 });
      const navigationExtras: NavigationExtras = {
        state: {
          subjectId: null,
          topicId: null,
          subtopicId: null,
          testId: this.testForm.value.subtitle,
          bookId: this.testForm.value.bookId,
          bookTestId: this.testForm.value.bookTestId,
          testValue: this.id,
        },
      };
      setTimeout(() => {
        this.router.navigate(['/questioncanvas'], navigationExtras);
      }, 1000);
    } else {
      this.snackBar.open(this.tr('snackbar.formInvalid'), this.tr('snackbar.close'), { duration: 2000 });
    }
  }

  onGradeChange(gradeId: number) {
    // Filter subjects by grade
    this.subjectService.getSubjectsByGrade(gradeId).subscribe((subjects) => {
      this.subjects = subjects;
      // Reset subject, topic, subtopic fields in the form
      this.testForm.patchValue({
        subjectId: null,
        topicId: null,
        subtopicId: null,
      });
      this.topics = [];
      this.subtopics = [];
    });
  }
}
