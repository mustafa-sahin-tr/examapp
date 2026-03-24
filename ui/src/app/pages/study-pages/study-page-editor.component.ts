import { CommonModule } from '@angular/common';
import { Component, OnDestroy, inject, signal } from '@angular/core';
import { FormControl, FormGroup, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
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
import { Subject } from '../../models/subject';
import { Topic } from '../../models/topic';
import { SubTopic } from '../../models/subtopic';
import { Grade } from '../../models/student';
import { StudyPage, StudyPageImage } from '../../models/study-page';
import { SectionHeaderComponent } from '../../shared/components/section-header/section-header.component';

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
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatIconModule,
    MatSlideToggleModule,
    MatSnackBarModule,
    SectionHeaderComponent,
  ],
})
export class StudyPageEditorComponent implements OnDestroy {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private gradesService = inject(GradesService);
  private subjectService = inject(SubjectService);
  private studyPageService = inject(StudyPageService);
  private snackBar = inject(MatSnackBar);

  form = new FormGroup({
    title: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    description: new FormControl('', { nonNullable: true }),
    gradeId: new FormControl<number | null>(null),
    subjectId: new FormControl<number | null>(null),
    topicId: new FormControl<number | null>(null),
    subTopicId: new FormControl<number | null>(null),
    isPublished: new FormControl(true, { nonNullable: true }),
  });

  grades = signal<Grade[]>([]);
  subjects = signal<Subject[]>([]);
  topics = signal<Topic[]>([]);
  subtopics = signal<SubTopic[]>([]);

