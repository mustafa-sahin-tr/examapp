import { Component, DestroyRef, computed, effect, inject, signal, untracked, viewChild, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe, DecimalPipe } from '@angular/common';
import { Subject, firstValueFrom, takeUntil } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleChange, MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectChange, MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import {
  ActiveInspector,
  ImageSelectorComponent,
  TrainedPage,
} from '../image-selector/image-selector.component';
import {
  ACTIVITY_COMPONENTS,
  ActivityComponent,
  BLOCK_KINDS,
  BlockInteractionType,
  BlockKind,
} from '../../models/prediction';
import { DRAG_PLACE } from '../../models/drag-place';
import { MATCH } from '../../models/match';
import { QuestionDetectorService } from '../../services/question-detector.service';
import { GradesService } from '../../services/grades.service';
import { Grade } from '../../models/student';
import {
  LabelingCandidate,
  filePageOf,
  isLabelingImage,
  pageIdOf,
  pickNextUnlabeled,
  sortLabelingFiles,
} from './labeling-pool';
import {
  DetectionSource,
  LabelingAiModel,
  LabelingBankBox,
  LabelingBankConflict,
  LabelingBankInfo,
  LabelingBankQuestion,
  LabelingPage,
  LabelingResult,
} from '../../models/labeling';
import { LabelingService, manualDetectionSource, sha256OfDataUrl } from '../../services/labeling.service';
import {
  LabelingPromptDialogComponent,
  LabelingPromptDialogResult,
  isActivityPromptSaved,
  isPromptUnchanged,
} from './labeling-prompt-dialog/labeling-prompt-dialog.component';
import { LabelingBook, LabelingBookPageInfo, LabelingCropConflict } from '../../models/labeling-book';
import { LabelingBookService } from '../../services/labeling-book.service';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../shared/components/confirm-dialog/confirm-dialog.component';
import {
  LabelingCropDuplicateDialogComponent,
  LabelingCropDuplicateDialogData,
} from './labeling-crop-duplicate-dialog/labeling-crop-duplicate-dialog.component';

/** Sayfa metinleri kendi Transloco scope'unda: `public/i18n/labeling/<lang>.json`. */
const LABELING_SCOPE = 'labeling';
/** Yalnız son seçilen şık sayısı saklanır (oturum sayaçları / havuz saklanmaz). */
const ANSWER_COUNT_STORAGE_KEY = 'labeling.answerCount';
const ANSWER_COUNTS = [3, 4, 5] as const;
/** Kayıtlı manuel yoksa sayfa hangi kaynakla açılsın (Model = YOLO, Ai = Claude) ve seçili Claude modeli. */
const SOURCE_STORAGE_KEY = 'labeling.source';
const AI_MODEL_STORAGE_KEY = 'labeling.aiModel';
/** Havuza kaydedilen sayfanın sınıfı (issue #565); yalnız listede hâlâ varsa geri yüklenir. */
const GRADE_STORAGE_KEY = 'labeling.gradeId';
/** Issue #577: seçili kaynak kitap; yalnız listede hâlâ varsa geri yüklenir. */
const BOOK_STORAGE_KEY = 'labeling.bookId';
/** Soru başına etkinlik / madde etiketi üst sınırı (sunucuyla aynı). */
export const MAX_QUESTION_LABEL = 50;

/** Müfettişte soru başına girilen kitap etiketleri (bölge adına göre; sayfa değişince silinir). */
export interface QuestionLabels {
  activityLabel: string;
  itemLabel: string;
}

type PreferredSource = 'Model' | 'Ai';

type DrawMode = 'question' | 'answer';

/**
 * İşaretleme modu: soru tespit modeline eğitim verisi üretir; "Havuza kaydet" ayrıca sayfadaki soruları teste
 * bağlamadan soru bankasına yazar (issue #470 — doğru şık dahil, sınıflandırma sunucuda otomatik başlar).
 * Seçilen kök klasörün (alt klasörler dahil) görselleri klasör ve dosya adı sırasıyla gezilir; daha önce eğitime
 * alınmamış ilk sayfa açılır; tuval, tespit ve doğrulama `ImageSelectorComponent`'ten gelir. "Eğit" sayfayı tamamı işaretlenmiş
 * (`pageComplete: true`) gönderir; "Atla" eğitmeden sonrakine geçer. Aynı oturumda bir dosya iki kez gelmez.
 */
