import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe, NgTemplateOutlet } from '@angular/common';
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
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';
import { QuestionCanvasViewComponentv5 } from '../../shared/components/question-canvas-view-v5/question-canvas-view-v5.component';
import { QuestionLiteViewComponent } from '../question-lite-view/question-lite-view.component';
import { PracticeService } from '../../services/practice.service';
import { StudentService } from '../../services/student.service';
import { SubjectService } from '../../services/subject.service';
import { AnswerChoice, QuestionRegion } from '../../models/draws';
import {
  PracticeAnswerResult,
  PracticeSession,
  PracticeSessionReview,
  PracticeSessionReviewQuestion,
} from '../../models/practice';
import { Question } from '../../models/question';
import { Subject } from '../../models/subject';
import { Paged } from '../../models/test-instance';
import { Topic } from '../../models/topic';

/**
 * Sayfa fazları:
 * - setup:    kapsam seçimi (ders / konu) — hiçbiri seçilmezse varsayılan kapsam (kendi sınıfı + tüm dersler);
 *             aynı ekranda "Geçmiş Oturumlar" listesi
 * - question: bekleyen soru gösteriliyor; "Pas Geç" / "Cevabı Gönder"
 * - feedback: doğru/yanlış/pas geri bildirimi; "Sonraki Soru"
 * - empty:    kapsamda soru yok ya da havuz tükendi; "Kapsamı Değiştir"
 * - ended:    oturum özeti
 * - review:   geçmiş bir oturumun soru soru incelemesi (salt okunur)
 */
type PracticePhase = 'setup' | 'question' | 'feedback' | 'empty' | 'ended' | 'review';

interface SubjectTopics {
  subject: Subject;
  topics: Topic[];
}

type ReviewStatusKind = 'correct' | 'wrong' | 'skipped' | 'pending';

/**
 * İnceleme ekranında tek sorunun görüntülenmeye hazır hâli. `worksheet-detail`'in
 * `regions` / `selectedChoices` / `correctChoices` üçlüsünün soru başına toplanmış karşılığı:
 * canvas sorular `region` + seçili/doğru `AnswerChoice`, metin soruları `question` (lite view).
 */
interface ReviewEntry {
  row: PracticeSessionReviewQuestion;
  kind: ReviewStatusKind;
  /** Lite view için; doğru şık `correctAnswer` olarak eşlenmiştir (Pending'de yok). */
  question: Question;
  /** Yalnız canvas sorularda dolu. */
  region: QuestionRegion | null;
  selectedChoice: AnswerChoice | undefined;
  correctChoice: AnswerChoice | undefined;
}