  isEditMode = signal(false);
  loading = signal(false);

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
  }

  ngOnDestroy(): void {
    this.newImages().forEach((image) => URL.revokeObjectURL(image.previewUrl));
  }

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
    console.log('🚀 onFilesSelected tetiklendi!', event);
    const input = event.target as HTMLInputElement;
    console.log('📁 Input element:', input);
    console.log('📝 Files:', input.files);
    
    if (!input.files || input.files.length === 0) {
      console.log('❌ Dosya bulunamadı veya boş');
      return;
    }

    const files = Array.from(input.files);
    console.log('📋 Seçilen dosyalar:', files.map(f => f.name));
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
    console.log('📄 onJsonSelected tetiklendi!', event);
    const input = event.target as HTMLInputElement;
    console.log('📁 JSON Input:', input.files);
    
    if (!input.files || input.files.length === 0) return;

    const file = input.files[0];
    console.log('📋 JSON dosyası:', file.name, file.size, 'bytes');
    
    if (!file.name.toLowerCase().endsWith('.json')) {
      console.log('❌ JSON olmayan dosya:', file.name);
      this.snackBar.open('Lütfen JSON dosyası seçin.', 'Tamam', { duration: 3000 });
      input.value = '';
      return;
    }

    this.jsonProcessing.set(true);
    this.jsonFileName.set(file.name);

    try {
      const text = await file.text();
      console.log('📖 JSON içeriği:', text);
      const config: BookConfig[] = JSON.parse(text);
      console.log('🔍 Parsed config:', config);
      
      // Validate JSON structure
      if (!Array.isArray(config) || !this.validateJsonConfig(config)) {
        throw new Error('Invalid JSON format');
      }

      this.jsonConfig.set(config);
      console.log('✅ JSON config set edildi:', this.jsonConfig());
      this.generateExpectedFiles(config);
      console.log('📝 Expected files oluşturuldu:', this.expectedFiles());
      
      // Auto-create MinIO images for expected files
      await this.createMinIOImages();
      
      this.snackBar.open(
        `JSON yüklendi: MinIO'dan ${this.newImages().filter(img => img.isFromMinio).length} resim bulundu.`, 
        'Tamam', 
        { duration: 3000 }
      );
    } catch (error) {
      console.log('❌ JSON parse hatası:', error);
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

  private validateJsonConfig(config: any[]): config is BookConfig[] {
    return config.every(
      (item) =>
        typeof item === 'object' &&
        typeof item.book === 'string' &&
        Array.isArray(item.pages) &&
        item.pages.every((page: any) => typeof page === 'number')
    );
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
    console.log('☁️ MinIO imajlar kontrol ediliyor...');
    
    const expectedFiles = this.expectedFiles();
    const newItems: NewImageItem[] = [];
    
    for (const expectedFile of expectedFiles) {
      const minioUrl = `/img/study-pages/books/${expectedFile.bookName}/page_${expectedFile.pageNumber}.webp`;
      console.log(`🔍 MinIO URL kontrol ediliyor: ${minioUrl}`);
      
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
    
    console.log(`✅ ${newItems.length}/${expectedFiles.length} MinIO resmi bulundu ve eklendi`);
  }

  async checkMinIOImageExists(url: string): Promise<boolean> {
    try {
      const encodedUrl = this.encodeMinIOUrl(url);
      
      // Use GET request directly since HEAD doesn't work with this MinIO setup
      const response = await fetch(encodedUrl, { method: 'GET' });
      return response.ok;
    } catch (error) {
      console.log(`❌ MinIO URL kontrol hatası: ${url}`, error);
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
    
    // Transform book folder name: only remove spaces after "number." patterns
    // "4. Sınıf" -> "4.Sınıf", "1. Defter" -> "1.Defter"
    // But keep normal spaces like "Maksimum Tam Benlik"
    // const transformedBookFolder = bookFolder
    //   .replace(/(\d+)\.\s+/g, '$1.'); // Only remove space after number+dot
    
    // Now encode the transformed folder name
    const encodedBookFolder = encodeURIComponent(bookFolder);
    const encodedFilename = encodeURIComponent(filename);
    
    return `${baseUrl}${encodedBookFolder}%2F${encodedFilename}`;
  }

  private findMatchingExpectedFile(fileName: string): ExpectedFile | undefined {
    console.log('🔍 Aranan dosya:', fileName);
    
    return this.expectedFiles().find((expected) => {
      // Check if filename matches the expected pattern - support both just filename and full path
      const expectedFileName = `page_${expected.pageNumber}.webp`;
      const expectedPath = `${expected.bookName}/page_${expected.pageNumber}.webp`;
      
      console.log(`📋 Beklenen: "${expectedPath}" ve "${expectedFileName}"`);
      
      // Match either just the filename or the full path
      const isMatch = fileName === expectedFileName || 
                     fileName === expectedPath ||
                     fileName.endsWith(expectedPath) ||
                     fileName.endsWith(expectedFileName);
                     
      console.log(`✅ Eşleşme: ${isMatch ? 'EVET' : 'HAYIR'}`);
      
      return isMatch;
    });
  }

  private updateExpectedFilesWithMatches(files: File[]) {
    const expected = [...this.expectedFiles()];
    const newImages = this.newImages();

    console.log('🔄 Dosya eşleştirmesi yapılıyor:', files.map(f => f.name));

    files.forEach((file, fileIndex) => {
      const newImageIndex = newImages.length + fileIndex;
      expected.forEach((expectedFile) => {
        const expectedFileName = `page_${expectedFile.pageNumber}.webp`;
        const expectedPath = `${expectedFile.bookName}/page_${expectedFile.pageNumber}.webp`;
        
        console.log(`🎯 "${file.name}" ile "${expectedPath}" ve "${expectedFileName}" karşılaştırılıyor`);
        
        // Use same matching logic as findMatchingExpectedFile
        const isMatch = file.name === expectedFileName || 
                       file.name === expectedPath ||
                       file.name.endsWith(expectedPath) ||
                       file.name.endsWith(expectedFileName);
                       
        console.log(`📌 Sonuç: ${isMatch ? 'EŞLEŞTI!' : 'eşleşmedi'}`);
                       
        if (isMatch && !expectedFile.found) {
          console.log(`✅ ${expectedFile.bookName}/page_${expectedFile.pageNumber}.webp bulundu olarak işaretlendi`);
          expectedFile.found = true;
        }
      });
    });

    this.expectedFiles.set(expected);
  }

  onBulkFilesSelected(event: Event) {
    console.log('📂 onBulkFilesSelected tetiklendi!', event);
    const input = event.target as HTMLInputElement;
    
    if (!input.files || input.files.length === 0) {
      console.log('❌ Dosya bulunamadı veya boş');
      return;
    }

    const files = Array.from(input.files);
    console.log('📋 Bulk seçilen dosyalar:', files.map(f => f.name));
    
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
    
    this.snackBar.open(
      `${files.length} adet dosya eklendi.`, 
      'Tamam', 
      { duration: 3000 }
    );
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
    const foundCount = expected.filter(f => f.found).length;
    const totalCount = expected.length;
    return { found: foundCount, total: totalCount, missing: totalCount - foundCount };
  }

  get hasJsonConfig() {
    return this.jsonConfig().length > 0;
  }

  get expectedFilesByBook() {
    const expected = this.expectedFiles();
    const byBook: { [book: string]: ExpectedFile[] } = {};
    
    expected.forEach(file => {
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

  private ensurePreviewIndexInRange() {
    const maxIndex = Math.max(0, this.previewItems.length - 1);
    if (this.previewIndex() > maxIndex) {
      this.previewIndex.set(maxIndex);
    }
  }

  onCancel() {
    this.router.navigate(['/study-pages']);
  }

  onSave() {
    if (this.form.invalid) {
      this.snackBar.open('Lutfen gerekli alanlari doldurun.', 'Tamam', { duration: 2000 });
      return;
    }

    const selectedNewFiles = this.newImages()
      .filter((img) => img.selected && img.file) // Only files that were actually uploaded
      .map((img) => img.file!);

    const selectedNewItems = this.newImages().filter((img) => img.selected); // All selected new items (including MinIO)
    const remainingExisting = this.existingImages().filter((img) => !this.removedImageIds.has(img.id));

    if (!this.isEditMode() && selectedNewItems.length === 0) {
      this.snackBar.open('En az bir resim secmelisiniz.', 'Tamam', { duration: 2000 });
      return;
    }

    if (this.isEditMode() && selectedNewItems.length === 0 && remainingExisting.length === 0) {
      this.snackBar.open('Bu sayfada en az bir resim kalmali.', 'Tamam', { duration: 2000 });
      return;
    }

    const selectedMinIOImages = this.newImages()
      .filter((img) => img.selected && img.isFromMinio && img.minioUrl)
      .map((img) => ({ bookName: img.bookName!, pageNumber: img.pageNumber!, minioUrl: img.minioUrl! }));

    const payload = {
      title: this.form.value.title || '',
      description: this.form.value.description || '',
      gradeId: this.form.value.gradeId,
      subjectId: this.form.value.subjectId,
      topicId: this.form.value.topicId,
      subTopicId: this.form.value.subTopicId,
      isPublished: this.form.value.isPublished ?? true,
      minioImages: selectedMinIOImages, // Add MinIO images to payload
    };

    this.loading.set(true);

    if (this.isEditMode()) {
      const id = Number(this.route.snapshot.paramMap.get('id'));
      this.studyPageService
        .update(
          id,
          {
            ...payload,
            removedImageIds: Array.from(this.removedImageIds),
          },
          selectedNewFiles
        )
        .subscribe({
          next: () => {
            this.snackBar.open('Calisma sayfasi guncellendi.', 'Tamam', { duration: 2000 });
            this.router.navigate(['/study-pages']);
          },
          error: () => {
            this.snackBar.open('Guncelleme sirasinda hata olustu.', 'Tamam', { duration: 3000 });
            this.loading.set(false);
          },
        });
      return;
    }

    this.studyPageService.create(payload, selectedNewFiles).subscribe({
      next: () => {
        this.snackBar.open('Calisma sayfasi kaydedildi.', 'Tamam', { duration: 2000 });
        this.router.navigate(['/study-pages']);
      },
      error: () => {
        this.snackBar.open('Kayit sirasinda hata olustu.', 'Tamam', { duration: 3000 });
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

        this.form.patchValue(
          {
            title: page.title,
            description: page.description,
            gradeId,
            subjectId,
            topicId,
            subTopicId,
            isPublished: page.isPublished,
          },
          { emitEvent: false }
        );
        this.existingImages.set(page.images || []);

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
        this.snackBar.open('Calisma sayfasi bulunamadi.', 'Tamam', { duration: 3000 });
        this.router.navigate(['/study-pages']);
      },
    });
  }
}
