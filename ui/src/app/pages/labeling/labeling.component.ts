import { Component, DestroyRef, computed, effect, inject, signal, untracked, viewChild, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe, DecimalPipe } from '@angular/common';
import { Observable, Subject, firstValueFrom, merge, takeUntil } from 'rxjs';
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
  InteractionDraftApply,
  TrainedPage,
} from '../image-selector/image-selector.component';
import {
  ACTIVITY_COMPONENTS,
  ActivityComponent,
  BLOCK_KINDS,
  BlockInteractionType,
  BlockKind,
  isBlockInteractionType,
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
  ANSWER_KEY_LIMITS,
  DetectionSource,
  LabelingAiModel,
  LabelingBankBox,
  LabelingBankConflict,
  LabelingBankInfo,
  LabelingActivityBlock,
  LabelingActivitySolutionResponse,
  LabelingActivityStructureResult,
  LabelingAnswerKeyEntry,
  LabelingAnswerKeyReadRequest,
  LabelingAnswerKeyReadResponse,
  LabelingBankQuestion,
  LabelingPage,
  LabelingPromptKind,
  LabelingResult,
  LabelingSolutionMode,
} from '../../models/labeling';
import { LabelingService, manualDetectionSource, sha256OfDataUrl } from '../../services/labeling.service';
import {
  LabelingPromptDialogComponent,
  LabelingPromptDialogResult,
  isActivityPromptSaved,
  isPromptUnchanged,
  isSolutionPromptKind,
} from './labeling-prompt-dialog/labeling-prompt-dialog.component';
import { AuthService } from '../../services/auth.service';
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

/** Issue #579: tek istekte en çok bu kadar blok (sunucu sınırı); fazlası ardışık isteklere bölünür. */
export const MAX_ACTIVITY_BLOCKS_PER_REQUEST = 20;
/** Bölge taşınınca / boyutlanınca yeniden istekten önce beklenen süre (sürükleme bitsin). */
export const STRUCTURE_BOX_DEBOUNCE_MS = 800;

/**
 * Issue #579: etkinlik bloğunun Claude taslağı (bölge adına göre). Tipi bölgenin güncel tipinden farklıysa (müfettişte
 * değişti) yeniden istenir; uygulanmış plan bölgenin o tipteki yazarlık durumundadır (#581 düzeltme ekranı bunu kullanır).
 */
export interface ActivityDraft {
  interactionType: BlockInteractionType;
  status: 'loading' | 'ok' | 'error';
  result: LabelingActivityStructureResult | null;
  /** Hata mesajı (sunucunun ya da yerel). */
  error: string | null;
  /**
   * Plan tuvale ne oldu: `applied` yüklendi, `kept` öğretmenin elle yazarlığı korundu ("Taslağı uygula" bekliyor),
   * `invalid` plan boş / bu tipe uymuyor; yanıt gelmeden null.
   */
  apply: InteractionDraftApply | null;
  /** İstenen blok kutusu (sayfa pikseli, yuvarlanmış); bölge taşınınca / boyutlanınca taslak bayatlar. */
  box: LabelingBankBox;
  /** Kutu değişti, yeniden istek bekleniyor (boyutlandırma bitince). */
  stale: boolean;
  /** Son isteğin numarası; daha eski isteğin yanıtı yok sayılır. */
  requestId: number;
}

/**
 * Issue #580: etkinlik bloğunun cevap anahtarı araması (açık sayfanın basılı sayfası + bloğun etkinlik etiketi). Etiket /
 * sayfa değişince yeniden aranır; eski isteğin yanıtı `requestId` ile ayıklanır.
 */
export interface AnswerKeyLookup {
  /** Sorgunun izi (kitap, sayfa, bölüm, etiket); aynı sorgu yeniden sorulmaz. */
  query: string;
  status: 'loading' | 'ok' | 'error';
  entries: LabelingAnswerKeyEntry[];
  requestId: number;
}

/**
 * Issue #580: etkinlik bloğunun taslak çözümü (bölge adına göre). Yazıldığı yapı taslağına (`draftRequestId`) bağlıdır:
 * yapı yeniden çıkarılınca / tip değişince geçersiz sayılır (id'ler değişmiş olabilir).
 */
export interface ActivitySolutionDraft {
  interactionType: BlockInteractionType;
  draftRequestId: number;
  mode: LabelingSolutionMode;
  /** `planChanged`: kayıtlı çözüm var ama başka bir plana yazılmış (yeniden çözülmeli; durum / onay gösterilmez). */
  status: 'loading' | 'ok' | 'error' | 'planChanged';
  response: LabelingActivitySolutionResponse | null;
  /** Çözümün yazıldığı planın kanonik JSON'u; tuvaldeki plan değişince çözüm bayatlar. */
  planJson: string | null;
  error: string | null;
  /** Çözüm tuvale ne oldu (`applyInteractionSolution`); yanıt gelmeden ya da çözüm yokken null. */
  apply: InteractionDraftApply | null;
  /** Uygulandığı / kaydedildiği andaki tuval çözümü (JSON); elle düzeltme bununla karşılaştırılır. */
  appliedJson: string | null;
  busy: 'saving' | 'approving' | null;
  requestId: number;
}

