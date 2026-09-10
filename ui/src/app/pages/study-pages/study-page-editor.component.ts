import { CommonModule } from '@angular/common';
import { Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { ActivatedRoute, Router } from '@angular/router';
import { GradesService } from '../../services/grades.service';
import { SubjectService } from '../../services/subject.service';
import { StudyPageService } from '../../services/study-page.service';
import { BookService } from '../../services/book.service';
import { Subject } from '../../models/subject';
import { Topic } from '../../models/topic';
import { SubTopic } from '../../models/subtopic';
import { Grade } from '../../models/student';
import { Book, BookTest } from '../../models/book';
import {
  STUDY_PAGE_CONTENT_TYPE_ICONS,
  STUDY_PAGE_CONTENT_TYPE_LABELS,
  STUDY_PAGE_PLATFORM_ICONS,
  STUDY_PAGE_PLATFORM_LABELS,
  StudyPage,
  StudyPageContentType,
  StudyPageImage,
  StudyPageLinkPlatform,
  StudyPageWriteRequest,
} from '../../models/study-page';
import { SectionHeaderComponent } from '../../shared/components/section-header/section-header.component';
import { AutofocusDirective } from '../../shared/directives/auto-focus.directive';

interface NewImageItem {
  file?: File; // Optional for MinIO items
  previewUrl: string;
  selected: boolean;
  isFromJson?: boolean;
  minioUrl?: string; // MinIO URL for the image
  bookName?: string; // Book name for MinIO items
  pageNumber?: number; // Page number for MinIO items
  isFromMinio?: boolean; // Indicates if this is from MinIO
}

interface BookConfig {
  book: string;
  pages: number[];
}

interface ExpectedFile {
  bookName: string;
  pageNumber: number;
  found: boolean;
}

type PreviewItemType = 'existing' | 'new';

interface PreviewItem {
  key: string;
  url: string;
  type: PreviewItemType;
  fileName?: string | null;
  selected: boolean;
  existingImageId?: number;
  newImageIndex?: number;
}

interface ContentTypeOption {
  value: StudyPageContentType;
  label: string;
  icon: string;
}

interface PlatformOption {
  value: StudyPageLinkPlatform;
  label: string;
  icon: string;
}

@Component({
  selector: 'app-study-page-editor',
  standalone: true,
  templateUrl: './study-page-editor.component.html',
  styleUrls: ['./study-page-editor.component.scss'],
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatIconModule,
    MatSlideToggleModule,
    MatSnackBarModule,
    SectionHeaderComponent,
    AutofocusDirective,
  ],
})
export class StudyPageEditorComponent implements OnDestroy {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private gradesService = inject(GradesService);
  private subjectService = inject(SubjectService);
  private studyPageService = inject(StudyPageService);
  private bookService = inject(BookService);
  private snackBar = inject(MatSnackBar);

  readonly ContentType = StudyPageContentType;

  readonly contentTypeOptions: ContentTypeOption[] = [
    StudyPageContentType.Image,
    StudyPageContentType.Link,
    StudyPageContentType.BookPageRange,
  ].map((value) => ({
    value,
    label: STUDY_PAGE_CONTENT_TYPE_LABELS[value],
    icon: STUDY_PAGE_CONTENT_TYPE_ICONS[value],
  }));

  readonly platformOptions: PlatformOption[] = [
    StudyPageLinkPlatform.Eba,
    StudyPageLinkPlatform.YouTube,
    StudyPageLinkPlatform.Other,
  ].map((value) => ({
    value,
    label: STUDY_PAGE_PLATFORM_LABELS[value],
    icon: STUDY_PAGE_PLATFORM_ICONS[value],
  }));

