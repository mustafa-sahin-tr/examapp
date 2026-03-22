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
  file: File;
  previewUrl: string;
  selected: boolean;
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
      fileName: img.file.name,
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
    if (!input.files || input.files.length === 0) return;

    const files = Array.from(input.files);
    const newItems = files.map((file) => ({
      file,
      previewUrl: URL.createObjectURL(file),
      selected: false,
    }));

    const existingCount = this.previewItems.length;
    this.newImages.set([...this.newImages(), ...newItems]);
    this.previewIndex.set(existingCount);
    input.value = '';
  }

  toggleNewImageSelection(index: number) {
    const items = [...this.newImages()];
    if (!items[index]) return;
    items[index] = { ...items[index], selected: !items[index].selected };
    this.newImages.set(items);
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
      .filter((img) => img.selected)
      .map((img) => img.file);

    const remainingExisting = this.existingImages().filter((img) => !this.removedImageIds.has(img.id));

    if (!this.isEditMode() && selectedNewFiles.length === 0) {
      this.snackBar.open('En az bir resim secmelisiniz.', 'Tamam', { duration: 2000 });
      return;
    }

    if (this.isEditMode() && selectedNewFiles.length === 0 && remainingExisting.length === 0) {
      this.snackBar.open('Bu sayfada en az bir resim kalmali.', 'Tamam', { duration: 2000 });
      return;
    }

    const payload = {
      title: this.form.value.title || '',
      description: this.form.value.description || '',
      gradeId: this.form.value.gradeId,
      subjectId: this.form.value.subjectId,
      topicId: this.form.value.topicId,
      subTopicId: this.form.value.subTopicId,
      isPublished: this.form.value.isPublished ?? true,
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