const SESSION_QUERY_PARAM = 'session';
const HISTORY_PAGE_SIZE = 10;
const EMPTY_HISTORY: Paged<PracticeSession> = { items: [], totalCount: 0, pageNumber: 1, pageSize: HISTORY_PAGE_SIZE };

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
    DatePipe,
    NgTemplateOutlet,
    RouterLink,
    MatButtonModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    SectionHeaderComponent,
    PaginationComponent,
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

  // ── Geçmiş oturumlar (kurulum ekranında) ────────────────────────────────────
  readonly historyLoading = signal(false);
  readonly historyError = signal<string | null>(null);
  readonly historyPaged = signal<Paged<PracticeSession>>(EMPTY_HISTORY);
  readonly historyPage = signal(1);
  readonly historyPageSize = HISTORY_PAGE_SIZE;
  readonly historyItems = computed(() => this.historyPaged().items);
  readonly historyTotal = computed(() => this.historyPaged().totalCount);

  private readonly subjectNameById = computed(() => new Map(this.subjects().map((s) => [s.id, s.name])));
  private readonly topicNameById = computed(() => {
    const names = new Map<number, string>();
    for (const topics of this.topicsBySubject().values()) for (const t of topics) names.set(t.id, t.name);
    return names;
  });

  // ── İnceleme (geçmiş oturum detayı) ─────────────────────────────────────────
  readonly reviewLoading = signal(false);
  readonly reviewError = signal<string | null>(null);
  readonly review = signal<PracticeSessionReview | null>(null);
  /** İncelemede gösterilen sorunun `reviewEntries` içindeki sırası. */
  readonly reviewIndex = signal(0);
  private reviewSessionId: number | null = null;

  readonly reviewSession = computed(() => this.review()?.session ?? null);
  readonly reviewQuestions = computed(() => this.review()?.questions ?? []);
  readonly reviewWrongCount = computed(() => {
    const s = this.reviewSession();
    return s ? Math.max(0, s.answeredCount - s.correctCount - s.skippedCount) : 0;
  });

  /**
   * Her inceleme satırı için canvas bölgesi + seçili/doğru şık bir kez kurulur
   * (`worksheet-detail.loadResultsForInstance` deseni). Pending satırda sunucu doğru şıkkı
   * gizlediğinden `correctChoice`/`correctAnswer` boş kalır; bu kasıtlıdır.
   */
  readonly reviewEntries = computed<ReviewEntry[]>(() =>
    this.reviewQuestions().map((row) => this.toReviewEntry(row))
  );
  readonly reviewEntry = computed<ReviewEntry | null>(() => this.reviewEntries()[this.reviewIndex()] ?? null);
  readonly reviewHasPrev = computed(() => this.reviewIndex() > 0);
  readonly reviewHasNext = computed(() => this.reviewIndex() < this.reviewEntries().length - 1);

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
    this.loadHistory(1);

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

  // ── Geçmiş oturumlar ────────────────────────────────────────────────────────

  loadHistory(page: number = this.historyPage()): void {
    this.historyLoading.set(true);
    this.historyError.set(null);
    this.historyPage.set(page);

    this.practiceService
      .listSessions(page, HISTORY_PAGE_SIZE)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (paged) => {
          this.historyPaged.set(paged);
          this.historyLoading.set(false);
        },
        error: (err: unknown) => {
          this.historyError.set(this.messageOf(err, 'Geçmiş oturumlar yüklenemedi.'));
          this.historyLoading.set(false);
        },
      });
  }

  onHistoryPageChange(page: number): void {
    if (page !== this.historyPage()) this.loadHistory(page);
  }

  /** Liste öğesi yalnız id taşır; adlar kurulum ekranının zaten yüklediği ders/konu kaynağından çözülür. */
  scopeLabel(session: PracticeSession): string {
    if (session.subjectIds.length === 0 && session.topicIds.length === 0) return 'Tüm dersler';

    const subjectNames = this.subjectNameById();
    const parts: string[] = [];
    const subjects = session.subjectIds.map((id) => subjectNames.get(id) ?? `Ders #${id}`);
    if (subjects.length) parts.push(subjects.join(', '));

    if (session.topicIds.length) {
      const topicNames = this.topicNameById();
      const resolved = session.topicIds.map((id) => topicNames.get(id)).filter((n): n is string => !!n);
      parts.push(
        resolved.length === session.topicIds.length
          ? resolved.join(', ')
          : `${session.topicIds.length} konu`
      );
    }
    return parts.join(' · ');
  }

  /** Cevaplanan sorular içinde (pas hariç) doğru yüzdesi. */
  accuracyOf(session: PracticeSession): number {
    const graded = session.answeredCount - session.skippedCount;
    return graded > 0 ? Math.round((session.correctCount / graded) * 100) : 0;
  }

  wrongCountOf(session: PracticeSession): number {
    return Math.max(0, session.answeredCount - session.correctCount - session.skippedCount);
  }

  /** Bitmiş oturum süresi ("12 dk" / "45 sn"); aktif oturumda boş. */
  durationLabel(session: PracticeSession): string {
    if (!session.endTime) return '';
    const seconds = Math.max(0, Math.round((Date.parse(session.endTime) - Date.parse(session.startTime)) / 1000));
    return seconds < 60 ? `${seconds} sn` : `${Math.round(seconds / 60)} dk`;
  }

  /** Listeden tıklama: aktif oturum kaldığı yerden devam eder, bitmiş oturum incelemeye açılır. */
  openHistoryItem(session: PracticeSession): void {
    if (session.status === 'Active') {
      this.resumeSession(session.id);
      return;
    }
    this.openReview(session.id);
  }

  // ── İnceleme ────────────────────────────────────────────────────────────────

  openReview(sessionId: number): void {
    this.reviewSessionId = sessionId;
    this.review.set(null);
    this.reviewIndex.set(0);
    this.error.set(null);
    this.phase.set('review');
    this.loadReview();
  }

  loadReview(): void {
    const sessionId = this.reviewSessionId;
    if (sessionId == null) return;

    this.reviewLoading.set(true);
    this.reviewError.set(null);

    this.practiceService
      .getSessionReview(sessionId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (review) => {
          this.review.set(review);
          this.reviewIndex.set(0);
          this.reviewLoading.set(false);
        },
        error: (err: unknown) => {
          this.reviewError.set(this.messageOf(err, 'Oturum detayı yüklenemedi.'));
          this.reviewLoading.set(false);
        },
      });
  }

  /** İncelemeden kurulum + geçmiş listesine dön; liste yeniden yüklenir. */
  closeReview(): void {
    this.reviewSessionId = null;
    this.review.set(null);
    this.resetToSetup();
  }

  /** Numaralı şeritten ya da ileri/geri düğmesinden soru seçimi (`worksheet-detail.questionSelected`). */
  selectReviewQuestion(index: number): void {
    if (index < 0 || index >= this.reviewEntries().length) return;
    this.reviewIndex.set(index);
  }

  reviewPrev(): void {
    this.selectReviewQuestion(this.reviewIndex() - 1);
  }

  reviewNext(): void {
    this.selectReviewQuestion(this.reviewIndex() + 1);
  }

  reviewStatusKind(question: PracticeSessionReviewQuestion): ReviewStatusKind {
    if (question.status === 'Pending') return 'pending';
    if (question.status === 'Skipped' || question.isSkipped) return 'skipped';
    return question.isCorrect ? 'correct' : 'wrong';
  }

  reviewKindLabel(kind: ReviewStatusKind): string {
    switch (kind) {
      case 'correct':
        return 'Doğru';
      case 'wrong':
        return 'Yanlış';
      case 'skipped':
        return 'Pas';
      default:
        return 'Cevaplanmadı';
    }
  }

  private toReviewEntry(row: PracticeSessionReviewQuestion): ReviewEntry {
    const source = row.question;
    const correctAnswerId = source.correctAnswerId ?? null;
    const correctAnswer =
      source.correctAnswer ??
      (correctAnswerId != null ? source.answers?.find((a) => a.id === correctAnswerId) : undefined);
    // Lite view doğru şıkkı `question.correctAnswer` üzerinden boyar (applyResult ile aynı).
    const question: Question = { ...source, correctAnswerId: correctAnswerId ?? undefined, correctAnswer };

    if (!source.isCanvasQuestion) {
      return {
        row,
        kind: this.reviewStatusKind(row),
        question,
        region: null,
        selectedChoice: undefined,
        correctChoice: undefined,
      };
    }

    // v5 seçimi referans eşitliğiyle karşılaştırır: şıklar YENİ bölgenin nesnelerinden alınır.
    const region = this.practiceService.toQuestionRegion(source, correctAnswerId);
    return {
      row,
      kind: this.reviewStatusKind(row),
      question,
      region,
      selectedChoice:
        row.selectedAnswerId != null ? region.answers.find((a) => a.id === row.selectedAnswerId) : undefined,
      correctChoice: correctAnswerId != null ? region.answers.find((a) => a.id === correctAnswerId) : undefined,
    };
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

  /**
   * Sayfa yenilemede `?session=` ile ya da geçmiş listesinden aktif oturuma geri döner;
   * bitmişse kurulumda kalır.
   */
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
      void this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { [SESSION_QUERY_PARAM]: session.id },
        replaceUrl: true,
      });
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
    if (this.phase() === 'review') {
      this.closeReview();
      return;
    }
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
    // Yeni biten/başlayan oturum listede görünsün.
    this.loadHistory(1);
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