  form = new FormGroup({
    title: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    description: new FormControl('', { nonNullable: true }),
    gradeId: new FormControl<number | null>(null),
    subjectId: new FormControl<number | null>(null),
    topicId: new FormControl<number | null>(null),
    subTopicId: new FormControl<number | null>(null),
    isPublished: new FormControl(true, { nonNullable: true }),

    contentType: new FormControl<StudyPageContentType>(StudyPageContentType.Image, { nonNullable: true }),

    // Link
    url: new FormControl('', { nonNullable: true }),
    platform: new FormControl<StudyPageLinkPlatform>(StudyPageLinkPlatform.Other, { nonNullable: true }),

    // BookPageRange
    bookId: new FormControl<number | null>(null),
    bookTestId: new FormControl<number | null>(null),
    newBookName: new FormControl('', { nonNullable: true }),
    newBookTestName: new FormControl('', { nonNullable: true }),
    startPage: new FormControl<number | null>(null),
    endPage: new FormControl<number | null>(null),
  });

  grades = signal<Grade[]>([]);
  subjects = signal<Subject[]>([]);
  topics = signal<Topic[]>([]);
  subtopics = signal<SubTopic[]>([]);

  isEditMode = signal(false);
  loading = signal(false);

  // ContentType — form control'ün signal yansıması
  contentType = toSignal(this.form.controls.contentType.valueChanges, {
    initialValue: this.form.controls.contentType.value,
  });
  isImageType = computed(() => this.contentType() === StudyPageContentType.Image);
  isLinkType = computed(() => this.contentType() === StudyPageContentType.Link);
  isBookPageRangeType = computed(() => this.contentType() === StudyPageContentType.BookPageRange);

  // Link platform — seçili platform ikonu
  selectedPlatform = toSignal(this.form.controls.platform.valueChanges, {
    initialValue: this.form.controls.platform.value,
  });
  selectedPlatformIcon = computed(() => STUDY_PAGE_PLATFORM_ICONS[this.selectedPlatform()]);
  selectedPlatformLabel = computed(() => STUDY_PAGE_PLATFORM_LABELS[this.selectedPlatform()]);

  // BookPageRange — test-create ile aynı UX: dropdown + inline "yeni ekle"
  books = signal<Book[]>([]);
  bookTests = signal<BookTest[]>([]);
  booksLoading = signal(false);
  booksError = signal<string | null>(null);
  bookTestsLoading = signal(false);
  showAddBookInput = signal(false);
  showAddBookTestInput = signal(false);
  private booksLoaded = false;

  existingImages = signal<StudyPageImage[]>([]);
  removedImageIds = new Set<number>();
  newImages = signal<NewImageItem[]>([]);
  previewIndex = signal(0);
  downloadingZip = signal(false);

  // JSON-guided bulk selection properties
  jsonConfig = signal<BookConfig[]>([]);
  expectedFiles = signal<ExpectedFile[]>([]);
  jsonProcessing = signal(false);
  jsonFileName = signal<string | null>(null);

  constructor() {
    this.loadGrades();

    const idParam = this.route.snapshot.paramMap.get('id');
    if (idParam && idParam !== 'new') {
      this.isEditMode.set(true);
      this.loadStudyPage(Number(idParam));
    }

    this.form.get('gradeId')?.valueChanges.subscribe((gradeId) => {
      if (gradeId) {
        this.subjectService.getSubjectsByGrade(gradeId).subscribe((subjects) => {
          this.subjects.set(subjects);
        });
      } else {
        this.subjects.set([]);
      }
      this.form.get('subjectId')?.setValue(null);
      this.form.get('topicId')?.setValue(null);
      this.form.get('subTopicId')?.setValue(null);
      this.topics.set([]);
      this.subtopics.set([]);
    });

    this.form.get('subjectId')?.valueChanges.subscribe((subjectId) => {
      const gradeId = this.form.get('gradeId')?.value;
      if (subjectId && gradeId) {
        this.subjectService.getTopicsBySubjectAndGrade(subjectId, gradeId).subscribe((topics) => {
          this.topics.set(topics);
        });
      } else {
        this.topics.set([]);
      }
      this.form.get('topicId')?.setValue(null);
      this.form.get('subTopicId')?.setValue(null);
      this.subtopics.set([]);
    });

    this.form.get('topicId')?.valueChanges.subscribe((topicId) => {
      if (topicId) {
        this.subjectService.getSubTopicsByTopic(topicId).subscribe((subtopics) => {
          this.subtopics.set(subtopics);
        });
      } else {
        this.subtopics.set([]);
      }
      this.form.get('subTopicId')?.setValue(null);
    });

    // Kitap listesi sadece BookPageRange tipi seçildiğinde (tembel) yüklenir
    this.form.controls.contentType.valueChanges.subscribe((type) => {
      if (type === StudyPageContentType.BookPageRange) {
        this.ensureBooksLoaded();
      }
    });
  }

