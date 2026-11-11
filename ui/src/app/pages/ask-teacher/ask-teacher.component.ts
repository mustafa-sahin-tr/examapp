import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute } from '@angular/router';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { finalize, forkJoin, take } from 'rxjs';

import {
  ASK_TEACHER_FEATURE,
  AskTeacherQuestion,
  AskTeacherQuota,
  AskTeacherTeacher,
  askTeacherErrorCode,
} from '../../models/ask-teacher.model';
import { Subject } from '../../models/subject';
import { AskTeacherService } from '../../services/ask-teacher.service';
import { LocaleService } from '../../services/locale.service';
import { SubjectService } from '../../services/subject.service';
import { AskTeacherImageComponent } from '../../shared/components/ask-teacher-image/ask-teacher-image.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { PlanLockedCardComponent } from '../../shared/components/plan-locked-card/plan-locked-card.component';

const SCOPE = 'ask-teacher';
export const ASK_TEACHER_MAX_NOTE = 1000;
export const ASK_TEACHER_MAX_PHOTO_BYTES = 15 * 1024 * 1024;

/**
 * Issue #1111: öğrencinin "Öğretmene sor" sayfası — ayın hakkı, fotoğraflı soru formu (ilişkili ya da ders aldığı öğretmene) ve
 * sorularım listesi (cevap, iptal, puan). Bildirimden `?q=<id>` ile gelinince o soru vurgulanır ve görünür alana kaydırılır.
 * Hak sınırı / planda yok yanıtları ortak yükseltme penceresiyle (interceptor) gelir; burada yalnız satır içi mesaj.
 */
