import { Component, computed, effect, inject, OnInit, signal, ViewChild } from '@angular/core';
import { FormGroup, Validators, FormControl, ReactiveFormsModule, FormsModule } from '@angular/forms';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatSnackBarModule } from '@angular/material/snack-bar';
import { CommonModule } from '@angular/common';
import { MatDividerModule } from '@angular/material/divider';
import { SubjectService } from '../../services/subject.service';
import { QuestionService } from '../../services/question.service';
import { ActivatedRoute, Router } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { Test } from '../../models/test-instance';
import { TestService } from '../../services/test.service';
import { QuestionCanvasForm } from '../../models/question-form';
import { Book, BookTest } from '../../models/book';
import { BookService } from '../../services/book.service';
import { ImageSelectorComponent } from '../image-selector/image-selector.component';
import { debounceTime, of, switchMap } from 'rxjs';
import { SidenavService } from '../../services/sidenav.service';
import { MatMenuModule } from '@angular/material/menu';
import { toSignal } from '@angular/core/rxjs-interop';
import { TestCreateEnhancedComponent } from '../test-create-enhanced/test-create-enhanced.component';
import { ClassificationSource, QuestionRegion } from '../../models/draws';
@Component({
  selector: 'app-question-canvas',
  standalone: true,
  templateUrl: './question-canvas.component.html',
  styleUrls: ['./question-canvas.component.scss'],
  imports: [
    MatSnackBarModule,
    ReactiveFormsModule,
    FormsModule,
    CommonModule,
    MatMenuModule,
    MatDividerModule,
    MatIconModule,
    ImageSelectorComponent,
    TestCreateEnhancedComponent,
  ],
})
export class QuestionCanvasComponent implements OnInit {
  @ViewChild(ImageSelectorComponent) imageSelector!: ImageSelectorComponent; // 🔥 Alt bileşene erişim
  @ViewChild(TestCreateEnhancedComponent) testCreateEnhancedComponent!: TestCreateEnhancedComponent;

  id: number | null = null;
  isEditMode: boolean = false;
  resetTest: boolean = false;
  public autoMode = signal<boolean>(false);
  public autoAlign = signal<boolean>(false);
  public showSidePanel = signal<boolean>(true);
  /** Üst breadcrumb'daki "Yeni test oluştur" ile açılan hızlı oluşturma paneli. */
  public showQuickCreate = signal<boolean>(false);
  public inProgress = signal<boolean>(false);
  public previewModeText = signal<string>('visibility');
  public dropdownVisible = signal<boolean>(false);

  // Used by ImageSelector preview mode to send current classification updates along with correct-answer updates.
  public previewMetaProvider = () => {
    const toNullableNumber = (x: any): number | null => {
      const n = Number(x);
      return Number.isFinite(n) && n > 0 ? n : null;
    };

    // Prefer the local form (questionForm) if it has values; fall back to testCreateEnhancedComponent form.
    const q = this.questionForm?.value as any;
    const v = this.testCreateEnhancedComponent?.testForm?.value as any;

    const subjectId = toNullableNumber(q?.subjectId) ?? toNullableNumber(v?.subjectId);
    const topicId = toNullableNumber(q?.topicId) ?? toNullableNumber(v?.topicId);
    const subtopicId = toNullableNumber(q?.subtopicId) ?? toNullableNumber(v?.subtopicId);

    return {
      subjectId,
      topicId,
      subtopicId,
    };
  };
  router = inject(Router);
  route = inject(ActivatedRoute);
  questionService = inject(QuestionService);
  testService = inject(TestService);
  subjectService = inject(SubjectService);
  sidenavService = inject(SidenavService);
  snackBar = inject(MatSnackBar);
  questionForm: FormGroup = new FormGroup<QuestionCanvasForm>({
    subjectId: new FormControl(0, { nonNullable: true, validators: [Validators.required] }),
    topicId: new FormControl(0, { nonNullable: true, validators: [Validators.required] }),
    subtopicId: new FormControl(0, { nonNullable: true, validators: [Validators.required] }),
    isExample: new FormControl(false, { nonNullable: true, validators: [Validators.required] }),
    practiceCorrectAnswer: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    testId: new FormControl(0, { nonNullable: true, validators: [Validators.required] }),
    testValue: new FormControl(0, { nonNullable: true, validators: [Validators.required] }),
    bookId: new FormControl(0, { nonNullable: true, validators: [Validators.required] }),
    bookTestId: new FormControl(0, { nonNullable: true, validators: [Validators.required] }),
  });
  bookService = inject(BookService);
  bookTests: BookTest[] = [];
  booksSignal = toSignal(this.bookService.getAll(), { initialValue: [] as Book[] });

