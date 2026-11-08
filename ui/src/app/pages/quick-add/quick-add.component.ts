import { DatePipe } from '@angular/common';
import { HttpErrorResponse, HttpEventType } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
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

import { BookImportJob, BookImportQuota } from '../../models/book-import';
import {
  QUICK_ADD_MAX_PHOTOS,
  QUICK_ADD_MAX_PHOTO_BYTES,
  isQuickAddImage,
  isQuickAddPending,
  quickAddPhase,
} from '../../models/quick-add';
import { Grade } from '../../models/student';
import { Subject } from '../../models/subject';
import { GradesService } from '../../services/grades.service';
import { QuickAddService } from '../../services/quick-add.service';
import { SubjectService } from '../../services/subject.service';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';

/** Sayfa metinleri kendi Transloco scope'unda: `public/i18n/quick-add/<lang>.json`. */
export const QUICK_ADD_SCOPE = 'quick-add';
/** Süren iş varken liste yenileme aralığı. */
export const QUICK_ADD_POLL_MS = 5000;
/** Son seçilen sınıf: telefonda her seferinde yeniden seçilmesin. */
export const QUICK_ADD_GRADE_KEY = 'quickAdd.gradeId';

export interface QuickAddDraftPhoto {
  id: number;
  file: File;
  /** Küçük resim için `URL.createObjectURL`; kaldırılınca / sayfadan çıkınca bırakılır. */
  url: string;
  answers: string;
}

export interface QuickAddPickError {
  key: 'tooMany' | 'tooLarge' | 'wrongType';
  name: string;
}

/**
 * Issue #1053 (epic #1048): mobil öncelikli "Hızlı soru ekle" — fotoğraf çek / seç, fotoğraf başına isteğe bağlı cevap anahtarı,
 * yükle; altta "Hızlı eklemelerim". Kamera `<input capture>` ile (Capacitor kamerası #1045). Sınır aşımında yükseltme penceresi
 * `entitlementLimitInterceptor`'dan gelir (#1029).
 */
