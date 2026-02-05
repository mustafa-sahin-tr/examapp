import { Component, computed, effect, inject, OnInit, signal, ViewChild } from '@angular/core';
import { FormGroup, Validators, FormControl, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { MatRadioModule } from '@angular/material/radio';
import { MatSliderModule } from '@angular/material/slider';
import { MatSnackBarModule } from '@angular/material/snack-bar';
import { CommonModule, Location } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { SubjectService } from '../../services/subject.service';
import { QuestionService } from '../../services/question.service';
import { QuillModule } from 'ngx-quill';
import { ActivatedRoute, Router } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { Test } from '../../models/test-instance';
import { TestService } from '../../services/test.service';
import { QuestionCanvasForm } from '../../models/question-form';
import { Book, BookTest } from '../../models/book';
import { BookService } from '../../services/book.service';
import { ImageSelectorComponent } from '../image-selector/image-selector.component';
import { debounceTime, of, switchMap, tap } from 'rxjs';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { SidenavService } from '../../services/sidenav.service';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatMenuModule } from '@angular/material/menu';
import { toSignal } from '@angular/core/rxjs-interop';
import { TestFormComponent } from '../test-form/test-form.component';
import { TestCreateEnhancedComponent } from '../test-create-enhanced/test-create-enhanced.component';
@Component({
  selector: 'app-question-canvas',
  standalone: true,
  templateUrl: './question-canvas.component.html',
  styleUrls: ['./question-canvas.component.scss'],
  imports: [
    MatInputModule,
    MatFormFieldModule,
    MatButtonModule,
    MatSelectModule,
    MatRadioModule,
    MatSliderModule,
    MatSnackBarModule,
    FormsModule,
    ReactiveFormsModule,
    CommonModule,
    MatMenuModule,
    MatCardModule,
    MatIconModule,
    QuillModule,
    ImageSelectorComponent,
    MatAutocompleteModule,
    MatSlideToggleModule,
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
  location = inject(Location);
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

  togglePreviewMode() {
    let testId = this.id;
    if (!testId) {
      testId = Number(this.questionForm.value.testValue);
    }

    if (!Number.isFinite(Number(testId)) || Number(testId) <= 0) {
      this.snackBar.open('Ön izleme için önce bir test seçin.', 'Tamam', { duration: 2500 });
      return;
    }

    const returnUrl = this.router.url;

    const rawState = (this.location.getState?.() as any) ?? (globalThis as any)?.history?.state ?? {};
    const { navigationId, ...returnState } = rawState || {};

    this.router.navigate(['/questioncanvas/preview', testId], {
      queryParams: { returnUrl },
      state: {
        returnUrl,
        returnState,
      },
    });
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

  onChangeQuestionCount(event: any) {
    const answerCount = event.target.value;
    this.imageSelector.answerCount.set(answerCount);
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
    this.imageSelector.autoMode.set(checked);
    if (checked) {
      this.nextImage();
    }
  }

  setAutoAlign(checked: boolean) {
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

  onSave(navigateNewQuestion: boolean = false) {
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
        this.imageSelector.sendToFix();
        this.testCreateEnhancedComponent.reloadComponent(formData.testValue);
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