  readonly bookIdSignal = toSignal(this.questionForm.get('bookId')!.valueChanges, {
    initialValue: this.questionForm.get('bookId')!.value,
  });

  readonly bookTestIdSignal = toSignal(this.questionForm.get('bookTestId')!.valueChanges, {
    initialValue: this.questionForm.get('bookTestId')!.value,
  });

  readonly bookEffect = effect(() => {
    const books = this.booksSignal();
    const bookId = this.bookIdSignal();
  });

  readonly clearTestFieldsEffect = effect(() => {
    // Subscribe to both signals.
    const currentBookId = this.bookIdSignal();
    const currentBookTestId = this.bookTestIdSignal();

    // Whenever either changes, clear testId and testValue.
    if (!this.resetTest) {
      this.questionForm.get('testId')?.setValue(null, { emitEvent: false });
      this.questionForm.get('testValue')?.setValue(null, { emitEvent: false });
    }
    this.resetTest = false;
  });

  readonly searchResultSignal = toSignal(
    this.questionForm.get('testId')!.valueChanges.pipe(
      debounceTime(300),
      switchMap((searchValue) => {
        if (!searchValue) {
          return of({ items: [] });
        }
        return this.testService.search(searchValue, [], [], 1, 1000);
      })
    ),
    { initialValue: { items: [] } }
  );

  readonly filteredTestListSignal = computed(() => {
    const results = this.searchResultSignal();
    const bookTestId = this.questionForm.get('bookTestId')?.value;
    const testId = this.questionForm.get('testValue')?.value;
    const testText = this.questionForm.get('testId')?.value;
    return results.items.filter((test: Test) => {
      if (test.id == testId) return true;
      if (bookTestId) {
        return (
          test.bookTestId === +bookTestId &&
          (testText === '' || test.subtitle?.toLowerCase().includes(testText.toLowerCase()))
        );
      } else {
        return testText === '' || test.subtitle?.toLowerCase().includes(testText.toLowerCase());
      }
    });
  });

  fullScreen = signal(false);

  readonly bookTestsSignal = toSignal(
    this.questionForm.get('bookId')!.valueChanges.pipe(
      switchMap((bookId) => {
        if (bookId) {
          return this.bookService.getTestsByBook(bookId);
        }
        return of([] as BookTest[]);
      })
    ),
    { initialValue: [] as BookTest[] }
  );

  constructor() {
    this.setFullScreen(false);
  }

  setFullScreen(fullScreen: boolean) {
    this.fullScreen.set(fullScreen);
    this.sidenavService.setSidenavState(!fullScreen);
    this.sidenavService.setFullScreen(fullScreen);
  }

  downloadRegionsLite() {
    this.imageSelector.downloadRegionsLite();
  }

  // public isPreviewModeComputed = computed(() => (this.imageSelector ? this.imageSelector.previewMode() : true));

  /** Önizleme artık ayrı route değil — aynı komponentte mod. imageFiles state'i korunur. */
  get previewOn(): boolean {
    return this.imageSelector?.previewMode() ?? false;
  }

  /** Kayıtlı sorusu olan bir test var mı? (dosya yüklenmemiş olsa da önizleme yapılabilir) */
  get canPreview(): boolean {
    return !!this.id || Number(this.questionForm.value.testValue) > 0;
  }

  togglePreviewMode() {
    if (this.previewOn) {
      this.imageSelector.togglePreviewMode(0);
      return;
    }

    if (!this.canPreview) {
      return;
    }

    const testId = this.id || Number(this.questionForm.value.testValue);
    this.imageSelector.togglePreviewMode(Number(testId));
  }

  exitPreview() {
    if (this.previewOn) {
      this.imageSelector.togglePreviewMode(0);
    }
  }

  toggleOnlyQuestionMode() {
    this.imageSelector.toggleOnlyQuestionMode();
  }

