import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectChange, MatSelectModule } from '@angular/material/select';
import { Observable, forkJoin, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { SectionHeaderComponent } from '../../shared/components/section-header/section-header.component';
import { QuestionCanvasViewComponentv5 } from '../../shared/components/question-canvas-view-v5/question-canvas-view-v5.component';
import { QuestionLiteViewComponent } from '../question-lite-view/question-lite-view.component';
import { PracticeService } from '../../services/practice.service';
import { StudentService } from '../../services/student.service';
import { SubjectService } from '../../services/subject.service';
import { AnswerChoice, QuestionRegion } from '../../models/draws';
import { PracticeAnswerResult, PracticeSession } from '../../models/practice';
import { Question } from '../../models/question';
import { Subject } from '../../models/subject';
import { Topic } from '../../models/topic';

/**
 * Sayfa fazları:
 * - setup:    kapsam seçimi (ders / konu) — hiçbiri seçilmezse varsayılan kapsam (kendi sınıfı + tüm dersler)
 * - question: bekleyen soru gösteriliyor; "Pas Geç" / "Cevabı Gönder"
 * - feedback: doğru/yanlış/pas geri bildirimi; "Sonraki Soru"
 * - empty:    kapsamda soru yok ya da havuz tükendi; "Kapsamı Değiştir"
 * - ended:    oturum özeti
 */
type PracticePhase = 'setup' | 'question' | 'feedback' | 'empty' | 'ended';

interface SubjectTopics {
  subject: Subject;
  topics: Topic[];
}

const SESSION_QUERY_PARAM = 'session';

/**
 * "Soru Çöz" pratik akışı (issue #63). Soru gösterimi sınav çözme ekranıyla aynı bileşenleri
 * kullanır: canvas sorular `app-question-canvas-view-v5`, metin soruları `app-question-lite-view`.
 * Backend havuzu yalnız tek doğru cevaplı (CorrectAnswerId != null) sorulardan oluştuğu için
 * sürükle-bırak etkileşimi burada ele alınmaz.
 */
@Component({
  selector: 'app-practice-solve',
  standalone: true,
  imports: [
    NgTemplateOutlet,
    RouterLink,
    MatButtonModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    SectionHeaderComponent,
    QuestionCanvasViewComponentv5,
    QuestionLiteViewComponent,
  ],
  templateUrl: './practice-solve.component.html',
  styleUrls: ['./practice-solve.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PracticeSolveComponent implements OnInit {
  private readonly practiceService = inject(PracticeService);
  private readonly studentService = inject(StudentService);
  private readonly subjectService = inject(SubjectService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  // ── Faz / genel durum ───────────────────────────────────────────────────────
  readonly phase = signal<PracticePhase>('setup');
  /** Oturum çağrısı (başlat / sonraki / cevapla / bitir) sürüyor. */
  readonly busy = signal(false);
  /** Oturum çağrısı hatası; "Tekrar dene" `retryAction` ile son işlemi yineler. */
  readonly error = signal<string | null>(null);
  private retryAction: (() => void) | null = null;

  // ── Kurulum ekranı ──────────────────────────────────────────────────────────
  readonly setupLoading = signal(true);
  readonly setupError = signal<string | null>(null);
  readonly gradeId = signal<number | null>(null);
  readonly subjects = signal<Subject[]>([]);
  readonly selectedSubjectIds = signal<number[]>([]);
  readonly selectedTopicIds = signal<number[]>([]);
  readonly topicsLoading = signal(false);
  private readonly topicsBySubject = signal<Map<number, Topic[]>>(new Map());

  readonly selectedSubjects = computed(() => {
    const ids = new Set(this.selectedSubjectIds());
    return this.subjects().filter((s) => ids.has(s.id));
  });

  /** Seçili derslerin konuları, ders başlığıyla gruplanmış (mat-optgroup). */
  readonly topicGroups = computed<SubjectTopics[]>(() => {
    const byId = this.topicsBySubject();
    return this.selectedSubjects()
      .map((subject) => ({ subject, topics: byId.get(subject.id) ?? [] }))
      .filter((g) => g.topics.length > 0);
  });

  readonly selectedTopics = computed(() => {
    const ids = new Set(this.selectedTopicIds());
    return this.topicGroups().flatMap((g) => g.topics.filter((t) => ids.has(t.id)));
  });

  readonly hasScope = computed(() => this.selectedSubjectIds().length > 0 || this.selectedTopicIds().length > 0);

  // ── Oturum / soru ───────────────────────────────────────────────────────────
  readonly session = signal<PracticeSession | null>(null);
  readonly question = signal<Question | null>(null);
  readonly region = signal<QuestionRegion | null>(null);
  readonly selectedAnswerId = signal<number | null>(null);
  readonly selectedChoice = signal<AnswerChoice | undefined>(undefined);
  readonly result = signal<PracticeAnswerResult | null>(null);
  readonly answeredCount = signal(0);
  readonly correctCount = signal(0);
  readonly skippedCount = signal(0);
  readonly showPassageOnly = signal(false);
  private questionShownAt = 0;

  readonly isCanvasQuestion = computed(() => !!this.question()?.isCanvasQuestion);
  readonly isPassageFirstActive = computed(() => {
    const q = this.question();
    return !!q?.showPassageFirst && !!q?.passage;
  });
  readonly canSubmit = computed(() => this.phase() === 'question' && this.selectedAnswerId() != null && !this.busy());
  readonly wrongCount = computed(() => Math.max(0, this.answeredCount() - this.correctCount() - this.skippedCount()));
  readonly accuracy = computed(() => {
    const graded = this.answeredCount() - this.skippedCount();
    return graded > 0 ? Math.round((this.correctCount() / graded) * 100) : 0;
  });
  /** Canvas görünümü: cevap sonrası v5 doğru/yanlış boyamasını `result` modunda yapar. */
  readonly canvasMode = computed<'exam' | 'result'>(() => (this.phase() === 'feedback' ? 'result' : 'exam'));

  readonly feedbackKind = computed<'correct' | 'wrong' | 'skipped' | null>(() => {
    const r = this.result();
    if (!r || this.phase() !== 'feedback') return null;
    if (r.skipped) return 'skipped';
    return r.isCorrect ? 'correct' : 'wrong';
  });

  ngOnInit(): void {
    this.loadSetupData();

    const sessionParam = Number(this.route.snapshot.queryParamMap.get(SESSION_QUERY_PARAM));
    if (Number.isInteger(sessionParam) && sessionParam > 0) {
      this.resumeSession(sessionParam);
    }
  }

  // ── Kurulum ─────────────────────────────────────────────────────────────────

  loadSetupData(): void {
    this.setupLoading.set(true);
    this.setupError.set(null);

    this.studentService
      .getProfile()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (profile) => {
          const gradeId = profile.gradeId > 0 ? profile.gradeId : null;
          this.gradeId.set(gradeId);
          if (!gradeId) {
            this.subjects.set([]);
            this.setupLoading.set(false);
            return;
          }
          this.subjectService
            .getSubjectsByGrade(gradeId)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
              next: (subjects) => {
                this.subjects.set(subjects);
                this.setupLoading.set(false);
              },
              error: (err: unknown) => {
                this.setupError.set(this.messageOf(err, 'Dersler yüklenemedi.'));
                this.setupLoading.set(false);
              },
            });
        },
        error: (err: unknown) => {
          this.setupError.set(this.messageOf(err, 'Profil bilgisi yüklenemedi.'));
          this.setupLoading.set(false);
        },
      });
  }

  onSubjectsChange(event: MatSelectChange): void {
    this.setSelectedSubjects((event.value as number[]) ?? []);
  }

  removeSubject(subjectId: number): void {
    this.setSelectedSubjects(this.selectedSubjectIds().filter((id) => id !== subjectId));
  }

  onTopicsChange(event: MatSelectChange): void {
    this.selectedTopicIds.set((event.value as number[]) ?? []);
  }

  removeTopic(topicId: number): void {
    this.selectedTopicIds.set(this.selectedTopicIds().filter((id) => id !== topicId));
  }

  private setSelectedSubjects(ids: number[]): void {
    this.selectedSubjectIds.set(ids);

    // Kaldırılan derslerin konularını seçimden düşür.
    const allowedTopicIds = new Set(
      ids.flatMap((subjectId) => (this.topicsBySubject().get(subjectId) ?? []).map((t) => t.id))
    );
    this.selectedTopicIds.update((current) => current.filter((id) => allowedTopicIds.has(id)));

    const gradeId = this.gradeId();
    const missing = ids.filter((subjectId) => !this.topicsBySubject().has(subjectId));
    if (!gradeId || missing.length === 0) return;

    this.topicsLoading.set(true);
    const requests = missing.map((subjectId) =>
      this.subjectService.getTopicsBySubjectAndGrade(subjectId, gradeId).pipe(
        map((topics) => ({ subjectId, topics })),
        catchError(() => of({ subjectId, topics: [] as Topic[] }))
      )
    );
    forkJoin(requests)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((results) => {
        this.topicsBySubject.update((current) => {
          const next = new Map(current);
          for (const r of results) next.set(r.subjectId, r.topics);
          return next;
        });
        this.topicsLoading.set(false);
      });
  }

  // ── Oturum akışı ────────────────────────────────────────────────────────────

  startSession(): void {
    const request = {
      subjectIds: this.selectedSubjectIds(),
      topicIds: this.selectedTopicIds(),
    };
    this.run(this.practiceService.startSession(request), () => this.startSession(), (session) => {
      this.session.set(session);
      this.answeredCount.set(session.answeredCount);
      this.correctCount.set(session.correctCount);
      this.skippedCount.set(session.skippedCount);
      void this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { [SESSION_QUERY_PARAM]: session.id },
        replaceUrl: true,
      });
      this.loadNext();
    });
  }

  /** Sayfa yenilemede `?session=` ile aktif oturuma geri döner; bitmişse kurulumda kalır. */
  private resumeSession(sessionId: number): void {
    this.run(this.practiceService.getSession(sessionId), () => this.resumeSession(sessionId), (session) => {
      if (session.status !== 'Active') {
        this.clearSessionParam();
        return;
      }
      this.session.set(session);
      this.answeredCount.set(session.answeredCount);
      this.correctCount.set(session.correctCount);
      this.skippedCount.set(session.skippedCount);
      this.loadNext();
    });
  }

  loadNext(): void {
    const session = this.session();
    if (!session) return;

    this.run(this.practiceService.getNextQuestion(session.id), () => this.loadNext(), (next) => {
      this.answeredCount.set(next.answeredCount);
      this.correctCount.set(next.correctCount);
      this.result.set(null);
      this.selectedAnswerId.set(null);
      this.selectedChoice.set(undefined);

      if (next.poolExhausted || !next.question) {
        this.question.set(null);
        this.region.set(null);
        this.phase.set('empty');
        return;
      }

      const question = next.question;
      this.question.set(question);
      this.region.set(question.isCanvasQuestion ? this.practiceService.toQuestionRegion(question) : null);
      this.showPassageOnly.set(!!question.showPassageFirst && !!question.passage);
      this.questionShownAt = Date.now();
      this.phase.set('question');
    });
  }

  /** Canvas görünümünden (v5) şık seçimi. */
  selectChoice(choice: AnswerChoice): void {
    if (this.phase() !== 'question') return;
    this.selectedChoice.set(choice);
    this.selectedAnswerId.set(choice.id);
  }

  /** Metin görünümünden (lite view) şık seçimi. */
  selectAnswer(answerId: number): void {
    if (this.phase() !== 'question') return;
    this.selectedAnswerId.set(answerId);
  }

  submitAnswer(): void {
    const question = this.question();
    const session = this.session();
    const selectedAnswerId = this.selectedAnswerId();
    if (!question || !session || selectedAnswerId == null || this.phase() !== 'question') return;

    const request = {
      questionId: question.id,
      selectedAnswerId,
      skipped: false,
      timeTaken: this.elapsedSeconds(),
    };
    this.run(this.practiceService.submitAnswer(session.id, request), () => this.submitAnswer(), (r) =>
      this.applyResult(r)
    );
  }

  skipQuestion(): void {
    const question = this.question();
    const session = this.session();
    if (!question || !session || this.phase() !== 'question') return;

    const request = {
      questionId: question.id,
      selectedAnswerId: null,
      skipped: true,
      timeTaken: this.elapsedSeconds(),
    };
    this.run(this.practiceService.submitAnswer(session.id, request), () => this.skipQuestion(), (r) =>
      this.applyResult(r)
    );
  }

  private applyResult(r: PracticeAnswerResult): void {
    const question = this.question();
    if (!question) return;

    this.result.set(r);
    this.answeredCount.set(r.answeredCount);
    this.correctCount.set(r.correctCount);
    if (r.skipped) this.skippedCount.update((n) => n + 1);

    // Doğru şık artık biliniyor: canvas bölgesini yeniden kur (isCorrect bayrakları) ve
    // seçili şıkkı YENİ bölgenin nesnesiyle eşle — v5 seçimi referans eşitliğiyle karşılaştırır.
    if (question.isCanvasQuestion) {
      const region = this.practiceService.toQuestionRegion(question, r.correctAnswerId);
      this.region.set(region);
      const selectedId = this.selectedAnswerId();
      this.selectedChoice.set(selectedId != null ? region.answers.find((a) => a.id === selectedId) : undefined);
    } else {
      // Lite view doğru şıkkı `question.correctAnswer` üzerinden boyar.
      const correctAnswer = question.answers?.find((a) => a.id === r.correctAnswerId);
      this.question.set({ ...question, correctAnswerId: r.correctAnswerId ?? undefined, correctAnswer });
    }

    this.showPassageOnly.set(false);
    this.phase.set('feedback');
  }

  finishSession(): void {
    const session = this.session();
    if (!session) {
      this.resetToSetup();
      return;
    }
    this.run(this.practiceService.endSession(session.id), () => this.finishSession(), (ended) => {
      this.session.set(ended);
      this.answeredCount.set(ended.answeredCount);
      this.correctCount.set(ended.correctCount);
      this.skippedCount.set(ended.skippedCount);
      this.phase.set('ended');
      this.clearSessionParam();
    });
  }

  /** Boş durumdan / özetten kuruluma dön; aktif oturum varsa önce sonlandır. */
  changeScope(): void {
    const session = this.session();
    if (session && session.status === 'Active') {
      this.run(this.practiceService.endSession(session.id), () => this.changeScope(), () => this.resetToSetup());
      return;
    }
    this.resetToSetup();
  }

  onHeaderBack(): void {
    if (this.phase() === 'setup' || this.phase() === 'ended') {
      void this.router.navigate(['/dashboard']);
      return;
    }
    this.finishSession();
  }

  retry(): void {
    const action = this.retryAction;
    this.error.set(null);
    action?.();
  }

  showQuestion(): void {
    this.showPassageOnly.set(false);
  }

  showPassage(): void {
    if (this.isPassageFirstActive()) this.showPassageOnly.set(true);
  }

  // ── Yardımcılar ─────────────────────────────────────────────────────────────

  private resetToSetup(): void {
    this.session.set(null);
    this.question.set(null);
    this.region.set(null);
    this.result.set(null);
    this.selectedAnswerId.set(null);
    this.selectedChoice.set(undefined);
    this.answeredCount.set(0);
    this.correctCount.set(0);
    this.skippedCount.set(0);
    this.error.set(null);
    this.retryAction = null;
    this.phase.set('setup');
    this.clearSessionParam();
  }

  private clearSessionParam(): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { [SESSION_QUERY_PARAM]: null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  private elapsedSeconds(): number {
    return Math.max(0, Math.round((Date.now() - this.questionShownAt) / 1000));
  }

  /** Tek noktadan busy / error / retry yönetimi. */
  private run<T>(source: Observable<T>, retry: () => void, onSuccess: (value: T) => void): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.retryAction = retry;

    source.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (value) => {
        this.busy.set(false);
        onSuccess(value);
      },
      error: (err: unknown) => {
        this.busy.set(false);
        this.error.set(this.messageOf(err, 'İşlem tamamlanamadı. Lütfen tekrar deneyin.'));
      },
    });
  }

  private messageOf(err: unknown, fallback: string): string {
    if (err instanceof HttpErrorResponse) {
      const message = (err.error as { message?: string } | null)?.message;
      if (typeof message === 'string' && message.trim()) return message;
    }
    return fallback;
  }
}
