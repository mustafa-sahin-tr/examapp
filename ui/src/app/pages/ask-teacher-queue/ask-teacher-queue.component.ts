import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute } from '@angular/router';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { finalize, take } from 'rxjs';

import { AskTeacherQuestion, AskTeacherStatus, askTeacherErrorCode } from '../../models/ask-teacher.model';
import { AskTeacherService } from '../../services/ask-teacher.service';
import { LocaleService } from '../../services/locale.service';
import { AskTeacherImageComponent } from '../../shared/components/ask-teacher-image/ask-teacher-image.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { ASK_TEACHER_MAX_PHOTO_BYTES } from '../ask-teacher/ask-teacher.component';

const SCOPE = 'ask-teacher-queue';
export const ASK_TEACHER_MAX_ANSWER = 4000;

export type AskQueueFilter = 'Open' | 'Answered' | 'all';

interface AnswerDraft {
  text: string;
  image: File | null;
  preview: string | null;
  error: string | null;
}

const EMPTY_DRAFT: AnswerDraft = { text: '', image: null, preview: null, error: null };

/**
 * Issue #1111: öğretmenin "Öğretmene sor" kuyruğu — bekleyen sorular süresi en yakından, cevap yazı ve / veya görselle; cevaplanan
 * soruda öğrencinin puanı görünür. Bildirimden `?q=<id>` ile gelinince o soru vurgulanır (bekleyenlerde yoksa "Tümü"ne geçilir).
 */
@Component({
  selector: 'app-ask-teacher-queue',
  standalone: true,
  imports: [
    FormsModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    TranslocoDirective,
    AskTeacherImageComponent,
    PageHeaderComponent,
  ],
  providers: [provideTranslocoScope(SCOPE)],
  templateUrl: './ask-teacher-queue.component.html',
  styleUrl: './ask-teacher-queue.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AskTeacherQueueComponent implements OnInit {
  private readonly service = inject(AskTeacherService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly locale = inject(LocaleService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  readonly maxAnswer = ASK_TEACHER_MAX_ANSWER;
  readonly stars = [1, 2, 3, 4, 5];

  readonly filter = signal<AskQueueFilter>('Open');
  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly questions = signal<AskTeacherQuestion[]>([]);
  readonly highlightId = signal<number | null>(null);
  readonly drafts = signal<Record<number, AnswerDraft>>({});
  readonly busyId = signal<number | null>(null);

  constructor() {
    // Snackbar / hata metinleri senkron `translate()` ile okunur; scope şablondan bağımsız yüklensin.
    this.transloco.load(`${SCOPE}/${this.transloco.getActiveLang()}`).pipe(take(1)).subscribe();
  }

  ngOnInit(): void {
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const id = Number(params.get('q'));
      this.highlightId.set(Number.isInteger(id) && id > 0 ? id : null);
      this.load();
    });
    this.destroyRef.onDestroy(() => Object.values(this.drafts()).forEach((d) => d.preview && URL.revokeObjectURL(d.preview)));
  }

  setFilter(filter: AskQueueFilter): void {
    this.filter.set(filter);
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(false);
    const filter = this.filter();
    const status: AskTeacherStatus | null = filter === 'all' ? null : filter;
    this.service
      .queue(status)
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (questions) => {
          const id = this.highlightId();
          // Bildirimdeki soru artık bekleyenlerde değilse (cevaplandı / süresi doldu) tümünde aranır.
          if (id !== null && filter !== 'all' && !questions.some((q) => q.id === id)) {
            this.filter.set('all');
            this.load();
            return;
          }
          this.questions.set(questions);
          this.scrollToHighlight();
        },
        error: () => this.loadError.set(true),
      });
  }

  draft(id: number): AnswerDraft {
    return this.drafts()[id] ?? EMPTY_DRAFT;
  }

  setText(id: number, text: string): void {
    this.patchDraft(id, { text, error: null });
  }

  onImagePicked(id: number, event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    input.value = '';
    if (!file) return;
    if (!file.type.startsWith('image/')) {
      this.patchDraft(id, { error: this.t('errors.invalidImage') });
      return;
    }
    if (file.size > ASK_TEACHER_MAX_PHOTO_BYTES) {
      this.patchDraft(id, { error: this.t('errors.imageTooLarge') });
      return;
    }
    this.clearImage(id);
    this.patchDraft(id, { image: file, preview: URL.createObjectURL(file), error: null });
  }

  clearImage(id: number): void {
    const preview = this.draft(id).preview;
    if (preview) URL.revokeObjectURL(preview);
    this.patchDraft(id, { image: null, preview: null });
  }

  canAnswer(id: number): boolean {
    const d = this.draft(id);
    return this.busyId() !== id && (d.text.trim().length > 0 || d.image !== null);
  }

  answer(question: AskTeacherQuestion): void {
    const d = this.draft(question.id);
    if (!this.canAnswer(question.id)) return;
    this.busyId.set(question.id);
    this.service
      .answer(question.id, d.text, d.image)
      .pipe(
        finalize(() => this.busyId.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (answered) => {
          this.clearImage(question.id);
          this.drafts.update(({ [question.id]: _, ...rest }) => rest);
          this.questions.update((list) =>
            this.filter() === 'Open' ? list.filter((q) => q.id !== question.id) : list.map((q) => (q.id === answered.id ? answered : q)),
          );
          this.snackBar.open(this.t('answered'), undefined, { duration: 4000 });
        },
        error: (error: unknown) => {
          const code = askTeacherErrorCode(error);
          const key = `errors.${code ?? 'generic'}`;
          const text = this.t(key);
          this.patchDraft(question.id, { error: text.endsWith(key) ? this.t('errors.generic') : text });
        },
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

  /** Kalan süre (saat, aşağı yuvarlanır); 0 → "1 saatten az". */
  hoursLeft(question: AskTeacherQuestion): number {
    return Math.max(0, Math.floor((new Date(question.dueAt).getTime() - Date.now()) / 3_600_000));
  }

  statusKey(question: AskTeacherQuestion): string {
    return `status.${question.status.charAt(0).toLowerCase()}${question.status.slice(1)}`;
  }

  private patchDraft(id: number, patch: Partial<AnswerDraft>): void {
    this.drafts.update((all) => ({ ...all, [id]: { ...(all[id] ?? EMPTY_DRAFT), ...patch } }));
  }

  private t(key: string): string {
    return this.transloco.translate<string>(`${SCOPE}.${key}`);
  }

  private scrollToHighlight(): void {
    const id = this.highlightId();
    if (id === null || typeof document === 'undefined') return;
    setTimeout(() => document.getElementById(`ask-queue-${id}`)?.scrollIntoView({ block: 'center', behavior: 'smooth' }));
  }
}