  onPreviewQuestionChange(evt: {
    index: number;
    questionId: number;
    subjectId: number | null;
    topicId: number | null;
    subtopicId: number | null;
    classificationSource?: ClassificationSource;
  }) {
    const subjectId = evt.subjectId ?? 0;
    const topicId = evt.topicId ?? 0;
    const subtopicId = evt.subtopicId ?? 0;

    // Keep local form in sync (used by save flow + previewMetaProvider).
    this.questionForm.patchValue(
      {
        subjectId,
        topicId,
        subtopicId,
      },
      { emitEvent: false }
    );

    // Also sync the visible left-side taxonomy selectors.
    this.testCreateEnhancedComponent?.syncClassification?.(subjectId || null, topicId || null, subtopicId || null);
  }

  handleFilesInput2(event: Event) {
    this.imageSelector.handleFilesInput2(event);
  }

  get answerCount(): number {
    return this.imageSelector ? this.imageSelector.answerCount() : 4;
  }

  onChangeQuestionCount(event: any) {
    this.setAnswerCount(Number(event.target.value));
  }

  setAnswerCount(count: number) {
    this.imageSelector.answerCount.set(count);
    this.imageSelector.recomputeWarnings();
  }

  resetFormWithDefaultValues(state: any) {
    this.resetTest = true;
    this.questionForm.patchValue(
      {
        subjectId: state?.subjectId || 0,
        topicId: state?.topicId || 0,
        subtopicId: state?.subtopicId || 0,
        isExample: false,
        practiceCorrectAnswer: '',
        testId: state?.testId || '',
        testValue: state?.testValue || '',
        bookId: state?.bookId || '',
        bookTestId: state?.bookTestId || '',
      },
      { emitEvent: true }
    );

    this.questionForm.get('testId')?.setValue(state?.testId || '', { emitEvent: false });
  }

  onFocus() {
    this.dropdownVisible.set(true);
  }

  previousImage() {
    this.imageSelector.previousImage();
  }

  nextImage() {
    this.imageSelector.nextImage();
  }

  setAutoMode(checked: boolean) {
    this.autoMode.set(checked);
    this.imageSelector.autoMode.set(checked);
    if (checked) {
      this.nextImage();
    }
  }

  /** image-selector'daki autoAlign signal'i ile senkron gösterge. */
  get autoAlignOn(): boolean {
    return this.imageSelector ? this.imageSelector.autoAlign() : this.autoAlign();
  }

  get autoModeOn(): boolean {
    return this.imageSelector ? this.imageSelector.autoMode() : this.autoMode();
  }

  setAutoAlign(checked: boolean) {
    this.autoAlign.set(checked);
    this.imageSelector.autoAlign.set(checked);
    this.imageSelector.predict();
  }

  onBlur() {
    setTimeout(() => {
      this.dropdownVisible.set(false);
    }, 150);
  }

  loadBooks() {
    const books = this.booksSignal();
  }

  displayFn = (selectedoption: any): string => {
    return selectedoption ? selectedoption.name + '-' + selectedoption.subtitle : '';
  };

  onOptionSelected(event: any) {
    console.log(event);
    this.questionForm.get('testId')?.setValue(event.subtitle, { emitEvent: false });
    this.questionForm.get('testValue')?.setValue(event.id);
    this.dropdownVisible.set(false);
  }

  ngOnInit() {
    this.loadBooks();
    const navigation = this.router.getCurrentNavigation();
    const state = navigation?.extras.state as {
      subjectId?: number;
      topicId?: number;
      subtopicId?: number;
      testId?: number;
      bookId?: number;
      bookTestId?: number;
      testValue?: string;
    };
    this.resetFormWithDefaultValues(history.state);
    this.id = this.route.snapshot.paramMap.get('id') ? Number(this.route.snapshot.paramMap.get('id')) : null;
    this.isEditMode = this.id !== null;
  }

  sendToFix() {
    this.imageSelector.sendToFix();
  }

  // --- Yeni kabuk (redesign) yardımcıları ---

  readonly selectedBookLabel = computed(() => {
    const id = Number(this.bookIdSignal());
    const book = this.booksSignal().find((b) => b.id === id);
    return book?.name || 'Kitap seç';
  });

  readonly selectedBookTestLabel = computed(() => {
    const id = Number(this.bookTestIdSignal());
    const bt = this.bookTestsSignal().find((b) => b.id === id);
    return bt?.name || 'Kitap testi';
  });

  get selectedTestLabel(): string {
    return this.questionForm.get('testId')?.value || 'Test seç';
  }