@Component({
  selector: 'app-ask-teacher',
  standalone: true,
  imports: [
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    TranslocoDirective,
    AskTeacherImageComponent,
    PageHeaderComponent,
    PlanLockedCardComponent,
  ],
  providers: [provideTranslocoScope(SCOPE)],
  templateUrl: './ask-teacher.component.html',
  styleUrl: './ask-teacher.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AskTeacherComponent implements OnInit {
  private readonly service = inject(AskTeacherService);
  private readonly subjectsApi = inject(SubjectService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly locale = inject(LocaleService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  readonly feature = ASK_TEACHER_FEATURE;
  readonly maxNote = ASK_TEACHER_MAX_NOTE;
  readonly stars = [1, 2, 3, 4, 5];

  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly quota = signal<AskTeacherQuota | null>(null);
  readonly teachers = signal<AskTeacherTeacher[]>([]);
  readonly subjects = signal<Subject[]>([]);
  readonly questions = signal<AskTeacherQuestion[]>([]);
  readonly highlightId = signal<number | null>(null);

  readonly subjectId = signal<number | null>(null);
  readonly teacherId = signal<number | null>(null);
  readonly note = signal('');
  readonly photo = signal<File | null>(null);
  readonly photoPreview = signal<string | null>(null);
  readonly formError = signal<string | null>(null);
  readonly sending = signal(false);
  readonly busyId = signal<number | null>(null);

  readonly notInPlan = computed(() => this.quota()?.limit === 0);
  /** null = sınırsız. Bekleyen sorular hakkı rezerve eder. */
  readonly remaining = computed(() => {
    const q = this.quota();
    if (!q || q.limit === null) return null;
    return Math.max(0, q.limit - q.used - q.pending);
  });
  readonly canSend = computed(
    () => !this.sending() && this.teacherId() !== null && this.photo() !== null && this.remaining() !== 0,
  );

  constructor() {
    // Snackbar / hata metinleri senkron `translate()` ile okunur; scope şablondan bağımsız yüklensin.
    this.transloco.load(`${SCOPE}/${this.transloco.getActiveLang()}`).pipe(take(1)).subscribe();
  }

  ngOnInit(): void {
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const id = Number(params.get('q'));
      this.highlightId.set(Number.isInteger(id) && id > 0 ? id : null);
      this.scrollToHighlight();
    });
    this.destroyRef.onDestroy(() => this.revokePreview());
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(false);
    forkJoin({ quota: this.service.quota(), questions: this.service.list() })
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: ({ quota, questions }) => {
          this.quota.set(quota);
          this.questions.set(questions);
          if (quota.limit !== 0) this.loadForm();
          this.scrollToHighlight();
        },
        error: () => this.loadError.set(true),
      });
  }

  private loadForm(): void {
    if (this.subjects().length === 0) {
      this.subjectsApi
        .loadCategories()
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({ next: (s) => this.subjects.set(s), error: () => this.subjects.set([]) });
    }
    this.loadTeachers();
  }

  private loadTeachers(): void {
    this.service
      .teachers(this.subjectId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (teachers) => {
          this.teachers.set(teachers);
          const current = this.teacherId();
          if (current !== null && !teachers.some((t) => t.teacherId === current)) this.teacherId.set(null);
          if (this.teacherId() === null && teachers.length === 1) this.teacherId.set(teachers[0].teacherId);
        },
        error: () => this.teachers.set([]),
      });
  }

  setSubject(id: number | null): void {
    this.subjectId.set(id);
    this.loadTeachers();
  }

  onPhotoPicked(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    input.value = '';
    if (!file) return;
    this.formError.set(null);
    if (!file.type.startsWith('image/')) {
      this.formError.set(this.t('errors.invalidImage'));
      return;
    }
    if (file.size > ASK_TEACHER_MAX_PHOTO_BYTES) {
      this.formError.set(this.t('errors.imageTooLarge'));
      return;
    }
    this.revokePreview();
    this.photo.set(file);
    this.photoPreview.set(URL.createObjectURL(file));
  }

  clearPhoto(): void {
    this.revokePreview();
    this.photo.set(null);
  }

  send(): void {
    const photo = this.photo();
    const teacherId = this.teacherId();
    if (!this.canSend() || !photo || teacherId === null) return;
    this.sending.set(true);
    this.formError.set(null);
    this.service
      .send({ teacherId, subjectId: this.subjectId(), note: this.note(), photo })
      .pipe(
        finalize(() => this.sending.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (question) => {
          this.questions.update((list) => [question, ...list]);
          this.quota.update((q) => (q ? { ...q, pending: q.pending + 1 } : q));
          this.clearPhoto();
          this.note.set('');
          this.snackBar.open(this.t('sent'), undefined, { duration: 4000 });
        },
        error: (error: unknown) => this.formError.set(this.errorText(error)),
      });
  }

  cancel(question: AskTeacherQuestion): void {
    this.busyId.set(question.id);
    this.service
      .cancel(question.id)
      .pipe(
        finalize(() => this.busyId.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.replace({ ...question, status: 'Cancelled' });
          this.quota.update((q) => (q ? { ...q, pending: Math.max(0, q.pending - 1) } : q));
        },
        error: (error: unknown) => this.snack(this.errorText(error)),
      });
  }

  rate(question: AskTeacherQuestion, rating: number): void {
    if (question.status !== 'Answered' || question.rating === rating) return;
    this.busyId.set(question.id);
    this.service
      .rate(question.id, rating)
      .pipe(
        finalize(() => this.busyId.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => this.replace({ ...question, rating }),
        error: (error: unknown) => this.snack(this.errorText(error)),
      });
  }

  formatDate(iso: string): string {
    return new Intl.DateTimeFormat(this.locale.localeDefinition().angularLocale, {
      day: 'numeric',
      month: 'long',
      hour: '2-digit',
      minute: '2-digit',
    }).format(new Date(iso));
  }

  statusKey(question: AskTeacherQuestion): string {
    return `status.${question.status.charAt(0).toLowerCase()}${question.status.slice(1)}`;
  }

  private replace(question: AskTeacherQuestion): void {
    this.questions.update((list) => list.map((q) => (q.id === question.id ? question : q)));
  }

  private errorText(error: unknown): string {
    // Sınır (429) ve planda yok (403) ortak yanıtlardır; yükseltme penceresini interceptor açar.
    const status = (error as { status?: number } | null)?.status;
    const code = status === 429 ? 'quotaReached' : status === 403 ? 'notInPlan' : askTeacherErrorCode(error);
    const key = `errors.${code ?? 'generic'}`;
    const text = this.t(key);
    return text === `${SCOPE}.${key}` || text === key ? this.t('errors.generic') : text;
  }

  private snack(message: string): void {
    this.snackBar.open(message, this.transloco.translate<string>('common.close'), { duration: 6000 });
  }

  private t(key: string): string {
    return this.transloco.translate<string>(`${SCOPE}.${key}`);
  }

  private revokePreview(): void {
    const url = this.photoPreview();
    if (url) URL.revokeObjectURL(url);
    this.photoPreview.set(null);
  }

  private scrollToHighlight(): void {
    const id = this.highlightId();
    if (id === null || typeof document === 'undefined') return;
    setTimeout(() => document.getElementById(`ask-q-${id}`)?.scrollIntoView({ block: 'center', behavior: 'smooth' }));
  }
}