  ngOnDestroy(): void {
    this.newImages().forEach((image) => URL.revokeObjectURL(image.previewUrl));
  }

  // ---- BookPageRange (test-create deseninden uyarlandı) ----

  private pendingAfterBooksLoad: Array<() => void> = [];

  private ensureBooksLoaded(afterLoad?: () => void) {
    if (this.booksLoaded) {
      afterLoad?.();
      return;
    }
    if (afterLoad) this.pendingAfterBooksLoad.push(afterLoad);
    if (this.booksLoading()) return; // istek zaten yolda; callback kuyruğa alındı

    this.booksLoading.set(true);
    this.booksError.set(null);
    this.bookService.getAll().subscribe({
      next: (books) => {
        this.books.set(books);
        this.booksLoaded = true;
        this.booksLoading.set(false);
        const callbacks = this.pendingAfterBooksLoad;
        this.pendingAfterBooksLoad = [];
        callbacks.forEach((cb) => cb());
      },
      error: () => {
        this.booksLoading.set(false);
        this.pendingAfterBooksLoad = [];
        this.booksError.set('Kitap listesi yuklenemedi.');
      },
    });
  }

  retryLoadBooks() {
    this.booksLoaded = false;
    this.ensureBooksLoaded();
  }

  onBookChange(bookId: number | null, resetTest = true) {
    if (!bookId) {
      this.bookTests.set([]);
      return;
    }
    this.showAddBookInput.set(false);
    this.bookTestsLoading.set(true);
    this.bookService.getTestsByBook(bookId).subscribe({
      next: (tests) => {
        this.bookTests.set(tests);
        this.bookTestsLoading.set(false);
        if (resetTest) {
          this.form.patchValue({ bookTestId: null, newBookTestName: '' });
        }
      },
      error: () => {
        this.bookTests.set([]);
        this.bookTestsLoading.set(false);
        this.snackBar.open('Kitap testleri yuklenemedi.', 'Tamam', { duration: 3000 });
      },
    });
  }

  openNewBookAdd() {
    this.showAddBookInput.set(true);
    this.form.patchValue({ bookId: null });
    this.bookTests.set([]);
    // Yeni kitabın testi de yeni olmak zorunda
    this.openNewBookTestAdd();
  }

  onNewBookBlur() {
    const name = this.form.controls.newBookName.value;
    if (!name?.trim()) {
      this.showAddBookInput.set(false);
      this.showAddBookTestInput.set(false);
    }
  }

  openNewBookTestAdd() {
    this.showAddBookTestInput.set(true);
    this.form.patchValue({ bookTestId: null });
  }

  onNewBookTestBlur() {
    const name = this.form.controls.newBookTestName.value;
    // Yeni kitap girilirken test de yeni olmak zorunda; dropdown'a geri dönme
    if (!name?.trim() && !this.showAddBookInput()) {
      this.showAddBookTestInput.set(false);
    }
  }

  // ---- Image (mevcut davranış) ----

  get previewItems(): PreviewItem[] {
    const existing = this.existingImages().map((img) => ({
      key: `existing-${img.id}`,
      url: img.imageUrl,
      type: 'existing' as const,
      fileName: img.fileName ?? null,
      selected: !this.removedImageIds.has(img.id),
      existingImageId: img.id,
    }));

    const newItems = this.newImages().map((img, index) => ({
      key: `new-${index}`,
      url: img.previewUrl,
      type: 'new' as const,
      fileName: img.file?.name || (img.isFromMinio ? `${img.bookName}/page_${img.pageNumber}.webp` : 'Unknown'),
      selected: img.selected,
      newImageIndex: index,
    }));

    return [...existing, ...newItems];
  }