  get pageFiles(): File[] {
    return this.imageSelector?.imageFiles ?? [];
  }

  get currentPageIndex(): number {
    return this.imageSelector?.currentImageIndex ?? 0;
  }

  selectBook(bookId: number) {
    this.questionForm.get('bookId')?.setValue(bookId);
  }

  selectBookTest(bookTestId: number) {
    this.questionForm.get('bookTestId')?.setValue(bookTestId);
  }

  openQuickCreate() {
    this.showQuickCreate.set(true);
    this.showSidePanel.set(true);
  }

  onQuickCreated(examId: number) {
    this.showQuickCreate.set(false);
    this.id = examId;
    this.testService.get(examId).subscribe((testData) => {
      this.questionForm.get('testId')?.setValue(testData.subtitle, { emitEvent: false });
      this.questionForm.get('testValue')?.setValue(testData.id, { emitEvent: false });
    });
  }

  get selectionMode(): 'passage' | 'question' | 'answer' | 'dropzone' | null {
    return this.imageSelector?.selectionMode() ?? null;
  }

  setSelectionMode(mode: 'question' | 'answer' | 'passage' | 'dropzone') {
    this.imageSelector.lockSelectionMode(this.selectionMode === mode ? null : mode);
  }

  triggerPredict() {
    this.imageSelector.predict();
  }

  triggerAlign() {
    this.imageSelector.autoAlign.set(true);
    this.imageSelector.predict();
  }

  goToPage(pageStr: string) {
    this.imageSelector.goToPage(pageStr);
  }

  toggleInspector() {
    this.showSidePanel.set(!this.showSidePanel());
  }

  askAi() {
    this.imageSelector.predict();
  }

  // ---------------- Soru Müfettişi (sağ panel) ----------------

  get insRegion(): QuestionRegion | null {
    return this.imageSelector?.currentRegion ?? null;
  }

  get insRegionIndex(): number | null {
    return this.imageSelector?.activeRegionIndex ?? null;
  }

  get insRegionNo(): number {
    return (this.insRegionIndex ?? 0) + 1;
  }

  get insRegionName(): string {
    return this.insRegion?.name ?? '';
  }

  get insInteractionType(): 'mcq' | 'dragDropLabeling' {
    const name = this.insRegionName;
    return name ? this.imageSelector.getInteractionType(name) : 'mcq';
  }

  get insWarnings(): string[] {
    const region = this.insRegion;
    if (!region || !this.imageSelector) {
      return [];
    }
    return this.imageSelector.warningMarkers
      .filter((m) => m.id === region.id)
      .flatMap((m) => m.messages);
  }

  get insDifficulty(): number | null {
    return this.imageSelector?.previewDifficultyLevel() ?? null;
  }

  insSelectNext() {
    this.imageSelector.selectNextQuestion();
  }

  insSelectPrevious() {
    this.imageSelector.selectPreviousQuestion();
  }

  insSetInteractionType(type: 'mcq' | 'dragDropLabeling') {
    const name = this.insRegionName;
    if (!name) {
      return;
    }
    this.imageSelector.onInteractionTypeChange({ target: { value: type } } as unknown as Event, name);
  }

  insSetCorrectAnswer(answerIndex: number) {
    const idx = this.insRegionIndex;
    if (idx == null) {
      return;
    }
    this.imageSelector.setCorrectAnswer(idx, answerIndex, 1);
  }

  insAlignAnswers() {
    const idx = this.insRegionIndex;
    if (idx != null) {
      this.imageSelector.alignAnswers(idx, true);
    }
  }

  insDrawAnswerMode() {
    const idx = this.insRegionIndex;
    if (idx != null) {
      this.imageSelector.selectAnswerMode(idx);
    }
  }

  insDropZoneMode() {
    const idx = this.insRegionIndex;
    if (idx != null) {
      this.imageSelector.selectDropZoneMode(idx);
    }
  }

  insGetLabelCount(): number {
    const name = this.insRegionName;
    return name ? this.imageSelector.getLabelCount(name) : 0;
  }

  insSetLabelCount(value: string) {
    const name = this.insRegionName;
    if (name) {
      this.imageSelector.setLabelCount(name, value);
    }
  }

  insGenerateDraggables() {
    const name = this.insRegionName;
    if (name) {
      this.imageSelector.generateDraggables(name);
    }
  }