/** Issue #580: toplu cevap anahtarı yüklemesinin özeti. */
export interface AnswerKeyBulkSummary {
  total: number;
  /** Okunan sayfa. */
  pages: number;
  rows: number;
  /** Yalnız canlı (ücretli) okumaların tahmini maliyeti. */
  costUsd: number;
  /** Kayıtlı satırları başka talimat / modelle okunmuş sayfalar. */
  stale: number;
  /** Kota / bütçe / anahtar yok yüzünden okunmayan kalan dosyalar. */
  skipped: number;
  failed: Array<{ name: string; message: string }>;
}

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
  private readonly auth = inject(AuthService);

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

  // --- Issue #579: etkinlik yapısı (kırpma → taslak etkileşim planı) ---
  /** C bileşeninin (sürükle-yerleştir ailesi) tip seçenekleri. */
  readonly componentCTypes: readonly BlockInteractionType[] = [DRAG_PLACE, MATCH];
  /** Bölge adı → taslak. Yeni tespitte / sayfada sıfırlanır. */
  readonly activityDrafts = signal<Record<string, ActivityDraft>>({});
  /** Taslakların ait olduğu tespit (`ImageSelectorComponent.detectionRevision`). */
  private draftRevision: number | null = null;
  private draftRequestSeq = 0;
  /** Yeni tespit uygulandı: süren yapı istekleri iptal edilir. */
  private readonly draftsReset = new Subject<void>();
  /** Kutu değişikliğinden sonraki yeniden istek zamanlayıcısı (testlerde 0). */
  structureDebounceMs = STRUCTURE_BOX_DEBOUNCE_MS;
  private boxRefreshTimer: ReturnType<typeof setTimeout> | null = null;
  /** Yapı yalnız Claude kaynağında (ya da Claude'dan başlayan manuel kayıtta) ve izinli model varken istenir. */
  readonly structureEnabled = computed(() => this.showKindLegend() && this.aiModels().length > 0);

  // --- Issue #580: cevap anahtarı okuma ve etkinlik çözümü ---
  /** Onay yalnız Admin + QuestionTrainer realm rolüyle (sunucu da 403 verir); diğerlerinde düğme gizli. */
  readonly canApproveSolutions = this.auth.hasRealmRole('Admin') || this.auth.hasRealmRole('QuestionTrainer');
  /** Açık sayfa seçili kitabın cevap anahtarı aralığında. */
  readonly isAnswerKeyPage = computed(
    () => this.bookId() != null && this.filePage() != null && this.bookPage()?.isAnswerKey === true
  );
  readonly answerKeyEntries = signal<LabelingAnswerKeyEntry[]>([]);
  readonly answerKeyReading = signal(false);
  readonly answerKeyError = signal<string | null>(null);
  /** Son okumanın kaynağı / prompt sürümü / maliyeti (okunmadıysa null). */
  readonly answerKeyMeta = signal<Omit<LabelingAnswerKeyReadResponse, 'entries'> | null>(null);
  readonly canReadAnswerKey = computed(
    () =>
      this.isAnswerKeyPage() &&
      this.aiModels().length > 0 &&
      !!this.selectorRef()?.imageData() &&
      !this.answerKeyReading() &&
      !this.analysing() &&
      !this.sendingToFix()
  );
  /** Düzenlenen anahtar satırı ve formu (metin alanları; basılı sayfa sayı metni). */
  readonly editingEntryId = signal<number | null>(null);
  readonly entryForm = signal({ printedPage: '', activityLabel: '', itemLabel: '', answerText: '' });
  /** Kaydı / silinmesi süren satır. */
  readonly entryBusyId = signal<number | null>(null);
  readonly answerKeyLimits = ANSWER_KEY_LIMITS;
  private answerKeySeq = 0;
  /** Toplu yükleme ilerlemesi ("3/8 okunuyor") ve son özet. */
  readonly bulkKeyProgress = signal<{ done: number; total: number; current: string } | null>(null);
  readonly bulkKeySummary = signal<AnswerKeyBulkSummary | null>(null);
  private bulkKeySeq = 0;
  /** Toplu yükleme: kitap seçili, Claude modeli var, başka yükleme sürmüyor. */
  readonly canBulkReadKeys = computed(
    () => this.bookId() != null && this.aiModels().length > 0 && this.bulkKeyProgress() == null
  );
  /** Bölge adı → anahtar araması. */
  readonly keyLookups = signal<Record<string, AnswerKeyLookup>>({});
  private keyLookupSeq = 0;
  private readonly keyLookupTimers = new Map<string, ReturnType<typeof setTimeout>>();
  /** Etiket yazılırken anahtar aramasından önce beklenen süre (testlerde 0). */
  keyLookupDebounceMs = 400;
  /** Belirsiz anahtarda öğretmenin seçtiği grup (bölge adı → grup). */
  readonly keyChoices = signal<Record<string, string>>({});
  /** Anahtardan ön seçilen şık (bölge adı → satır + şık sırası). */
  readonly mcqKeyApplied = signal<Record<string, { entryId: number; answerIndex: number }>>({});
  /** Otomatik "Anahtardan çöz" bir kez (bölge + yapı taslağı). */
  private readonly autoSolved = new Set<string>();
  /** Kayıtlı çözümü aranan bloklar (bitene kadar otomatik çözüm beklenir). */
  private readonly restoringSolutions = new Set<string>();
  /** Etiketlerin tespitten doldurulduğu son tespit. */
  private labelsSeededRevision: number | null = null;
  /** Bölge adı → taslak çözüm. */
  readonly activitySolutions = signal<Record<string, ActivitySolutionDraft>>({});
  private solutionSeq = 0;
  /** Tuvalde cevap anahtarı kutusu çizili mi (gereksiz yeniden çizim olmasın). */
  private answerKeyOverlaysShown = false;

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.pickSeq++;
      this.bulkKeySeq++;
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
    // Issue #579: tipli etkinlik blokları için taslak yapı istenir (sayfa açılışında tek toplu istek, tip değişince yalnız
    // o blok). Bölgeler (tür / tip değişikliği `regions` sinyalini yeniler), tespit ve kaynak izlenir.
    effect(() => {
      const selector = this.selectorRef();
      if (!selector) {
        return;
      }
      selector.regions();
      const revision = selector.detectionRevision();
      const enabled = this.structureEnabled();
      const busy = this.analysing();
      untracked(() => {
        this.syncActivityDrafts(revision, enabled && !busy);
        this.syncKeyLookups(revision);
      });
    });
    // Issue #580: cevap anahtarındaki görsel cevapların kutuları sayfada çizilir.
    effect(() => {
      const selector = this.selectorRef();
      const entries = this.answerKeyEntries();
      untracked(() => this.drawAnswerKeyOverlays(selector, entries));
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
    // Sayfa / kitap değişti: anahtar okuması ve etkinliklerin anahtar aramaları bu sayfaya ait değil.
    this.clearAnswerKey();
    this.clearKeyLookups();
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
          this.refreshKeyLookups();
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
    // Issue #580: etkinlik etiketi cevap anahtarı satırlarını bulur (madde etiketi yalnız istemcide süzer).
    const kind = this.selectorRef()?.getBlockKind(name);
    if (kind === 'activity' || kind === 'mcq_question') {
      this.mcqKeyApplied.update((all) => omit(all, name));
      this.scheduleKeyLookup(name);
    }
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
          this.onActivityPromptSaved(result.interactionType, result.prompt.version, token);
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
   * Issue #579: bir tipin talimatı kaydedildi. Bu sayfada o tipte taslak varsa yeni talimatla yeniden çıkarmak önerilir
   * (yeni prompt hash'i → canlı, ücretli çağrı); diğer tiplerin taslakları etkilenmez.
   */
  private onActivityPromptSaved(kind: LabelingPromptKind, version: number, token: number): void {
    if (isSolutionPromptKind(kind)) {
      // Issue #580: yalnız o talimatın önbelleği geçersiz olur; sonraki okuma / çözüm yeni sürümle (ücretli) üretilir.
      const name = this.tr(`solution.promptKinds.${kind}`);
      this.snackBar.open(this.tr('solution.promptSaved', { kind: name, version }), this.tr('ok'), { duration: 5000 });
      return;
    }
    const type = kind;
    const typeName = this.tr(`structure.types.${type}`);
    const message = this.tr('structure.promptSaved', { type: typeName, version });
    const keys = () => this.typedActivityBlocks().filter((b) => b.interactionType === type);
    if (token !== this.pageToken || !this.structureEnabled() || keys().length === 0) {
      this.snackBar.open(message, this.tr('ok'), { duration: 4000 });
      return;
    }
    this.snackBar
      .open(message, this.tr('structure.promptAskPage'), { duration: 8000 })
      .onAction()
      .subscribe(() => {
        const blocks = keys();
        if (token === this.pageToken && this.structureEnabled() && !this.inspectorLocked && blocks.length > 0) {
          this.requestStructures(blocks, false);
        }
      });
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
    this.activityDrafts.set({});
    this.draftRevision = null;
    this.clearBoxRefresh();
    this.clearAnswerKey();
    this.clearKeyLookups();
    this.activitySolutions.set({});
    this.mcqKeyApplied.set({});
    this.autoSolved.clear();
    this.restoringSolutions.clear();
    this.labelsSeededRevision = null;
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

  /** Müfettişteki bloğun taslağı (yoksa null). */
  draftOf(name: string): ActivityDraft | null {
    return this.activityDrafts()[name] ?? null;
  }

  /** Hata sonrası "Tekrar dene": önbellek kullanılır (force=false, kayıtlı sonuç varsa ücretsiz). */
  retryStructure(name: string): void {
    const block = this.typedActivityBlocks().find((b) => b.key === name);
    if (block && this.structureEnabled() && !this.inspectorLocked) {
      this.requestStructures([block], false);
    }
  }

  /** "Yapıyı yeniden çıkar": kayıtlı sonuç yok sayılır, Claude'a yeniden sorulur (ücretli) — önce onay. */
  reExtractStructure(name: string): void {
    if (!this.structureEnabled() || this.inspectorLocked || !this.typedActivityBlocks().some((b) => b.key === name)) {
      return;
    }
    const token = this.pageToken;
    const revision = this.draftRevision;
    this.snackBar
      .open(this.tr('structure.reExtractConfirm'), this.tr('structure.reExtractAction'), { duration: 8000 })
      .onAction()
      .subscribe(() => {
        // Onay başka bir sayfada / yeni tespitte gelirse ücretli çağrı yapılmaz.
        const block = this.typedActivityBlocks().find((b) => b.key === name);
        if (
          token === this.pageToken &&
          revision === this.draftRevision &&
          block &&
          this.structureEnabled() &&
          !this.inspectorLocked
        ) {
          this.requestStructures([block], true);
        }
      });
  }

  /** Tuvaldeki tipli etkinlik blokları (bölge adı + sayfa pikseli kutu + tip). */
  private typedActivityBlocks(): LabelingActivityBlock[] {
    const selector = this.selectorRef();
    if (!selector) {
      return [];
    }
    return selector.regions().flatMap((region) => {
      const type = selector.getInteractionType(region.name);
      if (selector.getBlockKind(region.name) !== 'activity' || !isBlockInteractionType(type)) {
        return [];
      }
      return [{ key: region.name, ...roundBox(region), interactionType: type }];
    });
  }

  /**
   * Taslakları tuvalle eşitler: yeni tespitte hepsi silinir (adlar yeniden "Soru 1"den başlar); tipi kalkan / bölgesi
   * silinen bloğun taslağı düşer — kaynak Claude olmasa da, aynı adı alan yeni bölge eskisinin taslağını devralmasın (tip
   * geri gelince yeniden istenir, önbellekten ücretsiz); taslağı olmayan ya da tipi değişen bloklar tek istekte sorulur.
   * Kutusu değişen bloğun taslağı bayatlar ve sürükleme bitince (`structureDebounceMs`) yeniden istenir. Hatalı taslak
   * kendiliğinden yeniden istenmez.
   */
  private syncActivityDrafts(revision: number, enabled: boolean): void {
    if (revision !== this.draftRevision) {
      this.draftRevision = revision;
      this.draftsReset.next();
      this.clearBoxRefresh();
      if (Object.keys(this.activityDrafts()).length > 0) {
        this.activityDrafts.set({});
      }
      if (Object.keys(this.activitySolutions()).length > 0) {
        this.activitySolutions.set({});
      }
      this.restoringSolutions.clear();
    }
    const blocks = this.typedActivityBlocks();
    const drafts = this.activityDrafts();
    const keys = new Set(blocks.map((b) => b.key));
    const next = { ...drafts };
    let changed = false;
    for (const key of Object.keys(drafts)) {
      if (!keys.has(key)) {
        delete next[key];
        changed = true;
      }
    }
    if (!enabled || !this.pageImage) {
      if (changed) {
        this.activityDrafts.set(next);
      }
      return;
    }
    const missing: LabelingActivityBlock[] = [];
    let moved = false;
    for (const block of blocks) {
      const draft = next[block.key];
      if (!draft || draft.interactionType !== block.interactionType) {
        missing.push(block);
      } else if (!sameBox(draft.box, block)) {
        moved = true;
        if (!draft.stale) {
          next[block.key] = { ...draft, stale: true };
          changed = true;
        }
      }
    }
    if (changed) {
      this.activityDrafts.set(next);
    }
    if (moved) {
      this.scheduleBoxRefresh();
    }
    if (missing.length > 0) {
      this.requestStructures(missing, false);
    }
  }

  /** Son kutu değişikliğinden `structureDebounceMs` sonra kutusu hâlâ farklı olan bloklar yeniden istenir. */
  private scheduleBoxRefresh(): void {
    this.clearBoxRefresh();
    const token = this.pageToken;
    const revision = this.draftRevision;
    this.boxRefreshTimer = setTimeout(() => {
      this.boxRefreshTimer = null;
      if (token !== this.pageToken || revision !== this.draftRevision || !this.structureEnabled() || this.analysing()) {
        return;
      }
      const drafts = this.activityDrafts();
      const moved = this.typedActivityBlocks().filter((b) => {
        const draft = drafts[b.key];
        return !!draft && draft.interactionType === b.interactionType && !sameBox(draft.box, b);
      });
      // Aynı kırpma (kutu geri alındı) önbellekten, yeni kırpma canlı çıkarılır; force yok.
      this.requestStructures(moved, false);
    }, this.structureDebounceMs);
  }

  private clearBoxRefresh(): void {
    if (this.boxRefreshTimer != null) {
      clearTimeout(this.boxRefreshTimer);
      this.boxRefreshTimer = null;
    }
  }

  /** Sayfanın Claude modeli: tespiti yapan (izinliyse), yoksa seçili model. */
  private structureModel(): string {
    const source = this.detectionSource();
    const name = source?.kind === 'Manual' ? source.base?.modelName : source?.modelName;
    return name && this.aiModels().some((m) => m.id === name) ? name : this.aiModelId();
  }

  /**
   * Bloklar en çok 20'lik isteklerle ve SIRAYLA sorulur (sunucu her istekte bütçeyi önceden ayırır; paralel istekler
   * birbirinin bütçesini tüketirdi). Tümü baştan "yükleniyor" olur; bir istek başarısızsa kalanlar gönderilmez, aynı
   * hatayla işaretlenir.
   */
  private requestStructures(blocks: LabelingActivityBlock[], force: boolean): void {
    const image = this.pageImage;
    const model = this.structureModel();
    if (!image || !model || blocks.length === 0) {
      return;
    }
    const batches: Array<{ blocks: LabelingActivityBlock[]; requestId: number }> = [];
    for (let i = 0; i < blocks.length; i += MAX_ACTIVITY_BLOCKS_PER_REQUEST) {
      batches.push({ blocks: blocks.slice(i, i + MAX_ACTIVITY_BLOCKS_PER_REQUEST), requestId: ++this.draftRequestSeq });
    }
    this.activityDrafts.update((all) => {
      const next = { ...all };
      for (const batch of batches) {
        for (const block of batch.blocks) {
          next[block.key] = {
            interactionType: block.interactionType,
            status: 'loading',
            result: null,
            error: null,
            apply: null,
            box: { x: block.x, y: block.y, width: block.width, height: block.height },
            stale: false,
            requestId: batch.requestId,
          };
        }
      }
      return next;
    });
    const token = this.pageToken;
    const revision = this.draftRevision;
    const current = () => token === this.pageToken && revision === this.draftRevision;
    const run = (index: number): void => {
      const batch = batches[index];
      if (!batch || !current()) {
        return;
      }
      this.requestStructureBatch(image, model, batch.blocks, batch.requestId, force, current, (error) => {
        if (error === null) {
          run(index + 1);
          return;
        }
        // Kalan istekler gönderilmez (ör. 429 bütçe): aynı mesajla işaretlenir.
        const rest = batches.slice(index + 1);
        if (rest.length > 0) {
          this.failDrafts(rest.flatMap((b) => b.blocks.map((block) => ({ key: block.key, requestId: b.requestId }))), error);
        }
      });
    };
    run(0);
  }

  private requestStructureBatch(
    image: string,
    model: string,
    blocks: LabelingActivityBlock[],
    requestId: number,
    force: boolean,
    current: () => boolean,
    done: (error: string | null) => void
  ): void {
    this.labelingApi
      .activityStructure({ imageBase64: image, model, force, blocks })
      .pipe(takeUntil(merge(this.pageChanged, this.draftsReset)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          if (!current()) {
            return;
          }
          const results = new Map((response?.results ?? []).map((r) => [r.key, r]));
          const selector = this.selectorRef();
          const next = { ...this.activityDrafts() };
          for (const block of blocks) {
            const draft = next[block.key];
            if (!draft || draft.requestId !== requestId) {
              continue;
            }
            const result = results.get(block.key);
            if (!result) {
              next[block.key] = { ...draft, status: 'error', error: this.tr('structure.missingResult') };
            } else if (result.status !== 'ok') {
              next[block.key] = { ...draft, status: 'error', result, error: result.error || this.tr('structure.failed') };
            } else {
              const apply: InteractionDraftApply =
                result.interactionType === block.interactionType && !!selector
                  ? selector.applyInteractionDraft(block.key, block.interactionType, result.plan)
                  : 'invalid';
              next[block.key] = { ...draft, status: 'ok', result, error: null, apply };
            }
          }
          this.activityDrafts.set(next);
          // Issue #580: bu kırpmanın kayıtlı çözüm taslağı varsa (sayfa yeniden açıldı) geri yüklenir.
          for (const block of blocks) {
            if (next[block.key]?.requestId === requestId) {
              this.restoreSolution(block.key);
              this.tryAutoSolve(block.key);
            }
          }
          done(null);
        },
        error: (error) => {
          if (!current()) {
            return;
          }
          console.error('labeling activity-structure failed', error);
          const status = (error as { status?: number })?.status;
          const serverMessage = (error as { error?: { message?: string } })?.error?.message;
          const message =
            status === 503
              ? serverMessage || this.tr('structure.unavailable')
              : serverMessage || this.tr('structure.failed');
          this.failDrafts(blocks.map((b) => ({ key: b.key, requestId })), message);
          // 429 (saatlik tavan / günlük bütçe) ve diğer hatalar: sunucunun mesajı gösterilir.
          this.snackBar.open(message, this.tr('ok'), { duration: 6000 });
          done(message);
        },
      });
  }

  private failDrafts(items: Array<{ key: string; requestId: number }>, message: string): void {
    const next = { ...this.activityDrafts() };
    for (const { key, requestId } of items) {
      const draft = next[key];
      if (draft?.requestId === requestId) {
        next[key] = { ...draft, status: 'error', error: message };
      }
    }
    this.activityDrafts.set(next);
  }

  /**
   * "Taslağı uygula": elle yazarlık korunduğu için bekleyen taslağı tuvale yükler — öğretmenin el düzenlemeleri kaybolur,
   * önce onay.
   */
  applyDraft(name: string): void {
    const draft = this.draftOf(name);
    if (!draft || draft.status !== 'ok' || draft.apply !== 'kept' || this.inspectorLocked) {
      return;
    }
    const token = this.pageToken;
    this.snackBar
      .open(this.tr('structure.applyConfirm'), this.tr('structure.applyAction'), { duration: 8000 })
      .onAction()
      .subscribe(() => {
        const latest = this.draftOf(name);
        const selector = this.selectorRef();
        if (token !== this.pageToken || latest?.requestId !== draft.requestId || !selector) {
          return;
        }
        const apply = selector.applyInteractionDraft(name, draft.interactionType, draft.result?.plan, { replace: true });
        this.activityDrafts.update((all) => (all[name]?.requestId === draft.requestId ? { ...all, [name]: { ...all[name], apply } } : all));
      });
  }

  // ---------------- Issue #580: cevap anahtarı sayfası ----------------

  /**
   * "Cevap anahtarını oku (Claude)": sayfa kitabın anahtar aralığındaysa satırlar okunur (kayıtlı sonuç ücretsiz döner).
   * `force` ("Yeniden oku") kayıtlı sonucu yok sayar ve ücretlidir: önce onay.
   */
  readAnswerKey(force = false): void {
    if (!this.canReadAnswerKey()) {
      return;
    }
    if (!force) {
      this.runAnswerKeyRead(false);
      return;
    }
    const token = this.pageToken;
    this.snackBar
      .open(this.tr('answerKey.rereadConfirm'), this.tr('answerKey.rereadAction'), { duration: 8000 })
      .onAction()
      .subscribe(() => {
        if (token === this.pageToken && this.canReadAnswerKey()) {
          this.runAnswerKeyRead(true);
        }
      });
  }

  private runAnswerKeyRead(force: boolean, confirmOverwrite = false): void {
    const bookId = this.bookId();
    const filePage = this.filePage();
    const image = this.pageImage;
    const model = this.structureModel();
    if (bookId == null || filePage == null || !image || !model) {
      return;
    }
    const seq = ++this.answerKeySeq;
    this.answerKeyReading.set(true);
    this.answerKeyError.set(null);
    this.editingEntryId.set(null);
    this.labelingApi
      .readAnswerKey({ bookId, filePage, imageBase64: image, model, force, ...(confirmOverwrite ? { confirmOverwrite } : {}) })
      .pipe(takeUntil(this.pageChanged), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          if (seq !== this.answerKeySeq) {
            return;
          }
          this.answerKeyReading.set(false);
          this.answerKeyEntries.set(response?.entries ?? []);
          this.answerKeyMeta.set({
            origin: response.origin,
            promptVersion: response.promptVersion,
            estimatedCostUsd: response.estimatedCostUsd,
            stale: response.stale === true,
          });
          if (response.origin === 'live' && response.estimatedCostUsd != null) {
            this.snackBar.open(
              this.tr('source.liveDone', { cost: response.estimatedCostUsd.toFixed(4) }),
              this.tr('ok'),
              { duration: 4000 }
            );
          }
        },
        error: (error) => {
          if (seq !== this.answerKeySeq) {
            return;
          }
          console.error('labeling answer-key read failed', error);
          this.answerKeyReading.set(false);
          const status = (error as { status?: number })?.status;
          const body = (error as { error?: { errorCode?: string; message?: string } })?.error;
          if (status === 409 && body?.errorCode === 'LabelingAnswerKeyHasEdits' && !confirmOverwrite) {
            // Elle düzeltilmiş satırlar yeniden okumada silinir: önce onay.
            this.confirmAnswerKeyOverwrite(body.message || this.tr('answerKey.overwrite.message'));
            return;
          }
          const message = this.serverError(error, 'answerKey.failed', 'answerKey.unavailable');
          this.answerKeyError.set(message);
          this.snackBar.open(message, this.tr('ok'), { duration: 6000 });
        },
      });
  }

  private confirmAnswerKeyOverwrite(message: string): void {
    const token = this.pageToken;
    const data: ConfirmDialogData = {
      title: this.tr('answerKey.overwrite.title'),
      message,
      confirmText: this.tr('answerKey.overwrite.confirm'),
      cancelText: this.tr('answerKey.overwrite.cancel'),
      icon: 'warning',
      confirmColor: 'warn',
    };
    this.dialog
      .open(ConfirmDialogComponent, { data, autoFocus: false })
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((confirmed) => {
        if (confirmed === true && token === this.pageToken && this.canReadAnswerKey()) {
          this.runAnswerKeyRead(true, true);
        }
      });
  }

  // ---------------- Issue #580: cevap anahtarlarını toplu yükleme ----------------

  /**
   * "Cevap anahtarlarını yükle": seçilen görseller seçili kitap için sırayla okunur (kayıtlı okuma ücretsiz döner). Dosya
   * adından sayfa numarası okunabiliyorsa (`page_201.webp`) `filePage` gider, yoksa sunucu bulur. Elle düzeltilmiş satırlı
   * sayfada (409) tek okumadaki gibi onay istenir; 429 / 503'te (kota, bütçe, anahtar yok) kalanlar okunmaz. Bitince özet
   * gösterilir ve açık sayfanın blokları yeniden aranır.
   */
  async onAnswerKeyFiles(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const files = sortLabelingFiles(Array.from(input.files ?? []).filter(isLabelingImage));
    input.value = '';
    const bookId = this.bookId();
    const model = this.aiModelId();
    if (files.length === 0 || !this.canBulkReadKeys() || bookId == null || !model) {
      return;
    }
    const seq = ++this.bulkKeySeq;
    const summary: AnswerKeyBulkSummary = { total: files.length, pages: 0, rows: 0, costUsd: 0, stale: 0, skipped: 0, failed: [] };
    this.bulkKeySummary.set(null);
    for (let i = 0; i < files.length; i++) {
      if (seq !== this.bulkKeySeq) {
        return;
      }
      const file = files[i];
      const name = file.webkitRelativePath || file.name;
      this.bulkKeyProgress.set({ done: i, total: files.length, current: name });
      let outcome: 'ok' | 'stop' | 'next';
      try {
        const imageBase64 = await readDataUrl(file);
        const filePage = filePageOf(name);
        outcome = await this.bulkReadOne(seq, summary, name, {
          bookId,
          imageBase64,
          model,
          force: false,
          ...(filePage != null ? { filePage } : {}),
        });
      } catch (error) {
        console.error('labeling answer-key file read failed', name, error);
        summary.failed.push({ name, message: this.tr('answerKey.bulk.unreadable') });
        outcome = 'next';
      }
      if (outcome === 'stop') {
        summary.skipped = files.length - i - 1;
        break;
      }
    }
    if (seq !== this.bulkKeySeq) {
      return;
    }
    this.bulkKeyProgress.set(null);
    this.bulkKeySummary.set(summary);
    // Yeni satırlar açık sayfanın bloklarını bulabilir.
    const names = this.selectorRef()?.regions().map((r) => r.name) ?? [];
    if (this.bookId() === bookId && this.bookPage() && this.showKindLegend() && names.length > 0) {
      this.lookupKeys(names, true);
    }
  }

  private async bulkReadOne(
    seq: number,
    summary: AnswerKeyBulkSummary,
    name: string,
    request: LabelingAnswerKeyReadRequest
  ): Promise<'ok' | 'stop' | 'next'> {
    try {
      const response = await firstValueFrom(this.labelingApi.readAnswerKey(request).pipe(takeUntilDestroyed(this.destroyRef)));
      summary.pages++;
      summary.rows += response?.entries?.length ?? 0;
      if (response?.origin === 'live') {
        summary.costUsd += response.estimatedCostUsd ?? 0;
      }
      if (response?.stale) {
        summary.stale++;
      }
      return 'ok';
    } catch (error) {
      if (seq !== this.bulkKeySeq) {
        return 'stop';
      }
      console.error('labeling answer-key bulk read failed', name, error);
      const status = (error as { status?: number })?.status;
      const body = (error as { error?: { errorCode?: string; message?: string } })?.error;
      if (status === 409 && body?.errorCode === 'LabelingAnswerKeyHasEdits' && !request.confirmOverwrite) {
        const confirmed = await firstValueFrom(
          this.dialog
            .open(ConfirmDialogComponent, {
              data: {
                title: this.tr('answerKey.overwrite.title'),
                message: `${name}: ${body.message || this.tr('answerKey.overwrite.message')}`,
                confirmText: this.tr('answerKey.overwrite.confirm'),
                cancelText: this.tr('answerKey.overwrite.cancel'),
                icon: 'warning',
                confirmColor: 'warn',
              } satisfies ConfirmDialogData,
              autoFocus: false,
            })
            .afterClosed()
        );
        if (confirmed === true && seq === this.bulkKeySeq) {
          return this.bulkReadOne(seq, summary, name, { ...request, force: true, confirmOverwrite: true });
        }
        summary.failed.push({ name, message: this.tr('answerKey.bulk.keptEdits') });
        return 'next';
      }
      const message = this.serverError(error, 'answerKey.failed', 'answerKey.unavailable');
      summary.failed.push({ name, message });
      return status === 429 || status === 503 ? 'stop' : 'next';
    }
  }

  dismissBulkSummary(): void {
    this.bulkKeySummary.set(null);
  }

  /** Okuma sonucu ve düzenleme durumu silinir; süren okuma yanıtı yok sayılır. */
  private clearAnswerKey(): void {
    this.answerKeySeq++;
    this.answerKeyEntries.set([]);
    this.answerKeyMeta.set(null);
    this.answerKeyError.set(null);
    this.answerKeyReading.set(false);
    this.editingEntryId.set(null);
    this.entryBusyId.set(null);
  }

  private drawAnswerKeyOverlays(selector: ImageSelectorComponent | undefined, entries: LabelingAnswerKeyEntry[]): void {
    if (!selector) {
      return;
    }
    const boxes = entries
      .filter((e) => e.visual && !!e.box)
      .map((e) => ({ box: e.box!, tag: entryTag(e) }));
    if (boxes.length === 0 && !this.answerKeyOverlaysShown) {
      return;
    }
    this.answerKeyOverlaysShown = boxes.length > 0;
    selector.setAnswerKeyOverlays(boxes);
  }

  /** Satırın kısa etiketi ("s. 12 · Etkinlik 3 · b"). */
  entryLabel(entry: LabelingAnswerKeyEntry): string {
    return entryTag(entry);
  }

  editEntry(entry: LabelingAnswerKeyEntry): void {
    if (this.entryBusyId() != null) {
      return;
    }
    this.editingEntryId.set(entry.id);
    this.entryForm.set({
      printedPage: entry.printedPage == null ? '' : String(entry.printedPage),
      activityLabel: entry.activityLabel ?? '',
      itemLabel: entry.itemLabel ?? '',
      answerText: entry.answerText ?? '',
    });
  }

  setEntryField(field: 'printedPage' | 'activityLabel' | 'itemLabel' | 'answerText', target: EventTarget | null): void {
    const value = (target as HTMLInputElement | null)?.value ?? '';
    this.entryForm.update((form) => ({ ...form, [field]: value }));
  }

  cancelEntryEdit(): void {
    this.editingEntryId.set(null);
  }

  /** Boş metin null gider; basılı sayfa tam sayı değilse kaydedilmez. */
  saveEntry(entry: LabelingAnswerKeyEntry): void {
    if (this.editingEntryId() !== entry.id || this.entryBusyId() != null) {
      return;
    }
    const form = this.entryForm();
    const printedText = form.printedPage.trim();
    const printedPage = printedText === '' ? null : Number(printedText);
    if (printedPage != null && (!Number.isInteger(printedPage) || printedPage < 1 || printedPage > ANSWER_KEY_LIMITS.maxPrintedPage)) {
      this.snackBar.open(this.tr('answerKey.invalidPage'), this.tr('ok'), { duration: 4000 });
      return;
    }
    const text = (value: string, max: number) => value.trim().slice(0, max) || null;
    const seq = this.answerKeySeq;
    this.entryBusyId.set(entry.id);
    this.labelingApi
      .updateAnswerKeyEntry(entry.id, {
        printedPage,
        activityLabel: text(form.activityLabel, ANSWER_KEY_LIMITS.maxLabel),
        itemLabel: text(form.itemLabel, ANSWER_KEY_LIMITS.maxLabel),
        answerText: text(form.answerText, ANSWER_KEY_LIMITS.maxAnswer),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          if (seq !== this.answerKeySeq) {
            return;
          }
          this.entryBusyId.set(null);
          this.editingEntryId.set(null);
          this.answerKeyEntries.update((all) => all.map((e) => (e.id === entry.id ? { ...e, ...updated } : e)));
        },
        error: (error) => {
          if (seq !== this.answerKeySeq) {
            return;
          }
          console.error('labeling answer-key entry update failed', error);
          this.entryBusyId.set(null);
          this.snackBar.open(this.serverError(error, 'answerKey.saveFailed'), this.tr('ok'), { duration: 6000 });
        },
      });
  }

  deleteEntry(entry: LabelingAnswerKeyEntry): void {
    if (this.entryBusyId() != null) {
      return;
    }
    const seq = this.answerKeySeq;
    this.entryBusyId.set(entry.id);
    this.labelingApi
      .deleteAnswerKeyEntry(entry.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          if (seq !== this.answerKeySeq) {
            return;
          }
          this.entryBusyId.set(null);
          if (this.editingEntryId() === entry.id) {
            this.editingEntryId.set(null);
          }
          this.answerKeyEntries.update((all) => all.filter((e) => e.id !== entry.id));
        },
        error: (error) => {
          if (seq !== this.answerKeySeq) {
            return;
          }
          console.error('labeling answer-key entry delete failed', error);
          this.entryBusyId.set(null);
          this.snackBar.open(this.serverError(error, 'answerKey.deleteFailed'), this.tr('ok'), { duration: 6000 });
        },
      });
  }

  // ---------------- Issue #580: cevap anahtarında arama (soru + etkinlik) ----------------

  /** Bölgenin anahtar araması (yoksa null). */
  keyLookupOf(name: string): AnswerKeyLookup | null {
    return this.keyLookups()[name] ?? null;
  }

  /**
   * Etkinliğin anahtar satırlarının grupları (basılı sayfa + etkinlik etiketi). Birden fazla grup = belirsiz (ör. fasiküllü
   * kitapta aynı etiket iki kez): öğretmen seçer.
   */
  keyGroups(name: string): Array<{ id: string; label: string; entries: LabelingAnswerKeyEntry[] }> {
    const lookup = this.keyLookupOf(name);
    if (lookup?.status !== 'ok') {
      return [];
    }
    const groups = new Map<string, { id: string; label: string; entries: LabelingAnswerKeyEntry[] }>();
    for (const entry of lookup.entries) {
      const id = `${entry.printedPage ?? ''}|${normalizeLabel(entry.activityLabel)}`;
      const group = groups.get(id) ?? { id, label: entryTag({ ...entry, itemLabel: null }), entries: [] };
      group.entries.push(entry);
      groups.set(id, group);
    }
    return [...groups.values()];
  }

  /** Etkinlikte anahtar belirsiz ve henüz seçilmedi. */
  keyAmbiguous(name: string): boolean {
    return this.keyGroups(name).length > 1 && !this.keyGroups(name).some((g) => g.id === this.keyChoices()[name]);
  }

  /** Belirsiz anahtarda öğretmenin seçtiği grup; seçimden sonra (etkinlikte) anahtardan çözüm bir kez otomatik istenir. */
  chooseKeyGroup(name: string, groupId: string): void {
    this.keyChoices.update((all) => ({ ...all, [name]: groupId }));
    this.tryAutoSolve(name);
  }

  /**
   * Bloğun anahtar satırları: etiketle bulunanlar (belirsizse seçilen grup); madde etiketi girildiyse yalnız o maddeninkiler
   * (büyük / küçük harf, boşluk ve noktalama farkı yok sayılır). Soru bloklarında süzme yok (madde etiketi soru numarasıdır).
   */
  matchingKeyEntries(name: string): LabelingAnswerKeyEntry[] {
    const lookup = this.keyLookupOf(name);
    if (lookup?.status !== 'ok') {
      return [];
    }
    if (this.selectorRef()?.getBlockKind(name) !== 'activity') {
      return lookup.entries;
    }
    const groups = this.keyGroups(name);
    const choice = this.keyChoices()[name];
    const base = groups.length > 1 ? (groups.find((g) => g.id === choice)?.entries ?? []) : lookup.entries;
    const item = normalizeLabel(this.labelsOf(name).itemLabel);
    return item ? base.filter((e) => normalizeLabel(e.itemLabel) === item) : base;
  }

  /** Anahtar aramasının neden yapılamadığı (`null`: aranabilir). */
  keyLookupBlocker(name: string): 'noBook' | 'needLabel' | null {
    if (this.bookId() == null || this.filePage() == null) {
      return 'noBook';
    }
    return this.lookupItemOf(name) ? null : 'needLabel';
  }

  /**
   * Bloğun arama girdisi: etkinlikte etkinlik etiketi; soruda soru numarası (madde etiketi), bölüm = etkinlik etiketi alanı
   * (tespitin bölüm başlığıyla dolar). Etiketsiz / başka türde null.
   */
  private lookupItemOf(name: string): { label: string; section: string } | null {
    const kind = this.selectorRef()?.getBlockKind(name);
    const labels = this.labelsOf(name);
    if (kind === 'activity') {
      const label = labels.activityLabel.trim();
      return label ? { label, section: '' } : null;
    }
    if (kind === 'mcq_question') {
      const label = labels.itemLabel.trim();
      return label ? { label, section: labels.activityLabel.trim() } : null;
    }
    return null;
  }

  private lookupQuery(name: string): string | null {
    const item = this.lookupItemOf(name);
    if (!item) {
      return null;
    }
    return [this.bookId(), this.filePage(), this.bookPage()?.printedPage ?? '', item.section, item.label].join('|');
  }

  private scheduleKeyLookup(name: string): void {
    const timer = this.keyLookupTimers.get(name);
    if (timer != null) {
      clearTimeout(timer);
    }
    this.keyLookupTimers.set(
      name,
      setTimeout(() => {
        this.keyLookupTimers.delete(name);
        this.lookupKey(name);
      }, this.keyLookupDebounceMs)
    );
  }

  /** Tek bloğun anahtarı (etiket düzenlendi / tekrar dene); aynı sorgunun başarılı sonucu varsa `force` olmadan sorulmaz. */
  lookupKey(name: string, force = false): void {
    this.lookupKeys([name], force);
  }

  /**
   * Blokların anahtar satırları: bölüm etiketine göre gruplanıp grup başına tek istek (`answer-key/lookup`). Etiketi
   * olmayan bloğun araması silinir. Yanıt sayfa / tespit değişince yok sayılır; sonuçla soru şıkkı ön seçilir, etkinlik
   * anahtardan çözülür.
   */
  private lookupKeys(names: string[], force: boolean): void {
    const bookId = this.bookId();
    const filePage = this.filePage();
    const printedPage = this.bookPage()?.printedPage ?? null;
    if (bookId == null || filePage == null) {
      return;
    }
    const groups = new Map<string, Array<{ name: string; label: string; query: string }>>();
    for (const name of names) {
      const item = this.lookupItemOf(name);
      const query = this.lookupQuery(name);
      if (!item || !query) {
        if (this.keyLookups()[name]) {
          this.keyLookups.update((all) => omit(all, name));
        }
        continue;
      }
      const existing = this.keyLookups()[name];
      if (!force && existing && existing.status !== 'error' && existing.query === query) {
        continue;
      }
      const group = groups.get(item.section) ?? [];
      group.push({ name, label: item.label, query });
      groups.set(item.section, group);
    }
    for (const [section, items] of groups) {
      const requestId = ++this.keyLookupSeq;
      this.keyLookups.update((all) => {
        const next = { ...all };
        for (const item of items) {
          next[item.name] = { query: item.query, status: 'loading', entries: [], requestId };
        }
        return next;
      });
      this.keyChoices.update((all) => items.reduce((acc, item) => omit(acc, item.name), all));
      const settle = (results: Map<string, LabelingAnswerKeyEntry[]> | null) => {
        this.keyLookups.update((all) => {
          const next = { ...all };
          for (const item of items) {
            if (next[item.name]?.requestId === requestId) {
              next[item.name] = results
                ? { ...next[item.name], status: 'ok', entries: results.get(item.name) ?? [] }
                : { ...next[item.name], status: 'error' };
            }
          }
          return next;
        });
        if (results) {
          items.forEach((item) => this.afterKeyLookup(item.name, requestId));
        }
      };
      this.labelingApi
        .lookupAnswerKey(bookId, {
          filePage,
          ...(printedPage != null ? { printedPage } : {}),
          ...(section ? { sectionLabel: section } : {}),
          items: items.map((item) => ({ key: item.name, label: item.label })),
        })
        .pipe(takeUntil(merge(this.pageChanged, this.draftsReset)), takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (response) => settle(new Map((response?.results ?? []).map((r) => [r.key, r.entries ?? []]))),
          error: (error) => {
            console.error('labeling answer-key lookup failed', error);
            settle(null);
          },
        });
    }
  }

  /** Arama sonucu: soruda tek harfli cevap şıkkı ön seçer (öğretmen seçmediyse), etkinlikte anahtardan çözümü dener. */
  private afterKeyLookup(name: string, requestId: number): void {
    const lookup = this.keyLookupOf(name);
    if (lookup?.requestId !== requestId || lookup.status !== 'ok') {
      return;
    }
    const kind = this.selectorRef()?.getBlockKind(name);
    if (kind === 'mcq_question' && lookup.entries.length === 1) {
      this.applyMcqKey(name, lookup.entries[0], false);
    } else if (kind === 'activity') {
      this.tryAutoSolve(name);
    }
  }

  /**
   * Anahtardaki harfi (A→0 …) şık olarak işaretler. Otomatikte öğretmen bir şık seçtiyse dokunulmaz; belirsiz anahtardan
   * öğretmenin seçimi (`manual`) her zaman uygular.
   */
  applyMcqKey(name: string, entry: LabelingAnswerKeyEntry, manual = true): void {
    const selector = this.selectorRef();
    const index = selector?.regions().findIndex((r) => r.name === name) ?? -1;
    const region = index >= 0 ? selector!.regions()[index] : null;
    const answerIndex = optionIndexOf(entry.answerText);
    if (!selector || !region || answerIndex == null || answerIndex >= (region.answers?.length ?? 0) || this.inspectorLocked) {
      return;
    }
    if (!manual && region.answers.some((a) => a.isCorrect)) {
      return;
    }
    selector.setCorrectAnswer(index, answerIndex, 1);
    this.mcqKeyApplied.update((all) => ({ ...all, [name]: { entryId: entry.id, answerIndex } }));
  }

  /** Tespitin bloğa verdiği basılı etiket (müfettişte gösterilir; etiket alanı düzenlenebilir). */
  detectedLabelOf(name: string): string | null {
    return this.selectorRef()?.getBlockLabel(name) ?? null;
  }

  /** Soruda anahtardan seçilen şık hâlâ seçiliyse harfi ("B"), değilse null. */
  mcqKeyLetter(name: string): string | null {
    const applied = this.mcqKeyApplied()[name];
    const region = this.selectorRef()?.regions().find((r) => r.name === name);
    if (!applied || !region?.answers?.[applied.answerIndex]?.isCorrect) {
      return null;
    }
    return String.fromCharCode(65 + applied.answerIndex);
  }

  /** Anahtardaki cevap şık harfi mi (A–J). */
  isOptionAnswer(entry: LabelingAnswerKeyEntry): boolean {
    return optionIndexOf(entry.answerText) != null;
  }

  /**
   * Etkinlik anahtarda bulunduysa "Anahtardan çöz" bir kez otomatik istenir (sunucu önbelleği; maliyet gösterilir): taslak
   * tuvalde, çözüm yok, kayıtlı çözüm geri yükleme beklenmiyor, anahtar belirsiz değil. AI ile çözüm hiç otomatik değil.
   */
  private tryAutoSolve(name: string): void {
    const draft = this.draftOf(name);
    if (!draft || this.restoringSolutions.has(name) || this.activitySolutions()[name]) {
      return;
    }
    const key = `${name}\u0000${draft.requestId}`;
    if (this.autoSolved.has(key) || !this.canSolve(name) || this.keyAmbiguous(name) || this.matchingKeyEntries(name).length === 0) {
      return;
    }
    this.autoSolved.add(key);
    this.requestSolution(name, 'key', false);
  }

  /**
   * Tespitten sonra (Claude kaynağı ya da Claude'dan başlayan manuel kayıt): blok etiketleri boş etiket alanlarına yazılır
   * (etkinlik → etkinlik etiketi; soru → bölüm başlığı + soru numarası), kitap seçili ve sayfa bilgisi geldiyse etiketli
   * bütün bloklar tek istekte aranır. Tespit başına bir kez.
   */
  private syncKeyLookups(revision: number): void {
    const selector = this.selectorRef();
    if (!selector || !this.showKindLegend() || this.analysing()) {
      return;
    }
    if (this.labelsSeededRevision !== revision) {
      this.labelsSeededRevision = revision;
      this.autoSolved.clear();
      this.mcqKeyApplied.set({});
      this.keyChoices.set({});
      const section = selector.sectionLabel() ?? '';
      const seeded = { ...this.questionLabels() };
      let changed = false;
      for (const region of selector.regions()) {
        const current = seeded[region.name];
        if (current && (current.activityLabel || current.itemLabel)) {
          continue;
        }
        const label = (selector.getBlockLabel(region.name) ?? '').slice(0, MAX_QUESTION_LABEL);
        const kind = selector.getBlockKind(region.name);
        const next =
          kind === 'activity'
            ? { activityLabel: label, itemLabel: '' }
            : kind === 'mcq_question'
              ? { activityLabel: label ? section.slice(0, MAX_QUESTION_LABEL) : '', itemLabel: label }
              : null;
        if (next && (next.activityLabel || next.itemLabel)) {
          seeded[region.name] = next;
          changed = true;
        }
      }
      if (changed) {
        this.questionLabels.set(seeded);
      }
    }
    this.lookupPage();
  }

  /** Sayfadaki etiketli soru / etkinlik bloklarının anahtarı (aranmamış ya da sorgusu değişmiş olanlar). */
  private lookupPage(): void {
    const selector = this.selectorRef();
    if (!selector || this.bookId() == null || this.filePage() == null || !this.bookPage() || !this.showKindLegend()) {
      return;
    }
    const names = selector
      .regions()
      .map((r) => r.name)
      .filter((name) => {
        const query = this.lookupQuery(name);
        return !!query && this.keyLookups()[name]?.query !== query;
      });
    if (names.length > 0) {
      this.lookupKeys(names, false);
    }
  }

  /** Kitap sayfası bilgisi geldi: etiketli blokların anahtarı aranır. */
  private refreshKeyLookups(): void {
    this.lookupPage();
  }

  private clearKeyLookups(): void {
    this.keyLookupTimers.forEach((timer) => clearTimeout(timer));
    this.keyLookupTimers.clear();
    this.keyLookupSeq++;
    if (Object.keys(this.keyLookups()).length > 0) {
      this.keyLookups.set({});
    }
    this.keyChoices.set({});
  }

  // ---------------- Issue #580: etkinlik çözümü ----------------

  /**
   * Bloğun geçerli çözümü: yazıldığı yapı taslağı hâlâ geçerliyse (yeniden çıkarılmadı, tip değişmedi); değilse null.
   */
  solutionOf(name: string): ActivitySolutionDraft | null {
    const solution = this.activitySolutions()[name];
    const draft = this.draftOf(name);
    if (
      !solution ||
      !draft ||
      draft.status !== 'ok' ||
      solution.draftRequestId !== draft.requestId ||
      solution.interactionType !== draft.interactionType
    ) {
      return null;
    }
    return solution;
  }

  /** Çözüm istenebilir mi: taslak tuvalde (uygulandı ya da öğretmenin yazarlığı korundu), istek / kayıt sürmüyor. */
  canSolve(name: string): boolean {
    const draft = this.draftOf(name);
    const solution = this.solutionOf(name);
    return (
      this.structureEnabled() &&
      !this.inspectorLocked &&
      draft?.status === 'ok' &&
      !draft.stale &&
      draft.apply != null &&
      draft.apply !== 'invalid' &&
      solution?.status !== 'loading' &&
      solution?.busy == null
    );
  }

  /** "Anahtardan çöz" (eşleşen satırlarla) / "AI ile çöz (ücretli)"; kayıtlı sonuç `force` değilse ücretsiz döner. */
  solve(name: string, mode: LabelingSolutionMode): void {
    if (!this.canSolve(name)) {
      return;
    }
    this.requestSolution(name, mode, false);
  }

  /** "Yeniden çöz (ücretli)": kayıtlı sonuç yok sayılır — önce onay. */
  resolve(name: string): void {
    const current = this.solutionOf(name);
    if (!current || !this.canSolve(name)) {
      return;
    }
    const token = this.pageToken;
    const requestId = current.requestId;
    this.snackBar
      .open(this.tr('solution.resolveConfirm'), this.tr('solution.resolveAction'), { duration: 8000 })
      .onAction()
      .subscribe(() => {
        if (token === this.pageToken && this.solutionOf(name)?.requestId === requestId && this.canSolve(name)) {
          this.requestSolution(name, current.mode, true);
        }
      });
  }

  private requestSolution(name: string, mode: LabelingSolutionMode, force: boolean): void {
    const selector = this.selectorRef();
    const draft = this.draftOf(name);
    const block = this.typedActivityBlocks().find((b) => b.key === name);
    const image = this.pageImage;
    const model = this.structureModel();
    if (!selector || !draft?.result || !block || block.interactionType !== draft.interactionType || !image || !model) {
      return;
    }
    const entryIds = mode === 'key' ? this.matchingKeyEntries(name).map((e) => e.id) : [];
    if (mode === 'key' && entryIds.length === 0) {
      return;
    }
    // Çözüm tuvaldeki planın id'lerine göre yazılsın (taslak ya da öğretmenin korunan yazarlığı).
    const plan = selector.getInteractionPlan(name) ?? draft.result.plan;
    if (!plan) {
      return;
    }
    const requestId = ++this.solutionSeq;
    const type = block.interactionType;
    this.activitySolutions.update((all) => ({
      ...all,
      [name]: {
        interactionType: type,
        draftRequestId: draft.requestId,
        mode,
        status: 'loading',
        response: null,
        error: null,
        apply: null,
        appliedJson: null,
        busy: null,
        requestId,
        planJson: canonicalJson(plan),
      },
    }));
    selector.setDraftSolution(name, null);
    const token = this.pageToken;
    const revision = this.draftRevision;
    const current = () =>
      token === this.pageToken &&
      revision === this.draftRevision &&
      this.activitySolutions()[name]?.requestId === requestId &&
      this.draftOf(name)?.requestId === draft.requestId;
    this.labelingApi
      .solveActivity({
        imageBase64: image,
        x: block.x,
        y: block.y,
        width: block.width,
        height: block.height,
        interactionType: type,
        plan,
        model,
        force,
        mode,
        ...(mode === 'key' ? { key: { entryIds } } : {}),
      })
      .pipe(takeUntil(merge(this.pageChanged, this.draftsReset)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          if (!current()) {
            return;
          }
          const hasSolution = !!response?.solution;
          const apply = hasSolution ? (this.selectorRef()?.applyInteractionSolution(name, type, response.solution) ?? null) : null;
          this.patchSolution(name, requestId, {
            status: hasSolution ? 'ok' : 'error',
            response,
            error: hasSolution ? null : response?.error || this.tr('solution.failed'),
            apply,
            appliedJson: apply === 'applied' ? this.canvasSolutionJson(name) : null,
          });
          this.linkDraftSolution(name);
          if (response?.origin === 'live' && response.estimatedCostUsd != null) {
            this.snackBar.open(
              this.tr('source.liveDone', { cost: response.estimatedCostUsd.toFixed(4) }),
              this.tr('ok'),
              { duration: 4000 }
            );
          }
        },
        error: (error) => {
          if (!current()) {
            return;
          }
          console.error('labeling activity-solution failed', error);
          const message = this.serverError(error, 'solution.failed', 'solution.unavailable');
          this.patchSolution(name, requestId, { status: 'error', error: message });
          // 429 (saatlik tavan / günlük bütçe) ve diğer hatalar: sunucunun mesajı gösterilir.
          this.snackBar.open(message, this.tr('ok'), { duration: 6000 });
        },
      });
  }

  /**
   * Sayfa yeniden açıldı: bloğun kırpmasının kayıtlı çözüm taslağı (en son güncellenen, aynı tip; planı tuvaldekiyle
   * aynı olan önce) tuvale yüklenir. Kayıt yoksa / istek başarısızsa sessiz kalınır (öğretmen yeniden çözebilir).
   */
  private restoreSolution(name: string): void {
    const draft = this.draftOf(name);
    const crop = draft?.result?.cropSha256;
    if (!draft || draft.status !== 'ok' || !crop || draft.apply == null || draft.apply === 'invalid' || this.solutionOf(name)) {
      return;
    }
    const token = this.pageToken;
    const revision = this.draftRevision;
    this.restoringSolutions.add(name);
    const done = () => {
      this.restoringSolutions.delete(name);
      if (token === this.pageToken && revision === this.draftRevision) {
        this.tryAutoSolve(name);
      }
    };
    this.labelingApi
      .getActivitySolutions(crop)
      .pipe(takeUntil(merge(this.pageChanged, this.draftsReset)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => {
          const latest = this.draftOf(name);
          const selector = this.selectorRef();
          if (
            token !== this.pageToken ||
            revision !== this.draftRevision ||
            latest?.requestId !== draft.requestId ||
            this.activitySolutions()[name] ||
            !selector
          ) {
            return;
          }
          const type = draft.interactionType;
          const candidates = (list?.solutions ?? []).filter(
            (sol) => !!sol.solution && sol.draftSolutionId != null && (sol.interactionType ?? type) === type
          );
          const plan = canonicalJson(selector.getInteractionPlan(name));
          // W7: yalnız tuvaldeki planla birebir aynı plana yazılmış çözüm yüklenir; başka plana yazılmışsa id'ler tutmayabilir.
          const pick = candidates.find((sol) => !!sol.plan && canonicalJson(sol.plan) === plan);
          if (!pick) {
            if (candidates.length > 0) {
              const latest = candidates[0];
              this.activitySolutions.update((all) => ({
                ...all,
                [name]: {
                  interactionType: type,
                  draftRequestId: draft.requestId,
                  mode: latest.mode ?? (latest.source === 'AI' ? 'ai' : 'key'),
                  status: 'planChanged',
                  response: null,
                  error: null,
                  apply: null,
                  appliedJson: null,
                  busy: null,
                  requestId: ++this.solutionSeq,
                  planJson: null,
                },
              }));
            }
            return;
          }
          const apply = selector.applyInteractionSolution(name, type, pick.solution);
          this.activitySolutions.update((all) => ({
            ...all,
            [name]: {
              interactionType: type,
              draftRequestId: draft.requestId,
              mode: pick.mode ?? (pick.source === 'AI' ? 'ai' : 'key'),
              status: 'ok',
              response: pick,
              error: null,
              apply,
              appliedJson: apply === 'applied' ? this.canvasSolutionJson(name) : null,
              busy: null,
              requestId: ++this.solutionSeq,
              planJson: plan,
            },
          }));
          this.linkDraftSolution(name);
        },
        error: (error) => {
          console.error('labeling activity-solution restore failed', error);
          done();
        },
        complete: done,
      });
  }

  /**
   * "Çözümü uygula": öğretmenin elle yazarlığı korunduğu için bekleyen çözümü tuvale yazar — el düzenlemeleri kaybolur,
   * önce onay.
   */
  applySolution(name: string): void {
    const solution = this.solutionOf(name);
    if (!solution?.response?.solution || solution.apply !== 'kept' || this.inspectorLocked) {
      return;
    }
    const token = this.pageToken;
    this.snackBar
      .open(this.tr('solution.applyConfirm'), this.tr('solution.applyAction'), { duration: 8000 })
      .onAction()
      .subscribe(() => {
        const latest = this.solutionOf(name);
        const selector = this.selectorRef();
        if (token !== this.pageToken || latest?.requestId !== solution.requestId || !selector || !latest.response?.solution) {
          return;
        }
        const apply = selector.applyInteractionSolution(name, latest.interactionType, latest.response.solution, {
          replace: true,
        });
        this.patchSolution(name, latest.requestId, {
          apply,
          appliedJson: apply === 'applied' ? this.canvasSolutionJson(name) : null,
        });
        this.linkDraftSolution(name);
      });
  }

  /**
   * W8: tuvaldeki plan çözümün yazıldığı plandan farklı (öğretmen satır / kutu / öğe değiştirdi) — çözüm bayat; onay ve
   * düzeltme kaydı kapalı, yeniden çözülmeli.
   */
  solutionPlanStale(name: string): boolean {
    const solution = this.solutionOf(name);
    if (solution?.status !== 'ok' || solution.planJson == null) {
      return false;
    }
    return canonicalJson(this.selectorRef()?.getInteractionPlan(name) ?? null) !== solution.planJson;
  }

  /**
   * Tuvaldeki çözüm bu taslak kaydından geldiyse tuvale bildirilir: soru kaydında `draftSolutionId` gider (#580). Çözüm
   * uygulanmadıysa bağ kaldırılır.
   */
  private linkDraftSolution(name: string): void {
    const solution = this.solutionOf(name);
    const id = solution?.status === 'ok' && solution.apply === 'applied' ? (solution.response?.draftSolutionId ?? null) : null;
    this.selectorRef()?.setDraftSolution(name, id);
  }

  /** Tuvaldeki çözüm uygulanandan farklı (öğretmen tuvalde işaretleri / eşleşmeleri değiştirdi). */
  solutionEdited(name: string): boolean {
    const solution = this.solutionOf(name);
    if (
      solution?.status !== 'ok' ||
      solution.apply !== 'applied' ||
      solution.appliedJson == null ||
      this.solutionPlanStale(name)
    ) {
      return false;
    }
    return this.canvasSolutionJson(name) !== solution.appliedJson;
  }

  /** "Düzeltmeyi kaydet": tuvaldeki çözüm sunucuya yazılır (kaynak `Human`, onay bekler). */
  saveSolutionEdit(name: string): void {
    const solution = this.solutionOf(name);
    const id = solution?.response?.draftSolutionId;
    const edited = this.selectorRef()?.getInteractionSolution(name) ?? null;
    if (!solution || id == null || solution.busy != null || this.inspectorLocked || this.solutionPlanStale(name)) {
      return;
    }
    if (!edited) {
      this.snackBar.open(this.tr('solution.emptyEdit'), this.tr('ok'), { duration: 5000 });
      return;
    }
    const json = JSON.stringify(edited);
    this.runSolutionAction(name, solution, 'saving', this.labelingApi.updateActivitySolution(id, edited, false), json);
  }

  /**
   * "Onayla" (Admin + QuestionTrainer): onaylayan ve zaman kayda yazılır. Tuvalde düzeltme varsa düzeltme kaydedilip
   * onaylanır (kaynak `Human`); doğrulamadan geçmeyen çözüm düzeltilmeden onaylanmaz.
   */
  approveSolution(name: string): void {
    if (!this.canApproveSolution(name)) {
      return;
    }
    const solution = this.solutionOf(name)!;
    const id = solution.response!.draftSolutionId!;
    if (this.solutionEdited(name)) {
      const edited = this.selectorRef()?.getInteractionSolution(name) ?? null;
      if (!edited) {
        this.snackBar.open(this.tr('solution.emptyEdit'), this.tr('ok'), { duration: 5000 });
        return;
      }
      this.runSolutionAction(
        name,
        solution,
        'approving',
        this.labelingApi.updateActivitySolution(id, edited, true),
        JSON.stringify(edited)
      );
      return;
    }
    this.runSolutionAction(name, solution, 'approving', this.labelingApi.approveActivitySolution(id), null);
  }

  /** Onay düğmesi etkin mi: yetki, kayıt, onaylanmamış, iş sürmüyor; geçersiz çözüm yalnız düzeltilmişse. */
  canApproveSolution(name: string): boolean {
    const solution = this.solutionOf(name);
    const response = solution?.response;
    return (
      this.canApproveSolutions &&
      !this.inspectorLocked &&
      solution?.status === 'ok' &&
      solution.busy == null &&
      response?.draftSolutionId != null &&
      response.status !== 'approved' &&
      !this.solutionPlanStale(name) &&
      (response.valid || this.solutionEdited(name))
    );
  }

  private runSolutionAction(
    name: string,
    solution: ActivitySolutionDraft,
    busy: 'saving' | 'approving',
    request: Observable<LabelingActivitySolutionResponse>,
    appliedJson: string | null
  ): void {
    const token = this.pageToken;
    const requestId = solution.requestId;
    this.patchSolution(name, requestId, { busy });
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (response) => {
        if (token !== this.pageToken) {
          return;
        }
        this.patchSolution(name, requestId, {
          busy: null,
          // Sunucu yalnız değişen alanları döndürse de önceki yanıt korunur (ör. çözüm gövdesi).
          response: { ...solution.response!, ...response },
          ...(appliedJson != null ? { appliedJson } : {}),
        });
        this.linkDraftSolution(name);
        this.snackBar.open(this.tr(busy === 'approving' ? 'solution.approvedDone' : 'solution.savedDone'), this.tr('ok'), {
          duration: 4000,
        });
      },
      error: (error) => {
        if (token !== this.pageToken) {
          return;
        }
        console.error(`labeling activity-solution ${busy} failed`, error);
        this.patchSolution(name, requestId, { busy: null });
        const status = (error as { status?: number })?.status;
        const message =
          busy === 'approving' && status === 403
            ? this.tr('solution.forbidden')
            : this.serverError(error, busy === 'approving' ? 'solution.approveFailed' : 'solution.saveFailed');
        this.snackBar.open(message, this.tr('ok'), { duration: 6000 });
      },
    });
  }

  private patchSolution(name: string, requestId: number, patch: Partial<ActivitySolutionDraft>): void {
    this.activitySolutions.update((all) =>
      all[name]?.requestId === requestId ? { ...all, [name]: { ...all[name], ...patch } } : all
    );
  }

  private canvasSolutionJson(name: string): string | null {
    const solution = this.selectorRef()?.getInteractionSolution(name) ?? null;
    return solution ? JSON.stringify(solution) : null;
  }

  /** Sunucu mesajı (yerelleştirilmiş) varsa o; 503'te `unavailableKey`, diğerlerinde `fallbackKey`. */
  private serverError(error: unknown, fallbackKey: string, unavailableKey?: string): string {
    const status = (error as { status?: number })?.status;
    const serverMessage = (error as { error?: { message?: string } })?.error?.message;
    if (serverMessage) {
      return serverMessage;
    }
    return this.tr(status === 503 && unavailableKey ? unavailableKey : fallbackKey);
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

/** Anahtar satırının kısa etiketi: "s. 12 · Etkinlik 3 · b" (boş alanlar atlanır). */
function entryTag(entry: LabelingAnswerKeyEntry): string {
  return [
    entry.printedPage != null ? `s. ${entry.printedPage}` : null,
    entry.activityLabel?.trim() || null,
    entry.itemLabel?.trim() || null,
  ]
    .filter((part): part is string => !!part)
    .join(' · ');
}

/** Etiket karşılaştırması: noktalama atılır ("a)" ≈ "a", "1." ≈ "1"), boşluklar sadeleşir, Türkçe küçük harf. */
export function normalizeLabel(value: string | null | undefined): string {
  return (value ?? '')
    .replace(/[\p{P}\p{S}]+/gu, ' ')
    .trim()
    .replace(/\s+/g, ' ')
    .toLocaleLowerCase('tr');
}

/** Anahtarları sıralı JSON (plan karşılaştırması alan sırasından bağımsız olsun). */
function canonicalJson(value: unknown): string {
  const sort = (v: unknown): unknown => {
    if (Array.isArray(v)) {
      return v.map(sort);
    }
    if (v && typeof v === 'object') {
      return Object.fromEntries(
        Object.keys(v as Record<string, unknown>)
          .sort()
          .map((k) => [k, sort((v as Record<string, unknown>)[k])])
      );
    }
    return v;
  };
  return JSON.stringify(sort(value ?? null));
}

function readDataUrl(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result ?? ''));
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(file);
  });
}

/** Anahtardaki şık harfi → sıra ("B" → 1, "c)" → 2); harf değilse null. */
function optionIndexOf(answer: string | null | undefined): number | null {
  const match = /^\s*\(?([A-Ja-j])\)?[.)]?\s*$/.exec(answer ?? '');
  return match ? match[1].toUpperCase().charCodeAt(0) - 65 : null;
}

function omit<T>(record: Record<string, T>, key: string): Record<string, T> {
  const next = { ...record };
  delete next[key];
  return next;
}

function sameBox(a: LabelingBankBox, b: LabelingBankBox): boolean {
  return a.x === b.x && a.y === b.y && a.width === b.width && a.height === b.height;
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