  get selectedItems(): PreviewItem[] {
    return this.previewItems.filter((item) => item.selected);
  }

  get currentPreview() {
    const items = this.previewItems;
    if (items.length === 0) return null;

    const safeIndex = Math.min(this.previewIndex(), items.length - 1);
    if (safeIndex !== this.previewIndex()) {
      this.previewIndex.set(safeIndex);
    }
    return items[safeIndex];
  }

  previousImage() {
    const items = this.previewItems;
    if (items.length === 0) return;
    const nextIndex = this.previewIndex() === 0 ? items.length - 1 : this.previewIndex() - 1;
    this.previewIndex.set(nextIndex);
  }

  nextImage() {
    const items = this.previewItems;
    if (items.length === 0) return;
    const nextIndex = this.previewIndex() === items.length - 1 ? 0 : this.previewIndex() + 1;
    this.previewIndex.set(nextIndex);
  }

  setPreview(index: number) {
    this.previewIndex.set(index);
  }

  setPreviewByKey(key: string) {
    const index = this.previewItems.findIndex((item) => item.key === key);
    if (index > -1) {
      this.previewIndex.set(index);
    }
  }

  onFilesSelected(event: Event) {
    const input = event.target as HTMLInputElement;

    if (!input.files || input.files.length === 0) {
      return;
    }

    const files = Array.from(input.files);
    const newItems = files.map((file) => {
      const expectedFile = this.findMatchingExpectedFile(file.name);
      return {
        file,
        previewUrl: URL.createObjectURL(file),
        selected: expectedFile ? true : false, // Auto-select if matches JSON expectation
        isFromJson: expectedFile ? true : false,
        minioUrl: expectedFile ? `/img/study-pages/books/${expectedFile.bookName}/page_${expectedFile.pageNumber}.webp` : undefined,
      };
    });

    // Update expected files with found matches
    this.updateExpectedFilesWithMatches(files);

    const existingCount = this.previewItems.length;
    this.newImages.set([...this.newImages(), ...newItems]);
    this.previewIndex.set(existingCount);
    input.value = '';
  }

  async onJsonSelected(event: Event) {
    const input = event.target as HTMLInputElement;

    if (!input.files || input.files.length === 0) return;

    const file = input.files[0];

    if (!file.name.toLowerCase().endsWith('.json')) {
      this.snackBar.open('Lütfen JSON dosyası seçin.', 'Tamam', { duration: 3000 });
      input.value = '';
      return;
    }

    this.jsonProcessing.set(true);
    this.jsonFileName.set(file.name);

    try {
      const text = await file.text();
      const config: unknown = JSON.parse(text);

      // Validate JSON structure
      if (!Array.isArray(config) || !this.validateJsonConfig(config)) {
        throw new Error('Invalid JSON format');
      }

      this.jsonConfig.set(config);
      this.generateExpectedFiles(config);

      // Auto-create MinIO images for expected files
      await this.createMinIOImages();

      this.snackBar.open(
        `JSON yüklendi: MinIO'dan ${this.newImages().filter((img) => img.isFromMinio).length} resim bulundu.`,
        'Tamam',
        { duration: 3000 }
      );
    } catch {
      this.snackBar.open(
        'JSON dosyası okunamadı. Format: [{"book":"kitap_adi","pages":[87,88,89]}]',
        'Tamam',
        { duration: 5000 }
      );
      this.jsonFileName.set(null);
    } finally {
      this.jsonProcessing.set(false);
      input.value = '';
    }
  }

  private validateJsonConfig(config: unknown[]): config is BookConfig[] {
    return config.every((item) => {
      if (typeof item !== 'object' || item === null) return false;
      const candidate = item as Partial<BookConfig>;
      return (
        typeof candidate.book === 'string' &&
        Array.isArray(candidate.pages) &&
        candidate.pages.every((page) => typeof page === 'number')
      );
    });
  }