  insAutoSolution() {
    const name = this.insRegionName;
    if (name) {
      this.imageSelector.autoAssignSolutionSequential(name);
    }
  }

  insDropZones() {
    const name = this.insRegionName;
    return name ? this.imageSelector.getDropZones(name) : [];
  }

  insDraggables() {
    const name = this.insRegionName;
    return name ? this.imageSelector.getDraggables(name) : [];
  }

  insZoneAssigned(zoneId: string): string {
    const name = this.insRegionName;
    return name ? this.imageSelector.getZoneAssignedDraggable(name, zoneId) : '0';
  }

  insSetZoneSolution(zoneId: string, draggableId: string) {
    const name = this.insRegionName;
    if (name) {
      this.imageSelector.setZoneSolution(name, zoneId, draggableId);
    }
  }

  get insIsExample(): boolean {
    const name = this.insRegionName;
    return name ? this.imageSelector.isExample(name) : false;
  }

  insToggleExample() {
    const name = this.insRegionName;
    if (name) {
      this.imageSelector.toggleExampleMode(name);
    }
  }

  get insExampleAnswer(): string {
    const name = this.insRegionName;
    return name ? this.imageSelector.getExampleAnswer(name) : '';
  }

  set insExampleAnswer(value: string) {
    const name = this.insRegionName;
    if (name) {
      this.imageSelector.onTextChanged(name, value);
    }
  }

  get insPassages() {
    return this.imageSelector?.passages() ?? [];
  }

  get insSelectedPassageId(): string {
    const name = this.insRegionName;
    return (name && this.imageSelector?.selectedPassageMap.get(name)) || '0';
  }

  insChangePassage(event: Event) {
    const name = this.insRegionName;
    if (name) {
      this.imageSelector.onPassageChange(event, name);
    }
  }

  get insPassageFirst(): boolean {
    return this.imageSelector?.isPassageFirstEnabledForPassage(this.insSelectedPassageId) ?? false;
  }

  insTogglePassageFirst() {
    const passageId = this.insSelectedPassageId;
    if (passageId && passageId !== '0') {
      this.imageSelector.toggleShowPassageFirstForPassage(passageId);
    }
  }

  insDismissWarning(event: MouseEvent) {
    this.imageSelector.dismissWarning(event);
  }

  insRemoveQuestion() {
    const idx = this.insRegionIndex;
    if (idx != null) {
      this.imageSelector.removeQuestion(idx);
    }
  }

  // --- Sınıflandırma: quick-create formundan gelen grade ile 3 dropdown ---

  private get tce() {
    return this.testCreateEnhancedComponent;
  }

  get insSubjects(): any[] {
    return this.tce?.subjects ?? [];
  }

  get insTopics(): any[] {
    return this.tce?.topics ?? [];
  }

  get insSubtopics(): any[] {
    return this.tce?.subtopics ?? [];
  }

  get insSubjectId(): any {
    return this.tce?.testForm?.value?.subjectId ?? '';
  }
  set insSubjectId(value: any) {
    const v = value ? Number(value) : '';
    this.tce?.testForm?.patchValue({ subjectId: v }, { emitEvent: false });
    this.tce?.onSubjectChange(v);
    this.pushInsClassificationToForm();
  }

  get insTopicId(): any {
    return this.tce?.testForm?.value?.topicId ?? '';
  }
  set insTopicId(value: any) {
    const v = value ? Number(value) : '';
    this.tce?.testForm?.patchValue({ topicId: v }, { emitEvent: false });
    if (v) {
      this.tce?.onTopicChange(v);
    }
    this.pushInsClassificationToForm();
  }

  get insSubtopicId(): any {
    return this.tce?.testForm?.value?.subtopicId ?? '';
  }
  set insSubtopicId(value: any) {
    const v = value ? Number(value) : '';
    this.tce?.testForm?.patchValue({ subtopicId: v }, { emitEvent: false });
    this.pushInsClassificationToForm();
  }

  private pushInsClassificationToForm() {
    const v = this.tce?.testForm?.value ?? {};
    this.questionForm.patchValue(
      {
        subjectId: v.subjectId || 0,
        topicId: v.topicId || 0,
        subtopicId: v.subtopicId || 0,
      },
      { emitEvent: false }
    );
  }