@Component({
  selector: 'app-labeling',
  standalone: true,
  imports: [
    MatButtonModule,
    MatButtonToggleModule,
    MatFormFieldModule,
    MatSelectModule,
    DatePipe,
    DecimalPipe,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    RouterLink,
    TranslocoDirective,
    ImageSelectorComponent,
  ],
  providers: [provideTranslocoScope(LABELING_SCOPE)],
  templateUrl: './labeling.component.html',
  styleUrls: ['./labeling.component.scss'],
  changeDetection: ChangeDetectionStrategy.Eager,
  host: { '(document:keydown)': 'onKeydown($event)' },
})
export class LabelingComponent {
  private readonly detector = inject(QuestionDetectorService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly labelingApi = inject(LabelingService);
  private readonly dialog = inject(MatDialog);
  private readonly gradesApi = inject(GradesService);
  private readonly booksApi = inject(LabelingBookService);

  private readonly selectorRef = viewChild(ImageSelectorComponent);

  readonly answerCounts = ANSWER_COUNTS;
  /** Issue #578: müfettişteki Tür / Bileşen seçenekleri ve araç çubuğu lejantı. */
  readonly blockKinds = BLOCK_KINDS;
  readonly activityComponents = ACTIVITY_COMPONENTS;
  /** Sayfa id'si (sha256[:16]); testlerde gecikmeli hash için değiştirilir. */
  hashFile: (file: File) => Promise<string> = (file) => pageIdOf(file);

  // --- Daha önce işaretlenmiş sayfalar (question-detector) ---
  readonly labeledLoading = signal(false);
  readonly labeledError = signal(false);
  private readonly labeled = new Set<string>();

  // --- Havuz ve sayaçlar ---
  /** Kalan, henüz gösterilmemiş dosyalar (seçilen her dosya çıkarılır). */
  private pool: File[] = [];
  readonly folderPicked = signal(false);
  readonly totalImages = signal(0);
  readonly skippedLabeled = signal(0);
  readonly unreadable = signal(0);
  readonly trainedThisSession = signal(0);
  readonly passedThisSession = signal(0);
  /** Gösterilen (tuvale açılan) sayfa sayısı; açık sayfa dahil. */
  private readonly shownCount = signal(0);
  /**
   * Henüz gösterilmemiş ve atlanmamış görseller. Havuz boyundan değil sayaçlardan türetilir: arka planda hash'i
   * alınan aday havuzdan çıkmış olsa da gösterilene kadar "kalan" sayılır.
   */
  readonly remaining = computed(() =>
    Math.max(0, this.totalImages() - this.shownCount() - this.skippedLabeled() - this.unreadable())
  );
  /** Havuz tükendi: aranacak sayfa kalmadı. */
  private readonly exhausted = signal(false);

  readonly current = signal<LabelingCandidate<File> | null>(null);
  /** Sağ panel (Soru Müfettişi) açık mı. */
  readonly showInspector = signal(true);
  readonly picking = signal(false);
  readonly answerCount = signal<number>(readStoredAnswerCount());

  /** Yeni klasör seçimi / yok edilme eski aramaları geçersiz kılar. */
  private pickSeq = 0;
  /** Kullanıcı sayfayı işaretlerken arka planda bulunan sonraki aday. */
  private prefetch: Promise<LabelingCandidate<File> | null> | null = null;
  /**
   * "Eğit"e basıldığında açık olan sayfa ve klasör oturumu; yanıt geldiğinde aynı oturumda hâlâ o sayfa açıksa
   * sayılır ve sonrakine geçilir.
   */
  private trainingFor: { candidate: LabelingCandidate<File>; seq: number; bankCount?: number } | null = null;

  readonly sendingToFix = computed(() => this.selectorRef()?.sendingToFix() ?? false);
  readonly analysing = computed(() => {
    const selector = this.selectorRef();
    return (selector?.pageLoading() ?? false) || (selector?.inProgress() ?? false);
  });
  /** Atla: yeni sayfa aranırken ya da eğitime gönderim sürerken kapalı (analiz sürerken geçilebilir). */
  readonly canSkip = computed(() => !!this.current() && !this.picking() && !this.sendingToFix());
  /** Açık adayın görseli okunamadı / çözülemedi. */
  readonly pageLoadFailed = computed(() => !!this.current() && (this.selectorRef()?.pageLoadFailed() ?? false));
  /**
   * Tespit başarısız ve tuvalde hiç soru yok: bu "boş sayfa" değil, gönderilirse tam sayfa arka plan örneği
   * olarak eğitime girerdi. Kullanıcı yeniden tespit eder ya da kutuları elle çizer.
   */
  readonly predictFailedEmpty = computed(() => {
    const selector = this.selectorRef();
    return !!this.current() && !!selector && selector.predictFailed() && selector.regions().length === 0;
  });
  /**
   * Eğit: açık adayın görseli yüklenmiş (okuma bitti, hata yok), analiz / gönderim sürmüyor ve tespit hatası
   * boş bir tuval bırakmamış olmalı.
   */
  readonly canTrain = computed(() => {
    const selector = this.selectorRef();
    return (
      this.canSkip() &&
      !this.bankSaving() &&
      !this.analysing() &&
      !!selector?.imageData() &&
      !this.pageLoadFailed() &&
      !this.predictFailedEmpty()
    );
  });
  readonly selectionMode = computed(() => this.selectorRef()?.selectionMode() ?? null);

  // --- Soru bankasına kayıt (issue #470; eğitime gönderimden bağımsız) ---
  readonly bankSaving = signal(false);
  /** Açık sayfanın bankaya son kaydı (yoksa null). */
  readonly bankRecord = computed(() => this.pageRecord()?.bank ?? null);
  /** Sınıf listesi (`GET /api/exam/worksheet/grades`); havuza kayıt seçili sınıfı taşır (issue #565). */
  readonly grades = signal<Grade[]>([]);
  readonly gradesLoading = signal(false);
  readonly gradesError = signal(false);
  readonly gradeId = signal<number | null>(null);
  /**
   * Kullanıcının elle seçtiği (localStorage'daki) sınıf. İçindekiler sınıfı yalnız o sayfa için `gradeId`'yi değiştirir;
   * içindekiler olmayan sayfada bu sınıfa dönülür (CR11 — TOC sınıfı sonraki sayfaya sızmaz).
   */
  private readonly manualGradeId = signal<number | null>(null);
  /** Eğit ile aynı ön koşullar + en az bir soru + seçili sınıf; kayıt sürerken kapalı. */
  readonly canSaveToBank = computed(
    () =>
      this.gradeId() != null &&
      this.canTrain() &&
      !this.bankSaving() &&
      (this.selectorRef()?.regions().length ?? 0) > 0
  );

  // --- Kaynak: Manuel kayıt / Model (YOLO) / Claude ---
  /** İzinli Claude modelleri; boşsa (yetki / yapılandırma yok) Claude seçeneği kapalı. */
  readonly aiModels = signal<LabelingAiModel[]>([]);
  readonly aiModelId = signal<string>(readStored(AI_MODEL_STORAGE_KEY) ?? '');
  /** Manuel kayıt yoksa sayfanın açıldığı kaynak. */
  readonly preferredSource = signal<PreferredSource>(readStored(SOURCE_STORAGE_KEY) === 'Ai' ? 'Ai' : 'Model');
  /** Açık sayfanın exam API'deki kaydı (yoksa null). */
  readonly pageRecord = signal<LabelingPage | null>(null);
  /** Son Claude yanıtı kayıttan mı geldi (maliyetsiz) yoksa canlı mı. */
  readonly aiOrigin = signal<'live' | 'cached' | null>(null);
  readonly aiBusy = signal(false);
  /** Açık sayfanın görseli (sayfa değişince eski Claude yanıtı uygulanmasın diye `pageToken` ile birlikte). */
  private pageImage: string | null = null;
  private pageToken = 0;
  /** Sayfa değişti / bileşen kapandı: süren Claude isteği iptal edilir (sunucu da Claude çağrısını keser). */
  private readonly pageChanged = new Subject<void>();

  readonly detectionSource = computed<DetectionSource | null>(() => this.selectorRef()?.detectionSource() ?? null);
  readonly manualRecord = computed(() => this.pageRecord()?.results.find((r) => r.source === 'Manual') ?? null);
  /** Tuvalde gösterilen Claude sonucunun kaydı (tarih, maliyet). */
  readonly aiRecord = computed(() => {
    const source = this.detectionSource();
    if (source?.kind !== 'Ai') {
      return null;
    }
    return this.pageRecord()?.results.find((r) => r.source === 'Ai' && r.modelName === source.modelName) ?? null;
  });
  readonly selectedAiModel = computed(() => this.aiModels().find((m) => m.id === this.aiModelId()) ?? null);
  /** Issue #578: tür renkleri lejantı yalnız Claude sonucu (ya da Claude'dan başlayan manuel kayıt) açıkken. */
  readonly showKindLegend = computed(() => {
    const source = this.detectionSource();
    return source?.kind === 'Ai' || (source?.kind === 'Manual' && source.base?.source === 'Ai');
  });
  /** Analiz / gönderim sürerken kaynak değiştirilmez. */
  readonly sourceLocked = computed(() => !this.current() || this.analysing() || this.sendingToFix());
  readonly finished = computed(
    () => this.folderPicked() && this.totalImages() > 0 && !this.current() && !this.picking() && this.exhausted()
  );
  readonly currentPath = computed(() => {
    const file = this.current()?.file;
    return file ? file.webkitRelativePath || file.name : '';
  });

  // --- Kaynak kitap (issue #577): dosya sayfası → basılı sayfa, cevap anahtarı, içindekiler varsayılanı ---
  readonly maxQuestionLabel = MAX_QUESTION_LABEL;
  /** Kitap listesi; yetki yoksa / hata olursa boş (kitap seçimi gizlenir, eski davranış). */
  readonly books = signal<LabelingBook[]>([]);
  readonly booksError = signal(false);
  readonly bookId = signal<number | null>(null);
  readonly selectedBook = computed(() => this.books().find((b) => b.id === this.bookId()) ?? null);
  /** Açık görselin dosya adından okunan sayfa numarası (`page_040.webp` → 40, `kitap_12.png` → 12). */
  readonly filePage = computed(() => (this.current() ? filePageOf(this.currentPath()) : null));
  readonly bookPage = signal<LabelingBookPageInfo | null>(null);
  readonly bookPageLoading = signal(false);
  readonly bookPageError = signal(false);
  private bookPageSeq = 0;
  /** İçindekilerden gelen sınıflandırma ("Fen Bilimleri › Maddenin Hâlleri"). */
  readonly tocLabel = computed(() => {
    const toc = this.bookPage()?.toc;
    if (!toc) {
      return null;
    }
    return [toc.subjectName, toc.topicName, toc.subTopicName].filter((part): part is string => !!part).join(' › ');
  });
  /** Soru başına etkinlik / madde etiketleri (bölge adı → etiketler). */
  readonly questionLabels = signal<Record<string, QuestionLabels>>({});

  /**
   * C bileşeninin (sürükle-yerleştir ailesi) tip seçenekleri. Issue #581: taslak yapı artık soru tuvalinde çıkarılır; tip
   * manuel kayıtta (eğitim / sonraki tuval açılışı) korunur.
   */
  readonly componentCTypes: readonly BlockInteractionType[] = [DRAG_PLACE, MATCH];

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.pickSeq++;
      this.prefetch = null;
      this.resetPageState();
    });
    // Alt bileşen hazır olunca (ve şık sayısı değişince) tuvale aktarılır. Kalıcı otomatik hizalama bu modda kapalı:
    // "Hizala" tek seferlik çalışır.
    effect(() => {
      const selector = this.selectorRef();
      const count = this.answerCount();
      if (!selector) {
        return;
      }
      untracked(() => {
        selector.autoAlign.set(false);
        if (selector.answerCount() !== count) {
          selector.answerCount.set(count);
          selector.recomputeWarnings();
        }
      });
    });
    // Kitap ya da sayfa değişince sayfanın kitap bilgisi (basılı sayfa, içindekiler) yeniden sorulur.
    effect(() => {
      const bookId = this.bookId();
      const filePage = this.filePage();
      untracked(() => this.loadBookPage(bookId, filePage));
    });
    void this.loadLabeledPages();
    this.loadAiModels();
    this.loadGrades();
    this.loadBooks();
  }

  loadBooks(): void {
    this.booksError.set(false);
    this.booksApi
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (books) => {
          const list = books ?? [];
          this.books.set(list);
          const stored = Number(readStored(BOOK_STORAGE_KEY));
          const current = this.bookId() ?? stored;
          this.bookId.set(list.some((b) => b.id === current) ? current : null);
        },
        error: (error) => {
          // Yetki yok / servis kapalı: kitapsız devam (sınıf elle seçilir).
          console.error('labeling books failed', error);
          this.books.set([]);
          this.booksError.set(true);
        },
      });
  }

  setBook(event: MatSelectChange): void {
    const id = (event.value as number | null) ?? null;
    this.bookId.set(id);
    writeStored(BOOK_STORAGE_KEY, id == null ? '' : String(id));
  }

  /**
   * Sayfanın kitap bilgisi. İçindekiler sınıf veriyorsa sınıf seçimi o sayfa için ona çekilir (kullanıcı yine
   * değiştirebilir; localStorage'a yazılmaz). Her yeni sayfada önce elle seçilen sınıfa dönülür. Geç gelen yanıt
   * `bookPageSeq` ile ayıklanır.
   */
  private loadBookPage(bookId: number | null, filePage: number | null): void {
    const seq = ++this.bookPageSeq;
    this.bookPage.set(null);
    this.bookPageError.set(false);
    this.gradeId.set(this.manualGradeId());
    if (bookId == null || filePage == null) {
      this.bookPageLoading.set(false);
      return;
    }
    this.bookPageLoading.set(true);
    this.booksApi
      .getPage(bookId, filePage)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (info) => {
          if (seq !== this.bookPageSeq) {
            return;
          }
          this.bookPageLoading.set(false);
          this.bookPage.set(info);
          const gradeId = info?.toc?.gradeId;
          // Sınıf listesi henüz gelmediyse `loadGrades` seçimi listeye göre doğrular.
          if (gradeId != null && (this.grades().length === 0 || this.grades().some((g) => g.id === gradeId))) {
            this.gradeId.set(gradeId);
          }
        },
        error: (error) => {
          if (seq !== this.bookPageSeq) {
            return;
          }
          console.error('labeling book page failed', error);
          this.bookPageLoading.set(false);
          this.bookPageError.set(true);
        },
      });
  }

  retryBookPage(): void {
    this.loadBookPage(this.bookId(), this.filePage());
  }

  /** Müfettişteki seçili sorunun etiketleri. */
  labelsOf(name: string): QuestionLabels {
    return this.questionLabels()[name] ?? { activityLabel: '', itemLabel: '' };
  }

  setQuestionLabel(name: string, field: keyof QuestionLabels, target: EventTarget | null): void {
    const value = ((target as HTMLInputElement | null)?.value ?? '').slice(0, MAX_QUESTION_LABEL);
    this.questionLabels.update((all) => ({ ...all, [name]: { ...this.labelsOf(name), [field]: value } }));
  }

  loadGrades(): void {
    this.gradesLoading.set(true);
    this.gradesError.set(false);
    this.gradesApi
      .getGrades()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (grades) => {
          const list = grades ?? [];
          this.grades.set(list);
          this.gradesLoading.set(false);
          const stored = Number(readStored(GRADE_STORAGE_KEY));
          const manual = this.manualGradeId() ?? stored;
          this.manualGradeId.set(list.some((g) => g.id === manual) ? manual : null);
          // İçindekiler sınıfı (liste gelmeden uygulanmış olabilir) listede varsa kalır, yoksa elle seçilene dönülür.
          const tocGrade = this.bookPage()?.toc?.gradeId ?? null;
          this.gradeId.set(tocGrade != null && list.some((g) => g.id === tocGrade) ? tocGrade : this.manualGradeId());
        },
        error: (error) => {
          console.error('labeling grades failed', error);
          this.grades.set([]);
          this.gradesLoading.set(false);
          this.gradesError.set(true);
        },
      });
  }

  setGrade(event: MatSelectChange): void {
    const id = event.value as number;
    this.gradeId.set(id);
    this.manualGradeId.set(id);
    writeStored(GRADE_STORAGE_KEY, String(id));
  }

  /**
   * CR1: seçili sınıf içindekiler sınıfından farklı — sunucu kitap konusunu uygulamaz (sınıflandırıcı seçili sınıfla
   * çalışır); uyarı gösterilir, içindekiler etiketi üstü çizili.
   */
  readonly tocGradeMismatch = computed(() => {
    const tocGrade = this.bookPage()?.toc?.gradeId;
    return tocGrade != null && this.gradeId() != null && this.gradeId() !== tocGrade;
  });

  private loadAiModels(): void {
    this.labelingApi
      .getAiModels()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (models) => {
          this.aiModels.set(models ?? []);
          if (!models?.some((m) => m.id === this.aiModelId())) {
            this.aiModelId.set(models?.find((m) => m.isDefault)?.id ?? models?.[0]?.id ?? '');
          }
        },
        error: (error) => {
          // Yetki yok (403) ya da servis kapalı: Claude seçeneği gizlenir, YOLO ile devam.
          console.error('labeling ai-models failed', error);
          this.aiModels.set([]);
        },
      });
  }

  /**
   * Sayfa görseli hazır (`externalDetection`): kayıtlı manuel varsa o, yoksa tercih edilen kaynak (YOLO taze /
   * Claude kayıtlıysa kayıttan) gösterilir. Kayıt sorgusu başarısızsa tercih edilen kaynakla devam edilir.
   */
  async onPageReady(imageData: string): Promise<void> {
    const selector = this.selectorRef();
    if (!selector) {
      return;
    }
    this.resetPageState();
    const token = this.pageToken;
    this.pageImage = imageData;
    const seq = selector.beginExternalDetection();

    let page: LabelingPage | null = null;
    try {
      const sha = await sha256OfDataUrl(imageData);
      if (token !== this.pageToken) {
        return;
      }
      page = await this.fetchPage(sha);
    } catch (error) {
      console.error('labeling page lookup failed', error);
    }
    if (token !== this.pageToken) {
      return;
    }
    this.pageRecord.set(page);
    const manual = page?.results.find((r) => r.source === 'Manual');
    if (manual) {
      selector.applyExternalPrediction(seq, manual.predictions, manualDetectionSource(manual));
      return;
    }
    if (this.preferredSource() === 'Ai' && this.aiModels().length > 0) {
      void this.runClaude(false, true);
    } else {
      selector.predict();
    }
  }

  /** Eğitime gönderilen hal kaydedildi: kart güncellensin (sayfa hâlâ açıksa). */
  async onManualSaved(imageData: string): Promise<void> {
    if (imageData !== this.pageImage) {
      return;
    }
    const token = this.pageToken;
    try {
      const page = await this.fetchPage(await sha256OfDataUrl(imageData));
      if (token === this.pageToken) {
        this.pageRecord.set(page);
      }
    } catch (error) {
      console.error('labeling page refresh failed', error);
    }
  }

  onSourceToggle(event: MatButtonToggleChange): void {
    const kind = event.value as 'Manual' | 'Model' | 'Ai';
    // Seçim onaya bağlı: grup gösterilen kaynağı göstermeye devam etsin, değişim uygulanınca güncellenir.
    event.source.buttonToggleGroup.value = this.detectionSource()?.kind ?? null;
    if (this.sourceLocked()) {
      return;
    }
    this.confirmDiscard(() => {
      if (kind !== 'Manual') {
        this.setPreferredSource(kind);
      }
      this.applySource(kind);
    });
  }

  setAiModel(event: MatSelectChange): void {
    const id = event.value as string;
    const apply = () => {
      this.aiModelId.set(id);
      writeStored(AI_MODEL_STORAGE_KEY, id);
      if (this.detectionSource()?.kind === 'Ai') {
        void this.runClaude(false, false);
      }
    };
    if (this.detectionSource()?.kind === 'Ai' && this.selectorRef()?.hasUnsavedEdits()) {
      // Onaya kadar liste tuvaldeki modeli göstersin.
      event.source.value = this.aiModelId();
      this.confirmDiscard(apply);
      return;
    }
    apply();
  }

  /**
   * Claude prompt ayarları. Kaydedilince sonraki çağrılar yeni sürümü kullanır (eski sonuçlar kayıttan dönmez); tuvalde
   * Claude sonucu varsa bu sayfayı yeni prompt ile sormak snackbar eylemiyle önerilir.
   */
  openPromptDialog(): void {
    const token = this.pageToken;
    this.dialog
      .open<LabelingPromptDialogComponent, unknown, LabelingPromptDialogResult>(LabelingPromptDialogComponent, {
        width: '820px',
        maxWidth: '95vw',
        autoFocus: false,
      })
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((result) => {
        if (!result) {
          return;
        }
        if (isPromptUnchanged(result)) {
          // Aynı içerik kaydedildi: sürüm açılmadı, yeniden sorma önerilmez.
          this.snackBar.open(this.tr('structure.promptUnchanged', { version: result.version }), this.tr('ok'), {
            duration: 4000,
          });
          return;
        }
        if (isActivityPromptSaved(result)) {
          this.onActivityPromptSaved(result.interactionType, result.prompt.version);
          return;
        }
        const message = this.tr('source.promptSaved', { version: result.active.version });
        if (this.detectionSource()?.kind !== 'Ai' || token !== this.pageToken) {
          this.snackBar.open(message, this.tr('ok'), { duration: 4000 });
          return;
        }
        this.snackBar
          .open(message, this.tr('source.promptAskPage'), { duration: 8000 })
          .onAction()
          .subscribe(() => {
            if (token === this.pageToken && !this.sourceLocked()) {
              this.confirmDiscard(() => void this.runClaude(false, false));
            }
          });
      });
  }

  /**
   * Issue #579 / #581: bir tipin talimatı kaydedildi. Taslaklar bu ekranda değil soru tuvalinde çıkarılır; yalnız bildirilir.
   */
  private onActivityPromptSaved(type: BlockInteractionType, version: number): void {
    const typeName = this.tr(`structure.types.${type}`);
    this.snackBar.open(this.tr('structure.promptSaved', { type: typeName, version }), this.tr('ok'), { duration: 4000 });
  }

  /** Kayıtlı Claude sonucunu yok sayıp yeniden sorar (maliyetli): önce onay. */
  askClaudeAgain(): void {
    if (this.sourceLocked() || !this.selectedAiModel()) {
      return;
    }
    const message = this.selectorRef()?.hasUnsavedEdits()
      ? this.tr('source.askAgainConfirmEdits')
      : this.tr('source.askAgainConfirm');
    const token = this.pageToken;
    this.snackBar
      .open(message, this.tr('source.askAgainAction'), { duration: 8000 })
      .onAction()
      .subscribe(() => {
        // Onay başka bir sayfada gelirse (snackbar açıkken geçildi) ücretli çağrı o sayfaya yapılmaz.
        if (token === this.pageToken && !this.sourceLocked()) {
          void this.runClaude(true, false);
        }
      });
  }

  private applySource(kind: 'Manual' | 'Model' | 'Ai'): void {
    const selector = this.selectorRef();
    if (!selector || this.sourceLocked()) {
      return;
    }
    if (kind === 'Model') {
      selector.predict();
    } else if (kind === 'Ai') {
      void this.runClaude(false, false);
    } else {
      const manual = this.manualRecord();
      if (manual) {
        selector.applyExternalPrediction(
          selector.beginExternalDetection(),
          manual.predictions,
          manualDetectionSource(manual)
        );
      }
    }
  }

  /**
   * Claude ile tespit (kayıtlı sonuç varsa ve `force` değilse sunucu API'ye gitmez). Sayfa açılışında hata olursa
   * (`fallbackToModel`) YOLO ile devam edilir; elle seçimde tuval boş kalır, kullanıcı kaynağı değiştirir.
   */
  private async runClaude(force: boolean, fallbackToModel: boolean): Promise<void> {
    const selector = this.selectorRef();
    const model = this.aiModelId();
    const image = this.pageImage;
    if (!selector || !model || !image) {
      return;
    }
    const token = this.pageToken;
    const seq = selector.beginExternalDetection();
    this.aiBusy.set(true);
    try {
      const response = await firstValueFrom(
        this.labelingApi.aiDetect({ imageBase64: image, model, force }).pipe(takeUntil(this.pageChanged))
      );
      if (token !== this.pageToken) {
        return;
      }
      this.mergeResult(response.imageSha256, response.result);
      this.aiOrigin.set(response.origin);
      selector.applyExternalPrediction(seq, response.result.predictions, {
        kind: 'Ai',
        modelName: response.result.modelName,
        promptVersion: response.result.promptVersion,
      });
      if (response.origin === 'live' && response.result.estimatedCostUsd != null) {
        this.snackBar.open(
          this.tr('source.liveDone', { cost: response.result.estimatedCostUsd.toFixed(4) }),
          this.tr('ok'),
          { duration: 4000 }
        );
      }
    } catch (error) {
      if (token !== this.pageToken) {
        return;
      }
      console.error('labeling ai-detect failed', error);
      selector.failExternalDetection(seq);
      const serverMessage = (error as { error?: { message?: string } })?.error?.message;
      this.snackBar.open(serverMessage || this.tr('source.aiFailed'), this.tr('ok'), { duration: 6000 });
      if (fallbackToModel) {
        selector.predict();
      }
    } finally {
      if (token === this.pageToken) {
        this.aiBusy.set(false);
      }
    }
  }

  /** Kaydedilmemiş elle düzeltme varsa önce sorulur (snackbar eylemi), yoksa hemen uygulanır. */
  private confirmDiscard(action: () => void): void {
    if (!this.selectorRef()?.hasUnsavedEdits()) {
      action();
      return;
    }
    const token = this.pageToken;
    this.snackBar
      .open(this.tr('source.unsavedEdits'), this.tr('source.discardAction'), { duration: 8000 })
      .onAction()
      .subscribe(() => {
        if (token === this.pageToken && !this.sourceLocked()) {
          action();
        }
      });
  }

  /**
   * Yeni sayfa (ya da kapanış): eski sayfanın kaydı, Claude isteği ve onay snackbar'ı geçersiz olur; geç gelen yanıt
   * `pageToken` ile ayıklanır.
   */
  private resetPageState(): void {
    this.pageToken++;
    this.pageChanged.next();
    this.pageImage = null;
    this.pageRecord.set(null);
    this.aiOrigin.set(null);
    this.aiBusy.set(false);
    this.bankSaving.set(false);
    this.questionLabels.set({});
    this.snackBar.dismiss();
  }

  private setPreferredSource(kind: PreferredSource): void {
    this.preferredSource.set(kind);
    writeStored(SOURCE_STORAGE_KEY, kind);
  }

  private async fetchPage(sha: string): Promise<LabelingPage | null> {
    try {
      return await firstValueFrom(this.labelingApi.getPage(sha));
    } catch (error) {
      if ((error as { status?: number })?.status === 404) {
        return null;
      }
      throw error;
    }
  }

  private mergeResult(sha: string, result: LabelingResult): void {
    const page = this.pageRecord();
    const others = (page?.results ?? []).filter(
      (r) => !(r.source === result.source && r.modelName === result.modelName)
    );
    this.pageRecord.set({
      imageSha256: sha,
      width: page?.width ?? 0,
      height: page?.height ?? 0,
      results: [...others, result],
      bank: page?.bank ?? null,
    });
  }

  aiModelName(id: string | null | undefined): string {
    return this.aiModels().find((m) => m.id === id)?.displayName ?? id ?? '';
  }

  /** Eğitim veri setindeki sayfa id'lerini kümeye ekler. Hata durumunda seçim yapılmaz (tekrar dene). */
  async loadLabeledPages(): Promise<boolean> {
    this.labeledLoading.set(true);
    this.labeledError.set(false);
    try {
      const response = await firstValueFrom(this.detector.getLabeledPages());
      for (const id of response?.pageIds ?? []) {
        this.labeled.add(id);
      }
      return true;
    } catch (error) {
      console.error('labeled-pages failed', error);
      this.labeledError.set(true);
      return false;
    } finally {
      this.labeledLoading.set(false);
    }
  }

  /** Hata sonrası "Tekrar dene": liste gelirse ve klasör seçilmişse aramaya başlanır. */
  async retryLabeledPages(): Promise<void> {
    const ok = await this.loadLabeledPages();
    if (ok && this.folderPicked() && !this.current() && !this.picking()) {
      void this.showNext();
    }
  }

  async onFolderSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const files = sortLabelingFiles(Array.from(input.files ?? []).filter(isLabelingImage));
    // Aynı klasör tekrar seçilebilsin.
    input.value = '';

    this.pickSeq++;
    this.prefetch = null;
    // Süren eğitim isteği (önceki klasör) yanıtı yalnız id kümesine yazılır; bu oturumun sayacına girmez.
    this.pool = files;
    this.current.set(null);
    this.picking.set(false);
    this.exhausted.set(false);
    this.folderPicked.set(true);
    this.totalImages.set(files.length);
    this.shownCount.set(0);
    this.skippedLabeled.set(0);
    this.unreadable.set(0);
    this.trainedThisSession.set(0);
    this.passedThisSession.set(0);

    if (files.length === 0) {
      return;
    }
    // Başka bir oturumda bu arada eğitilenler de atlansın.
    const seq = this.pickSeq;
    const ok = await this.loadLabeledPages();
    if (ok && seq === this.pickSeq) {
      void this.showNext();
    }
  }

  /** Eğitmeden sıradaki sayfaya geç. */
  skip(): void {
    if (!this.canSkip()) {
      return;
    }
    this.passedThisSession.update((n) => n + 1);
    void this.showNext();
  }

  /** `bankCount`: sayfa az önce bankaya kaydedildi (Havuza kaydet akışı); eğitim bitince birleşik mesaj gösterilir. */
  train(bankCount?: number): void {
    const selector = this.selectorRef();
    if (!selector || !this.canTrain()) {
      return;
    }
    this.trainingFor = { candidate: this.current()!, seq: this.pickSeq, bankCount };
    // pageComplete: işaretleme modunda sayfanın tüm soruları işaretlenir (boş sayfa = arka plan örneği).
    selector.sendToFix(true);
  }

  onTrained(event: TrainedPage): void {
    const training = this.trainingFor;
    this.trainingFor = null;
    // Sunucunun kaydettiği sayfa her durumda veri setinde: tekrar getirilmesin.
    if (event.pageId) {
      this.labeled.add(event.pageId);
    }
    if (!training || training.seq !== this.pickSeq) {
      // Klasör bu arada değişti (ya da istek bu sayfadan değil): yeni oturumun sayacına / akışına dokunulmaz.
      return;
    }
    const candidate = training.candidate;
    if (event.pageId !== candidate.pageId) {
      // Gönderilen görsel açık aday değil (ör. eski sayfa verisi). Aday işaretli sayılmaz, ilerlenmez.
      console.error('labeling: page id mismatch', { candidate: candidate.pageId, server: event.pageId });
      this.snackBar.open(this.tr('pageIdMismatch'), this.tr('ok'), { duration: 8000 });
      return;
    }
    this.labeled.add(candidate.pageId);
    this.trainedThisSession.update((n) => n + 1);
    if (training.bankCount != null) {
      this.snackBar.open(this.tr('bank.savedAndTrained', { count: training.bankCount }), this.tr('ok'), {
        duration: 5000,
      });
    }
    if (candidate === this.current()) {
      void this.showNext();
    }
  }

  /**
   * Sayfadaki çoktan seçmeli soruları (doğru şık zorunlu) bankaya kaydeder, başarılıysa "Eğit" gibi eğitime gönderip
   * sonraki sayfaya geçer. Sayfa daha önce kaydedildiyse önce sorulur (yeniden kayıt soruları ikinci kez oluşturur).
   * Sürükle-bırak soruları bu ekrandan kaydedilmez.
   */
  saveToBank(): void {
    const selector = this.selectorRef();
    const image = this.pageImage;
    if (!selector || !image || !this.canSaveToBank()) {
      return;
    }
    // Etkileşimli sorular (sürükle-bırak, matris #574) ve soru dışı bloklar (#578: etkinlik, anlatım, cevap anahtarı,
    // gürültü) bu ekrandan bankaya kaydedilmez.
    const regions = selector.regions().filter((r) => selector.isMcqRegion(r.name));
    if (regions.length === 0) {
      this.snackBar.open(this.tr('bank.noMcq'), this.tr('ok'), { duration: 5000 });
      return;
    }
    // Şık sayısı + doğru şık; sorunlu ilk soru seçilir ve snackbar ile bildirilir.
    if (!selector.ensureQuestionsValid({ requireCorrectAnswer: true, skipInteractive: true })) {
      return;
    }
    const questions: LabelingBankQuestion[] = regions.map((q) => {
      const labels = this.labelsOf(q.name);
      const activityLabel = labels.activityLabel.trim();
      const itemLabel = labels.itemLabel.trim();
      return {
        ...roundBox(q),
        options: q.answers.map(roundBox),
        correctOptionIndex: q.answers.findIndex((a) => a.isCorrect),
        ...(activityLabel ? { activityLabel } : {}),
        ...(itemLabel ? { itemLabel } : {}),
      };
    });
    const existing = this.bankRecord();
    if (existing) {
      this.confirmBankAgain(existing, image, questions);
      return;
    }
    this.submitBank(image, questions, false);
  }

  private confirmBankAgain(existing: LabelingBankInfo, image: string, questions: LabelingBankQuestion[]): void {
    const token = this.pageToken;
    this.snackBar
      .open(this.tr('bank.alreadySaved', { count: existing.questionCount }), this.tr('bank.saveAgain'), {
        duration: 10000,
      })
      .onAction()
      .subscribe(() => {
        if (token === this.pageToken && this.canSaveToBank()) {
          this.submitBank(image, questions, true);
        }
      });
  }

  /**
   * İstek sayfa değişse de iptal edilmez (yarım kalmasın); yanıt yalnız aynı sayfa açıksa ekrana yansır. Kayıt başarılıysa
   * eğitime gönderilir (send-to-fix → manuel kayıt → sonraki sayfa); eğitim başarısız olursa sayfa açık kalır, "Eğit" ile
   * yeniden denenebilir (kayıt kartı bankada olduğunu gösterir). Kayıt başarısızsa eğitime gönderilmez.
   */
  private submitBank(image: string, questions: LabelingBankQuestion[], force: boolean): void {
    const gradeId = this.gradeId();
    if (gradeId == null) {
      return;
    }
    const token = this.pageToken;
    // Kaynak kitap yalnız seçiliyse ve dosya adından sayfa numarası okunduysa gider (sunucu içindekileri uygular).
    const bookId = this.bookId();
    const filePage = this.filePage();
    const source = bookId != null && filePage != null ? { bookId, filePage } : {};
    this.bankSaving.set(true);
    this.labelingApi.saveToBank({ imageBase64: image, questions, gradeId, force, ...source }).subscribe({
      next: (response) => {
        if (token !== this.pageToken) {
          return;
        }
        this.bankSaving.set(false);
        this.setBankRecord(response.imageSha256, response.bank);
        if (this.canTrain()) {
          this.train(response.bank.questionCount);
          return;
        }
        this.snackBar.open(this.tr('bank.saved', { count: response.bank.questionCount }), this.tr('ok'), {
          duration: 5000,
        });
      },
      error: (error) => {
        if (token !== this.pageToken) {
          return;
        }
        this.bankSaving.set(false);
        console.error('labeling bank save failed', error);
        const status = (error as { status?: number })?.status;
        const body = (error as { error?: Partial<LabelingBankConflict> | Partial<LabelingCropConflict> })?.error;
        if (status === 409 && body?.errorCode === 'LabelingCropAlreadyInBank') {
          // Aynı kırpma bankada (issue #577): mevcut kayıtlar listelenir, onaylanırsa force ile yeniden gönderilir.
          this.confirmCropDuplicate((body as Partial<LabelingCropConflict>).existing ?? [], image, questions);
          return;
        }
        if (status === 409 && body?.errorCode === 'LabelingAnswerKeyPage') {
          // Sayfa cevap anahtarı aralığında (issue #577): onaylanırsa force ile kaydedilir.
          this.confirmAnswerKeyPage(body.message || this.tr('bank.answerKey.message'), image, questions);
          return;
        }
        if (status === 409 && body?.existing) {
          // Kayıt bilgisi bu sayfada yoktu (ya da başka bir sekmede kaydedildi): bilgiyi göster, yeniden kaydı sor.
          const existing = body.existing as LabelingBankInfo;
          this.setBankRecord(null, existing);
          this.confirmBankAgain(existing, image, questions);
          return;
        }
        // 403: işaretleme rolü (QuestionTrainer) var ama soru yazma rolü (Teacher/Admin) yok.
        const message = status === 403 ? this.tr('bank.forbidden') : body?.message || this.tr('bank.failed');
        this.snackBar.open(message, this.tr('ok'), { duration: 6000 });
      },
    });
  }

  private confirmAnswerKeyPage(message: string, image: string, questions: LabelingBankQuestion[]): void {
    const token = this.pageToken;
    const data: ConfirmDialogData = {
      title: this.tr('bank.answerKey.title'),
      message,
      confirmText: this.tr('bank.answerKey.force'),
      cancelText: this.tr('bank.answerKey.cancel'),
      icon: 'key',
      confirmColor: 'warn',
    };
    this.dialog
      .open(ConfirmDialogComponent, { data, autoFocus: false })
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((force) => {
        if (force === true && token === this.pageToken && this.canSaveToBank()) {
          this.submitBank(image, questions, true);
        }
      });
  }

  private confirmCropDuplicate(
    existing: LabelingCropConflict['existing'],
    image: string,
    questions: LabelingBankQuestion[]
  ): void {
    const token = this.pageToken;
    const data: LabelingCropDuplicateDialogData = { existing };
    this.dialog
      .open(LabelingCropDuplicateDialogComponent, { data, width: '560px', maxWidth: '95vw', autoFocus: false })
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((force) => {
        if (force === true && token === this.pageToken && this.canSaveToBank()) {
          this.submitBank(image, questions, true);
        }
      });
  }

  private setBankRecord(sha: string | null, bank: LabelingBankInfo): void {
    const page = this.pageRecord();
    this.pageRecord.set({
      imageSha256: page?.imageSha256 ?? sha ?? '',
      width: page?.width ?? 0,
      height: page?.height ?? 0,
      results: page?.results ?? [],
      bank,
    });
  }

  /** Tuvale aktarım `answerCount` effect'inde. */
  setAnswerCount(count: number): void {
    this.answerCount.set(count);
    try {
      localStorage.setItem(ANSWER_COUNT_STORAGE_KEY, String(count));
    } catch {
      // depolama kapalı: yalnız bu oturumda geçerli
    }
  }

  setDrawMode(mode: DrawMode): void {
    const selector = this.selectorRef();
    if (!selector) {
      return;
    }
    selector.lockSelectionMode(this.selectionMode() === mode ? null : mode);
  }

  repredict(): void {
    if (!this.current() || this.analysing() || this.sendingToFix()) {
      return;
    }
    this.selectorRef()?.predict();
  }

  align(): void {
    const selector = this.selectorRef();
    if (!selector || !this.current() || this.analysing() || this.sendingToFix()) {
      return;
    }
    // Tek seferlik: tuvaldeki şıkları yerinde hizalar (kalıcı autoAlign açılmaz, tespit yeniden çalışmaz).
    selector.alignAllAnswers();
  }

  // ---------------- Soru Müfettişi (sağ panel) ----------------
  // Soru tuvalindeki müfettişin eğitim verisini etkileyen kısmı: soru gezinme, şıklar, hizalama, soru silme; doğru
  // şık yalnız bankaya kayıtta kullanılır. Sınıflandırma (sunucuda otomatik) / pasaj burada yok.

  /** Seçili sorunun müfettiş verisi; seçim image-selector'da sinyal dışı tutulduğu için getter. */
  get inspector(): ActiveInspector | null {
    return this.current() ? (this.selectorRef()?.getActiveInspector() ?? null) : null;
  }

  get questionCount(): number {
    return this.selectorRef()?.regions().length ?? 0;
  }

  /** Gönderim / analiz sürerken tuval değişmesin. */
  get inspectorLocked(): boolean {
    return this.analysing() || this.sendingToFix();
  }

  toggleInspector(): void {
    this.showInspector.update((v) => !v);
  }

  insSelectPrevious(): void {
    this.selectorRef()?.selectPreviousQuestion();
  }

  insSelectNext(): void {
    this.selectorRef()?.selectNextQuestion();
  }

  /**
   * Şıkkı seçer: soru tuvalindeki gibi `isCorrect` ile işaretlenir, tuvalde yeşil görünür; tuvalde şıkka tıklamak da
   * aynı alanı yazar. Doğru cevap eğitim verisine gitmez; "Havuza kaydet" ile sorunun doğru cevabı olarak yazılır.
   */
  insSelectAnswer(answerIndex: number): void {
    const index = this.inspector?.index;
    if (index != null && !this.inspectorLocked) {
      this.selectorRef()?.setCorrectAnswer(index, answerIndex, 1);
    }
  }

  insRemoveAnswer(answerIndex: number): void {
    const index = this.inspector?.index;
    if (index != null && !this.inspectorLocked) {
      this.selectorRef()?.removeAnswer(index, answerIndex);
    }
  }

  insAlignAnswers(): void {
    const index = this.inspector?.index;
    if (index != null && !this.inspectorLocked) {
      this.selectorRef()?.alignAnswers(index, true);
    }
  }

  insDrawAnswer(): void {
    const index = this.inspector?.index;
    if (index != null && !this.inspectorLocked) {
      this.selectorRef()?.selectAnswerMode(index);
    }
  }

  /** Issue #578: yanlış blok türünü düzeltir (renk değişir; Soru'ya dönünce MCQ davranışı geri gelir). */
  insSetKind(kind: BlockKind): void {
    const index = this.inspector?.index;
    if (index != null && !this.inspectorLocked) {
      this.selectorRef()?.setBlockKind(index, kind);
    }
  }

  insSetComponent(component: ActivityComponent | null): void {
    const index = this.inspector?.index;
    if (index != null && !this.inspectorLocked) {
      this.selectorRef()?.setActivityComponent(index, component);
    }
  }

  /** Issue #579: C bileşeninde tip (sürükle-yerleştir / eşleştirme); değişince blok yeni tipin talimatıyla çıkarılır. */
  insSetInteraction(type: BlockInteractionType): void {
    const index = this.inspector?.index;
    if (index != null && !this.inspectorLocked) {
      this.selectorRef()?.setActivityInteraction(index, type);
    }
  }

  insRemoveQuestion(): void {
    const index = this.inspector?.index;
    if (index != null && !this.inspectorLocked) {
      this.selectorRef()?.removeQuestion(index);
    }
  }

  /** N: atla / sonraki (yazı alanındayken ya da kısayol tuşlarıyla birlikte değil). */
  onKeydown(event: KeyboardEvent): void {
    if (event.ctrlKey || event.metaKey || event.altKey || event.repeat) {
      return;
    }
    if (event.key !== 'n' && event.key !== 'N') {
      return;
    }
    const target = event.target as HTMLElement | null;
    if (target && (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName))) {
      return;
    }
    if (this.canSkip()) {
      event.preventDefault();
      this.skip();
    }
  }

  /** Sıradaki işaretlenmemiş sayfayı açar (önceden bulunmuş aday varsa onu kullanır). */
  private async showNext(): Promise<void> {
    const seq = this.pickSeq;
    this.resetPageState();
    this.picking.set(true);
    this.current.set(null);

    let candidate = await (this.prefetch ?? this.pick(seq));
    if (seq !== this.pickSeq) {
      return;
    }
    this.prefetch = null;
    // Aday beklerken aynı içerikli başka bir dosya eğitilmiş olabilir.
    while (candidate && this.labeled.has(candidate.pageId) && seq === this.pickSeq) {
      this.skippedLabeled.update((n) => n + 1);
      candidate = await this.pick(seq);
    }
    if (seq !== this.pickSeq) {
      return;
    }
    this.picking.set(false);
    this.current.set(candidate);
    if (!candidate) {
      this.exhausted.set(true);
      return;
    }
    this.shownCount.update((n) => n + 1);
    const selector = this.selectorRef();
    if (selector) {
      // Kilitli "şık çiz" modunda image-selector sayfayı tek tam-sayfa soru sayıp tespiti atlar; eğitim verisi
      // için her sayfa tespitle açılmalı.
      if (selector.selectionModeLocked() === 'answer') {
        selector.lockSelectionMode(null);
      }
      selector.loadFile(candidate.file);
      this.startPrefetch(seq);
    }
  }

  private startPrefetch(seq: number): void {
    if (this.pool.length === 0) {
      return;
    }
    this.prefetch = this.pick(seq);
  }

  private pick(seq: number): Promise<LabelingCandidate<File> | null> {
    return pickNextUnlabeled<File>({
      pool: this.pool,
      labeled: this.labeled,
      hash: (file) => this.hashFile(file),
      isCancelled: () => seq !== this.pickSeq,
      onSkipLabeled: () => {
        if (seq !== this.pickSeq) {
          return;
        }
        this.skippedLabeled.update((n) => n + 1);
      },
      onUnreadable: (file, error) => {
        if (seq !== this.pickSeq) {
          return;
        }
        console.error('labeling: file could not be read', file.name, error);
        this.unreadable.update((n) => n + 1);
      },
    });
  }

  private tr(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${LABELING_SCOPE}.${key}`, params) ?? '';
  }
}

function roundBox(box: LabelingBankBox): LabelingBankBox {
  return { x: Math.round(box.x), y: Math.round(box.y), width: Math.round(box.width), height: Math.round(box.height) };
}

function readStored(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function writeStored(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // depolama kapalı: yalnız bu oturumda geçerli
  }
}

function readStoredAnswerCount(): number {
  try {
    const stored = Number(localStorage.getItem(ANSWER_COUNT_STORAGE_KEY));
    return (ANSWER_COUNTS as readonly number[]).includes(stored) ? stored : 4;
  } catch {
    return 4;
  }
}