  private generateExpectedFiles(config: BookConfig[]) {
    const expected: ExpectedFile[] = [];
    config.forEach((bookConfig) => {
      bookConfig.pages.forEach((page) => {
        expected.push({
          bookName: bookConfig.book,
          pageNumber: page,
          found: false,
        });
      });
    });
    this.expectedFiles.set(expected);
  }

  async createMinIOImages() {
    const expectedFiles = this.expectedFiles();
    const newItems: NewImageItem[] = [];

    for (const expectedFile of expectedFiles) {
      const minioUrl = `/img/study-pages/books/${expectedFile.bookName}/page_${expectedFile.pageNumber}.webp`;

      const exists = await this.checkMinIOImageExists(minioUrl);

      if (exists) {
        const encodedUrl = this.encodeMinIOUrl(minioUrl);
        newItems.push({
          previewUrl: encodedUrl, // Use encoded URL for preview
          selected: true,
          isFromJson: true,
          minioUrl: minioUrl, // Keep original for reference
          bookName: expectedFile.bookName,
          pageNumber: expectedFile.pageNumber,
          isFromMinio: true,
        });

        expectedFile.found = true;
      }
    }

    const existingCount = this.previewItems.length;
    this.newImages.set([...this.newImages(), ...newItems]);
    this.previewIndex.set(existingCount);
  }

  async checkMinIOImageExists(url: string): Promise<boolean> {
    try {
      const encodedUrl = this.encodeMinIOUrl(url);

      // Use GET request directly since HEAD doesn't work with this MinIO setup
      const response = await fetch(encodedUrl, { method: 'GET' });
      return response.ok;
    } catch {
      return false;
    }
  }

  private encodeMinIOUrl(url: string): string {
    // Find the study-pages path segment
    const pathMarker = '/img/study-pages/books/';
    const pathIndex = url.indexOf(pathMarker);
    if (pathIndex === -1) {
      return url; // Return original if path not found
    }

    const baseUrl = url.substring(0, pathIndex + pathMarker.length);
    const pathPart = url.substring(pathIndex + pathMarker.length);

    // Split into book folder and filename
    const parts = pathPart.split('/');
    if (parts.length !== 2) {
      return url; // Fallback to original if format unexpected
    }

    const [bookFolder, filename] = parts;

    const encodedBookFolder = encodeURIComponent(bookFolder);
    const encodedFilename = encodeURIComponent(filename);

    return `${baseUrl}${encodedBookFolder}%2F${encodedFilename}`;
  }

  private findMatchingExpectedFile(fileName: string): ExpectedFile | undefined {
    return this.expectedFiles().find((expected) => {
      // Check if filename matches the expected pattern - support both just filename and full path
      const expectedFileName = `page_${expected.pageNumber}.webp`;
      const expectedPath = `${expected.bookName}/page_${expected.pageNumber}.webp`;

      // Match either just the filename or the full path
      return (
        fileName === expectedFileName ||
        fileName === expectedPath ||
        fileName.endsWith(expectedPath) ||
        fileName.endsWith(expectedFileName)
      );
    });
  }

  private updateExpectedFilesWithMatches(files: File[]) {
    const expected = [...this.expectedFiles()];

    files.forEach((file) => {
      expected.forEach((expectedFile) => {
        const expectedFileName = `page_${expectedFile.pageNumber}.webp`;
        const expectedPath = `${expectedFile.bookName}/page_${expectedFile.pageNumber}.webp`;

        // Use same matching logic as findMatchingExpectedFile
        const isMatch =
          file.name === expectedFileName ||
          file.name === expectedPath ||
          file.name.endsWith(expectedPath) ||
          file.name.endsWith(expectedFileName);

        if (isMatch && !expectedFile.found) {
          expectedFile.found = true;
        }
      });
    });

    this.expectedFiles.set(expected);
  }