  onInspectorQuestionChange(evt: { index: number; region: QuestionRegion | null }) {
    const region = evt.region;
    if (!region) {
      return;
    }
    const subjectId = region.subjectId ?? 0;
    const topicId = region.topicId ?? 0;
    const subtopicId = region.subtopicId ?? 0;
    this.questionForm.patchValue({ subjectId, topicId, subtopicId }, { emitEvent: false });
    this.testCreateEnhancedComponent?.syncClassification?.(
      subjectId || null,
      topicId || null,
      subtopicId || null
    );
  }

  onSaveAndNew() {
    this.testCreateEnhancedComponent.onCreateAsync().subscribe({
      next: (test) => {
        console.log('Test created:', test);
        this.id = test.examId;
        this.testService.get(test.examId).subscribe({
          next: (testData) => {
            console.log('Test fetched:', testData);
            this.questionForm.get('testId')?.setValue(testData.subtitle, { emitEvent: false });
            // this.resetFormWithDefaultValues({
            //   subjectId: null,
            //   topicId: null,
            //   subtopicId: null,
            //   testId: testData.subtitle,
            //   bookId: testData.bookId,
            //   bookTestId: testData.bookTestId,
            //   testValue: testData.id,
            // });
            this.onSave();
          },
        });
      },
      error: (error) => {
        console.error('Error creating test:', error);
        this.snackBar.open(error?.message || 'Soru kaydedilirken hata oluştu!', 'Tamam', { duration: 3000 });
      },
    });
  }

  onSaveAndFix() {
    this.testCreateEnhancedComponent.onCreateAsync().subscribe({
      next: (test) => {
        console.log('Test created:', test);
        this.id = test.examId;
        this.testService.get(test.examId).subscribe({
          next: (testData) => {
            console.log('Test fetched:', testData);
            this.questionForm.get('testId')?.setValue(testData.subtitle, { emitEvent: false });
            this.onSave(true);
          },
        });
      },
      error: (error) => {
        console.error('Error creating test:', error);
        this.snackBar.open(error?.message || 'Soru kaydedilirken hata oluştu!', 'Tamam', { duration: 3000 });
      },
    });
  }

  sendToWorkPages() {
    console.log('Çalışma sayfasına gönderiliyor...');
    this.imageSelector.sendToStudyPage();
  }

  onSubmit() {
    this.testCreateEnhancedComponent.onCreateAsync().subscribe({
      next: (test) => {
        console.log('Test created:', test);
        this.id = test.examId;
        this.testService.get(test.examId).subscribe({
          next: (testData) => {
            console.log('Test fetched:', testData);
            this.questionForm.get('testId')?.setValue(testData.subtitle, { emitEvent: false });
            this.resetFormWithDefaultValues({
              subjectId: null,
              topicId: null,
              subtopicId: null,
              testId: testData.subtitle,
              bookId: testData.bookId,
              bookTestId: testData.bookTestId,
              testValue: testData.id,
            });
            this.onSave();
          },
        });
      },
      error: (error) => {
        console.error('Error creating test:', error);
        this.snackBar.open(error?.message || 'Soru kaydedilirken hata oluştu!', 'Tamam', { duration: 3000 });
      },
    });
  }

  onSave(sendToFix: boolean = true) {
    const formData = this.questionForm.value;

    if (formData.isExample) {
      if (!formData.practiceCorrectAnswer) {
        this.snackBar.open('Lütfen örnek soru için doğru cevabı seçin!', 'Tamam', { duration: 3000 });
        return;
      }
    }

    const questionPayload = {
      testId: formData.testValue ? formData.testValue : 0,
      topicId: formData.topicId,
      subjectId: formData.subjectId,
    };

    var payload = this.imageSelector.getRegions(questionPayload);
    this.questionService.saveBulk(payload).subscribe({
      next: (data) => {
        console.log('Soru Kaydedildi:', data);
        this.snackBar.open('sorular Başarıyla Kaydedildi', 'Tamam', { duration: 2000 });
        if (sendToFix) {
          this.imageSelector.sendToFix();
        }
        this.testCreateEnhancedComponent.reloadComponent(formData.testValue);
        setTimeout(() => {
          this.nextImage();
        }, 2000);
      },
      error: (err) => {
        console.log(err);
        for (const key in err?.error?.errors) {
          if (key.startsWith('$.')) {
            this.snackBar.open('Hatalı alan yolu:', key);
          }
        }
      },
    });
  }
}