@Component({
  selector: 'app-quick-add',
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
  providers: [provideTranslocoScope(QUICK_ADD_SCOPE)],
  templateUrl: './quick-add.component.html',
  styleUrls: ['./quick-add.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuickAddComponent implements OnInit {
  private readonly api = inject(QuickAddService);
  private readonly gradesApi = inject(GradesService);
  private readonly subjectsApi = inject(SubjectService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly maxPhotos = QUICK_ADD_MAX_PHOTOS;

  readonly jobs = signal<BookImportJob[]>([]);
  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly quota = signal<BookImportQuota | null>(null);
  readonly grades = signal<Grade[]>([]);
  readonly subjects = signal<Subject[]>([]);

  readonly photos = signal<QuickAddDraftPhoto[]>([]);
  readonly pickErrors = signal<QuickAddPickError[]>([]);
  readonly submitted = signal(false);
  readonly uploading = signal(false);
  readonly uploadPercent = signal(0);
  /** Sunucu hata kodu (`quickAdd.invalidImage` …) ve hatalı fotoğrafın sırası. */
  readonly submitError = signal<{ code: string; photo: number | null } | null>(null);

  readonly gradeId = new FormControl<number | null>(null, Validators.required);
  readonly subjectId = new FormControl<number | null>(null);

  /** Kalan hak; sınırsızsa null. */
  readonly remaining = computed(() => {
    const quota = this.quota();
    return quota?.limit == null ? null : Math.max(0, quota.limit - quota.used);
  });
  readonly quotaBlocked = computed(() => this.quota()?.allowed === false || this.remaining() === 0);
  /** Seçilen fotoğraflar kalan hakkı aşıyor (sunucu yine de 429 + yükseltme penceresi döner). */
  readonly overQuota = computed(() => {
    const remaining = this.remaining();
    return remaining != null && this.photos().length > remaining;
  });
  readonly canAddMore = computed(() => this.photos().length < QUICK_ADD_MAX_PHOTOS && !this.uploading());

  readonly phase = quickAddPhase;

  private nextId = 1;
  private poll?: Subscription;

  ngOnInit(): void {
    this.destroyRef.onDestroy(() => this.photos().forEach((p) => URL.revokeObjectURL(p.url)));
    this.load();
    this.loadQuota();
    this.gradesApi
      .getGrades()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (grades) => this.grades.set(grades ?? []), error: () => this.grades.set([]) });
    this.gradeId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((gradeId) => this.onGradeChange(gradeId));
    const saved = Number(localStorage.getItem(QUICK_ADD_GRADE_KEY));
    if (saved > 0) this.gradeId.setValue(saved);
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

  private loadQuota(): void {
    this.api
      .quota()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (quota) => this.quota.set(quota), error: () => this.quota.set(null) });
  }

  /** Süren iş varken liste yenilenir; hepsi durunca yoklama biter. */
  private schedulePoll(): void {
    this.poll?.unsubscribe();
    if (!this.jobs().some(isQuickAddPending)) return;
    this.poll = timer(QUICK_ADD_POLL_MS)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.load());
  }

  private onGradeChange(gradeId: number | null): void {
    this.subjectId.setValue(null);
    this.subjects.set([]);
    if (gradeId == null) return;
    localStorage.setItem(QUICK_ADD_GRADE_KEY, String(gradeId));
    this.subjectsApi.getSubjectsByGrade(gradeId).subscribe({
      next: (subjects) => this.subjects.set(subjects ?? []),
      error: () => this.subjects.set([]),
    });
  }

  onFileInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.addFiles(Array.from(input.files ?? []));
    input.value = '';
  }

  /** Türü / boyutu uygun olanlar eklenir, sınırın ötesi düşer; her ret için bir mesaj. */
  addFiles(files: File[]): void {
    const errors: QuickAddPickError[] = [];
    const added: QuickAddDraftPhoto[] = [];
    let room = QUICK_ADD_MAX_PHOTOS - this.photos().length;
    for (const file of files) {
      if (!isQuickAddImage(file)) {
        errors.push({ key: 'wrongType', name: file.name });
      } else if (file.size <= 0 || file.size > QUICK_ADD_MAX_PHOTO_BYTES) {
        errors.push({ key: 'tooLarge', name: file.name });
      } else if (room <= 0) {
        if (!errors.some((e) => e.key === 'tooMany')) errors.push({ key: 'tooMany', name: file.name });
      } else {
        added.push({ id: this.nextId++, file, url: URL.createObjectURL(file), answers: '' });
        room--;
      }
    }
    this.pickErrors.set(errors);
    this.submitError.set(null);
    if (added.length) this.photos.update((photos) => [...photos, ...added]);
  }

  removePhoto(id: number): void {
    const photo = this.photos().find((p) => p.id === id);
    if (photo) URL.revokeObjectURL(photo.url);
    this.photos.update((photos) => photos.filter((p) => p.id !== id));
    this.submitError.set(null);
  }

  setAnswers(id: number, answers: string): void {
    this.photos.update((photos) => photos.map((p) => (p.id === id ? { ...p, answers } : p)));
    if (this.submitError()?.code === 'quickAdd.invalidAnswerKey') this.submitError.set(null);
  }

  submit(): void {
    this.submitted.set(true);
    const photos = this.photos();
    if (this.gradeId.invalid || photos.length === 0 || this.uploading()) {
      this.gradeId.markAsTouched();
      return;
    }
    this.uploading.set(true);
    this.uploadPercent.set(0);
    this.submitError.set(null);
    this.api
      .start({
        gradeId: this.gradeId.value!,
        subjectId: this.subjectId.value,
        photos: photos.map((p) => ({ file: p.file, answers: p.answers })),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (event) => {
          if (event.type === HttpEventType.UploadProgress && event.total) {
            this.uploadPercent.set(Math.round((100 * event.loaded) / event.total));
          } else if (event.type === HttpEventType.Response && event.body) {
            this.uploading.set(false);
            void this.router.navigate(['/quick-add', event.body.id]);
          }
        },
        error: (error: HttpErrorResponse) => {
          this.uploading.set(false);
          const code = typeof error.error?.code === 'string' ? (error.error.code as string) : 'generic';
          const photo = typeof error.error?.photo === 'number' ? (error.error.photo as number) : null;
          this.submitError.set({ code, photo });
          if (error.status === 429) this.loadQuota();
        },
      });
  }

  /** Sunucu hata kodunu scope'taki anahtara çevirir (`quickAdd.invalidImage` → `errors.invalidImage`). */
  errorKey(code: string): string {
    const known = [
      'invalidGrade', 'invalidSubject', 'noPhotos', 'tooManyPhotos', 'photoTooLarge', 'invalidImage', 'invalidAnswerKey',
    ];
    if (code === 'entitlement.limitReached') return 'errors.quotaReached';
    const name = code.replace(/^quickAdd\./, '');
    return known.includes(name) ? `errors.${name}` : 'errors.generic';
  }

  progress(job: BookImportJob): number {
    return job.stageTotal > 0 ? Math.round((100 * job.stageProcessed) / job.stageTotal) : 0;
  }
}