  onBulkFilesSelected(event: Event) {
    const input = event.target as HTMLInputElement;

    if (!input.files || input.files.length === 0) {
      return;
    }

    const files = Array.from(input.files);

    // Add all selected files as new images
    const newItems = files.map((file) => ({
      file,
      previewUrl: URL.createObjectURL(file),
      selected: true,
      isFromJson: false,
    }));

    const existingCount = this.previewItems.length;
    this.newImages.set([...this.newImages(), ...newItems]);
    this.previewIndex.set(existingCount);

    this.snackBar.open(`${files.length} adet dosya eklendi.`, 'Tamam', { duration: 3000 });
    input.value = '';
  }

  clearJsonConfig() {
    this.jsonConfig.set([]);
    this.expectedFiles.set([]);
    this.jsonFileName.set(null);

    // Remove JSON-guided selections (including MinIO images)
    const filtered = this.newImages().filter((img) => !img.isFromJson);
    this.newImages.set(filtered);
  }

  toggleNewImageSelection(index: number) {
    const items = [...this.newImages()];
    if (!items[index]) return;
    items[index] = { ...items[index], selected: !items[index].selected };
    this.newImages.set(items);
  }

  get guidanceStats() {
    const expected = this.expectedFiles();
    const foundCount = expected.filter((f) => f.found).length;
    const totalCount = expected.length;
    return { found: foundCount, total: totalCount, missing: totalCount - foundCount };
  }

  get hasJsonConfig() {
    return this.jsonConfig().length > 0;
  }

  get expectedFilesByBook() {
    const expected = this.expectedFiles();
    const byBook: { [book: string]: ExpectedFile[] } = {};

    expected.forEach((file) => {
      if (!byBook[file.bookName]) {
        byBook[file.bookName] = [];
      }
      byBook[file.bookName].push(file);
    });

    return byBook;
  }

  removeNewImage(index: number) {
    const items = [...this.newImages()];
    const removed = items.splice(index, 1)[0];
    if (removed) {
      URL.revokeObjectURL(removed.previewUrl);
    }
    this.newImages.set(items);
  }

  toggleRemoveExisting(image: StudyPageImage) {
    if (this.removedImageIds.has(image.id)) {
      this.removedImageIds.delete(image.id);
    } else {
      this.removedImageIds.add(image.id);
    }
  }

  toggleCurrentSelection() {
    const item = this.currentPreview;
    if (!item) return;

    const shouldAdvance = !item.selected;

    if (item.type === 'new' && item.newImageIndex !== undefined) {
      this.toggleNewImageSelection(item.newImageIndex);
      if (shouldAdvance) {
        this.nextImage();
      }
      return;
    }

    if (item.type === 'existing' && item.existingImageId !== undefined) {
      const existing = this.existingImages().find((img) => img.id === item.existingImageId);
      if (!existing) return;
      this.toggleRemoveExisting(existing);
      if (shouldAdvance) {
        this.nextImage();
      }
    }
  }

  removeSelectedByKey(key: string) {
    const item = this.previewItems.find((x) => x.key === key);
    if (!item) return;

    if (item.type === 'new' && item.newImageIndex !== undefined) {
      if (item.selected) {
        this.toggleNewImageSelection(item.newImageIndex);
      }
      return;
    }

    if (item.type === 'existing' && item.existingImageId !== undefined) {
      const existing = this.existingImages().find((img) => img.id === item.existingImageId);
      if (!existing) return;
      if (!this.removedImageIds.has(existing.id)) {
        this.toggleRemoveExisting(existing);
      }
    }
  }

  async downloadAsZip() {
    const images = this.existingImages();
    if (images.length === 0) return;

    this.downloadingZip.set(true);
    try {
      const JSZip = (await import('jszip')).default;
      const zip = new JSZip();

      await Promise.all(
        images.map(async (img, index) => {
          const response = await fetch(img.imageUrl);
          const blob = await response.blob();
          const ext = img.fileName?.split('.').pop() || 'jpg';
          const name = img.fileName || `resim_${index + 1}.${ext}`;
          zip.file(name, blob);
        })
      );

      const content = await zip.generateAsync({ type: 'blob' });
      const title = (this.form.value.title || 'calisma-sayfasi').replace(/[^a-z0-9_\-]/gi, '_').toLowerCase();
      const url = URL.createObjectURL(content);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${title}.zip`;
      a.click();
      URL.revokeObjectURL(url);
    } catch {
      this.snackBar.open('ZIP indirme sirasinda hata olustu.', 'Tamam', { duration: 3000 });
    } finally {
      this.downloadingZip.set(false);
    }
  }

  // ---- Kaydet ----

  onCancel() {
    this.router.navigate(['/study-pages']);
  }

  /** Tipe özel istemci doğrulaması; backend ValidateContent ile aynı kurallar. Hata mesajı döner, null ise geçerli. */
  private validateTypedContent(): string | null {
    const v = this.form.getRawValue();

    switch (v.contentType) {
      case StudyPageContentType.Link: {
        const url = v.url.trim();
        if (!url) return 'Link tipi icin URL zorunludur.';
        try {
          const parsed = new URL(url);
          if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
            return 'Gecerli bir http/https URL giriniz.';
          }
        } catch {
          return 'Gecerli bir http/https URL giriniz.';
        }
        return null;
      }

      case StudyPageContentType.BookPageRange: {
        const hasBook = !!v.bookId || !!v.newBookName.trim();
        const hasTest = !!v.bookTestId || !!v.newBookTestName.trim();
        if (!hasBook) return 'Kitap secin veya yeni kitap adi girin.';
        if (!hasTest) return 'Kitap testi secin veya yeni test adi girin.';
        if (v.startPage === null || v.endPage === null) return 'Baslangic ve bitis sayfasi zorunludur.';
        if (v.startPage <= 0 || v.endPage <= 0) return 'Sayfa numaralari pozitif olmalidir.';
        if (v.endPage < v.startPage) return 'Bitis sayfasi baslangictan kucuk olamaz.';
        return null;
      }

      case StudyPageContentType.Image:
      default: {
        const selectedNewItems = this.newImages().filter((img) => img.selected);
        const remainingExisting = this.existingImages().filter((img) => !this.removedImageIds.has(img.id));
        if (!this.isEditMode() && selectedNewItems.length === 0) return 'En az bir resim secmelisiniz.';
        if (this.isEditMode() && selectedNewItems.length === 0 && remainingExisting.length === 0) {
          return 'Bu sayfada en az bir resim kalmali.';
        }
        return null;
      }
    }
  }

  onSave() {
    if (this.form.invalid) {
      this.snackBar.open('Lutfen gerekli alanlari doldurun.', 'Tamam', { duration: 2000 });
      return;
    }

    const typedError = this.validateTypedContent();
    if (typedError) {
      this.snackBar.open(typedError, 'Tamam', { duration: 3000 });
      return;
    }

    const v = this.form.getRawValue();
    const isImage = v.contentType === StudyPageContentType.Image;

    // Yalnızca Image tipinde dosya/MinIO listesi gönderilir
    const selectedNewFiles = isImage
      ? this.newImages()
          .filter((img) => img.selected && img.file)
          .map((img) => img.file!)
      : [];

    const selectedMinIOImages = isImage
      ? this.newImages()
          .filter((img) => img.selected && img.isFromMinio && img.minioUrl)
          .map((img) => ({ bookName: img.bookName!, pageNumber: img.pageNumber!, minioUrl: img.minioUrl! }))
      : [];

    const payload: StudyPageWriteRequest = {
      title: v.title,
      description: v.description,
      gradeId: v.gradeId,
      subjectId: v.subjectId,
      topicId: v.topicId,
      subTopicId: v.subTopicId,
      isPublished: v.isPublished,
      contentType: v.contentType,
      url: v.url,
      platform: v.platform,
      bookId: this.showAddBookInput() ? null : v.bookId,
      bookTestId: this.showAddBookTestInput() ? null : v.bookTestId,
      newBookName: this.showAddBookInput() ? v.newBookName : null,
      newBookTestName: this.showAddBookTestInput() ? v.newBookTestName : null,
      startPage: v.startPage,
      endPage: v.endPage,
      minioImages: selectedMinIOImages,
    };

    this.loading.set(true);

    if (this.isEditMode()) {
      const id = Number(this.route.snapshot.paramMap.get('id'));
      this.studyPageService
        .update(
          id,
          {
            ...payload,
            removedImageIds: isImage ? Array.from(this.removedImageIds) : [],
          },
          selectedNewFiles
        )
        .subscribe({
          next: () => {
            this.snackBar.open('Calisma etkinligi guncellendi.', 'Tamam', { duration: 2000 });
            this.router.navigate(['/study-pages']);
          },
          error: (err: { error?: { message?: string } }) => {
            this.snackBar.open(err?.error?.message || 'Guncelleme sirasinda hata olustu.', 'Tamam', { duration: 3000 });
            this.loading.set(false);
          },
        });
      return;
    }

    this.studyPageService.create(payload, selectedNewFiles).subscribe({
      next: () => {
        this.snackBar.open('Calisma etkinligi kaydedildi.', 'Tamam', { duration: 2000 });
        this.router.navigate(['/study-pages']);
      },
      error: (err: { error?: { message?: string } }) => {
        this.snackBar.open(err?.error?.message || 'Kayit sirasinda hata olustu.', 'Tamam', { duration: 3000 });
        this.loading.set(false);
      },
    });
  }

  private loadGrades() {
    this.gradesService.getGrades().subscribe((grades) => {
      this.grades.set(grades);
    });
  }

  private loadStudyPage(id: number) {
    this.loading.set(true);
    this.studyPageService.getById(id).subscribe({
      next: (page: StudyPage) => {
        const gradeId = page.gradeId ?? null;
        const subjectId = page.subjectId ?? null;
        const topicId = page.topicId ?? null;
        const subTopicId = page.subTopicId ?? null;
        // Eski kayıtlarda contentType gelmezse Image varsay
        const contentType = page.contentType ?? StudyPageContentType.Image;

        this.form.patchValue(
          {
            title: page.title,
            description: page.description,
            gradeId,
            subjectId,
            topicId,
            subTopicId,
            isPublished: page.isPublished,
            contentType,
            url: page.url ?? '',
            platform: page.platform ?? StudyPageLinkPlatform.Other,
            bookId: page.bookId ?? null,
            bookTestId: page.bookTestId ?? null,
            startPage: page.startPage ?? null,
            endPage: page.endPage ?? null,
          },
          { emitEvent: false }
        );
        // patchValue emitEvent:false ile çalıştığı için toSignal'lar tetiklenmez; elle senkronla
        this.form.controls.contentType.setValue(contentType);
        this.form.controls.platform.setValue(page.platform ?? StudyPageLinkPlatform.Other);

        this.existingImages.set(page.images || []);

        if (contentType === StudyPageContentType.BookPageRange) {
          this.ensureBooksLoaded(() => {
            if (page.bookId) {
              this.onBookChange(page.bookId, false);
            }
          });
        }

        if (gradeId) {
          this.subjectService.getSubjectsByGrade(gradeId).subscribe((subjects) => {
            this.subjects.set(subjects);
          });
        } else {
          this.subjects.set([]);
        }

        if (subjectId && gradeId) {
          this.subjectService.getTopicsBySubjectAndGrade(subjectId, gradeId).subscribe((topics) => {
            this.topics.set(topics);
          });
        } else {
          this.topics.set([]);
        }

        if (topicId) {
          this.subjectService.getSubTopicsByTopic(topicId).subscribe((subtopics) => {
            this.subtopics.set(subtopics);
          });
        } else {
          this.subtopics.set([]);
        }

        this.loading.set(false);
      },
      error: () => {
        this.snackBar.open('Calisma etkinligi bulunamadi.', 'Tamam', { duration: 3000 });
        this.router.navigate(['/study-pages']);
      },
    });
  }
}
