import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoService } from '@jsverse/transloco';
import { Subject, firstValueFrom, takeUntil } from 'rxjs';

import { ImageSelectorComponent } from '../image-selector/image-selector.component';
import { LabelingAiDetectResponse, LabelingAiModel, LabelingPage, LabelingResult } from '../../models/labeling';
import { LabelingService, manualDetectionSource, sha256OfDataUrl } from '../../services/labeling.service';

/** Soru tuvalinde kutuların kaynağı: kayıtlı manuel / YOLO (Model) / Claude (Ai). */
export type CanvasSourceKind = 'Manual' | 'Model' | 'Ai';
type PreferredSource = 'Model' | 'Ai';

/** İşaretleme ekranından ayrı tutulur: tuvalde varsayılan YOLO, Claude yalnız burada seçildiyse açılır. */
const SOURCE_STORAGE_KEY = 'questionCanvas.source';
const AI_MODEL_STORAGE_KEY = 'questionCanvas.aiModel';
/**
 * Bu yanıtlarda (yetki yok, kota / bütçe dolu, anahtar yok) Claude oturum boyunca durur: önceden getirme yapılmaz, sayfalar
 * YOLO ile açılır. Kullanıcı Claude'u yeniden seçerse bir kez daha denenir.
 */
const SUSPEND_STATUSES = [403, 429, 503];

/**
 * Soru tuvalinde (issue #473) tespit kaynağı seçimi: işaretleme ekranındaki Manuel | Model | Claude düzeninin tuval
 * sürümü. Yalnız `ai-models` boş olmayan kullanıcıya (bugün Admin + QuestionTrainer) açılır; diğerlerinde image-selector
 * `externalDetection` kapalı kalır ve davranış değişmez. Kayıt yoksa sayfa tercih edilen kaynakla açılır (varsayılan
 * YOLO). Claude seçiliyken bir sonraki sayfa arka planda Claude'a sorulur (sunucu sonucu sayfa hash'iyle saklar; sayfaya
 * geçince kayıttan, ücretsiz ve hızlı döner). Claude hatası / bütçe aşımında mesaj gösterilir ve sayfa YOLO ile açılır.
 * Sayfa değişince açık sayfanın Claude isteği iptal edilir (sayfalar hızla geçilirse paralel ücretli çağrı birikmesin; sunucu
 * da Claude çağrısını keser). Önceden getirme iptal edilmez, en çok bir tane sürer; açılan sayfa onu bekler.
 */
@Injectable()
export class CanvasDetectionSource {
  private readonly labelingApi = inject(LabelingService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  private selector: () => ImageSelectorComponent | undefined = () => undefined;
  private scope = '';

  /** İzinli Claude modelleri; boşsa (yetki / yapılandırma yok) kaynak seçimi gösterilmez. */
  readonly aiModels = signal<LabelingAiModel[]>([]);
  readonly available = computed(() => this.aiModels().length > 0);
  readonly aiModelId = signal<string>(readStored(AI_MODEL_STORAGE_KEY) ?? '');
  readonly selectedAiModel = computed(() => this.aiModels().find((m) => m.id === this.aiModelId()) ?? null);
  readonly preferredSource = signal<PreferredSource>(readStored(SOURCE_STORAGE_KEY) === 'Ai' ? 'Ai' : 'Model');
  /** Açık sayfanın exam API'deki kaydı (yoksa null); Manuel seçeneği kayıt varsa görünür. */
  readonly pageRecord = signal<LabelingPage | null>(null);
  readonly manualRecord = computed(() => this.pageRecord()?.results.find((r) => r.source === 'Manual') ?? null);
  readonly aiBusy = signal(false);
  /** Tuvaldeki Claude sonucu kayıttan mı (ücretsiz) yoksa canlı mı geldi. */
  readonly aiOrigin = signal<'live' | 'cached' | null>(null);
  /**
   * Son Claude hatasının mesajı (bütçe / kota dahil); araç çubuğunda kalır. Snackbar'daki kopyası YOLO'nun "analiz
   * tamamlandı" bildirimiyle kapanabildiği için. Claude başarılı olunca ya da kaynak seçilince temizlenir.
   */
  readonly aiNotice = signal<string | null>(null);
  /** Görünen kaynak; Claude yanıtı beklenirken Claude. */
  readonly activeKind = computed<CanvasSourceKind | null>(() =>
    this.aiBusy() ? 'Ai' : (this.selector()?.detectionSource()?.kind ?? null)
  );
  readonly showModelPicker = computed(
    () => this.available() && (this.preferredSource() === 'Ai' || this.activeKind() === 'Ai')
  );
  /** Sayfa görseli hazır (kaynak değiştirilebilir). */
  readonly hasPage = computed(() => this.pageImage() !== null);

  private readonly pageImage = signal<string | null>(null);
  private pageSha: string | null = null;
  private pageToken = 0;
  private modelsLoaded: Promise<void> = Promise.resolve();
  /** Süren Claude istekleri (`sha|model`): sayfa önceden getirilirken açılırsa aynı istek beklenir, ikinci kez sorulmaz. */
  private readonly inflight = new Map<string, Promise<LabelingAiDetectResponse>>();
  /** Bu oturumda sonucu alınmış ya da Claude'un işaretleyemediği sayfalar: yeniden önceden getirilmez. */
  private readonly fetched = new Set<string>();
  private readonly failed = new Set<string>();
  /** Açık sayfanın (önceden getirme dışı) Claude isteğini iptal eder. */
  private readonly pageChanged = new Subject<void>();
  /** `SUSPEND_STATUSES` alındı: Claude tercihi korunur ama sayfalar YOLO ile açılır, önceden getirme yapılmaz. */
  private claudeSuspended = false;
  private destroyed = false;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.pageToken++;
      this.pageChanged.next();
    });
  }

  /**
   * Tuval bağlanır. `enabled` false ise (eğitim rolü yok) model listesi hiç istenmez; Claude seçeneği görünmez.
   * `scope` çeviri anahtarlarının öneki (`question`).
   */
  attach(selector: () => ImageSelectorComponent | undefined, enabled: boolean, scope: string): void {
    this.selector = selector;
    this.scope = scope;
    if (enabled) {
      this.modelsLoaded = this.loadAiModels();
    }
  }

  private async loadAiModels(): Promise<void> {
    try {
      const models = (await firstValueFrom(this.labelingApi.getAiModels())) ?? [];
      this.aiModels.set(models);
      if (!models.some((m) => m.id === this.aiModelId())) {
        this.aiModelId.set(models.find((m) => m.isDefault)?.id ?? models[0]?.id ?? '');
      }
    } catch (error) {
      // Yetki yok (403) ya da servis kapalı: Claude seçeneği gizlenir, YOLO ile devam.
      console.error('question canvas ai-models failed', error);
      this.aiModels.set([]);
    }
  }

  /** Yeni sayfa yüklenmeye başladı: eski sayfanın kaydı ve geç gelen Claude yanıtı geçersiz. */
  resetPage(): void {
    this.pageToken++;
    this.pageChanged.next();
    this.pageImage.set(null);
    this.pageSha = null;
    this.pageRecord.set(null);
    this.aiOrigin.set(null);
    this.aiBusy.set(false);
  }

  /**
   * Sayfa görseli hazır (`externalDetection`). Tercih Model ise YOLO hemen başlar (eski davranış); Claude ise model
   * listesi beklenir ve Claude'a sorulur; örtü hemen açılır ki beklerken çizilen kutular Claude sonucuyla ezilmesin. Manuel
   * kayıt arka planda aranır, yalnız seçilirse uygulanır.
   */
  async onPageReady(imageData: string): Promise<void> {
    const selector = this.selector();
    if (!selector) {
      return;
    }
    this.resetPage();
    const token = this.pageToken;
    this.pageImage.set(imageData);
    const wantsAi = this.preferredSource() === 'Ai' && !this.claudeSuspended;
    let seq: number | undefined;
    if (!wantsAi) {
      selector.predict();
    } else {
      seq = selector.beginExternalDetection();
      this.aiBusy.set(true);
      await this.modelsLoaded;
    }

    try {
      this.pageSha = await sha256OfDataUrl(imageData);
    } catch (error) {
      console.error('question canvas page hash failed', error);
    }
    if (token !== this.pageToken) {
      return;
    }
    if (wantsAi) {
      if (this.available() && this.pageSha) {
        void this.runClaude(seq);
      } else {
        this.aiBusy.set(false);
        selector.predict();
      }
    }
    if (this.available() && this.pageSha) {
      void this.loadPageRecord(this.pageSha, token);
    }
  }

  /** "Kaydet ve Düzelt" / "Eğit" manuel kaydı yazdı: Manuel seçeneği güncellensin (sayfa hâlâ açıksa). */
  onManualSaved(imageData: string): void {
    if (imageData === this.pageImage() && this.pageSha) {
      void this.loadPageRecord(this.pageSha, this.pageToken);
    }
  }

  /** Araç çubuğundan kaynak seçimi; kaydedilmemiş elle düzeltme varsa önce onay istenir. */
  selectSource(kind: CanvasSourceKind): void {
    if (!this.hasPage() || kind === this.activeKind() || (kind === 'Manual' && !this.manualRecord())) {
      return;
    }
    this.confirmDiscard(() => {
      this.aiNotice.set(null);
      if (kind === 'Ai') {
        // Açık seçim: askıdaki Claude bir kez daha denenir.
        this.claudeSuspended = false;
      }
      if (kind !== 'Manual') {
        this.setPreferredSource(kind);
      }
      this.applySource(kind);
    });
  }

  setAiModel(id: string): void {
    if (id === this.aiModelId() || !this.aiModels().some((m) => m.id === id)) {
      return;
    }
    const apply = () => {
      this.aiModelId.set(id);
      writeStored(AI_MODEL_STORAGE_KEY, id);
      if (this.activeKind() === 'Ai' && this.hasPage()) {
        void this.runClaude();
      }
    };
    if (this.activeKind() === 'Ai') {
      this.confirmDiscard(apply);
      return;
    }
    apply();
  }

  aiModelName(id: string | null | undefined): string {
    return this.aiModels().find((m) => m.id === id)?.displayName ?? id ?? '';
  }

  private applySource(kind: CanvasSourceKind): void {
    const selector = this.selector();
    if (!selector || !this.hasPage()) {
      return;
    }
    if (kind === 'Model') {
      selector.predict();
    } else if (kind === 'Ai') {
      void this.runClaude();
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
   * Claude ile tespit (kayıtlı sonuç varsa sunucu Claude'a gitmez, ücretsiz döner). Hata — bütçe / kota dahil — olursa
   * sunucunun mesajı gösterilir ve sayfa YOLO ile açılır. Başarılıysa sonraki sayfa önceden getirilir. `openSeq`: sayfa
   * açılışında önceden başlatılmış tespit.
   */
  private async runClaude(openSeq?: number): Promise<void> {
    const selector = this.selector();
    const model = this.aiModelId();
    const image = this.pageImage();
    const sha = this.pageSha;
    if (!selector || !image) {
      return;
    }
    if (!model || !sha) {
      selector.predict();
      return;
    }
    const token = this.pageToken;
    const seq = openSeq ?? selector.beginExternalDetection();
    this.aiBusy.set(true);
    try {
      const response = await this.detect(sha, image, model, true);
      if (token !== this.pageToken) {
        return;
      }
      this.mergeResult(response.imageSha256, response.result);
      this.aiOrigin.set(response.origin);
      this.aiNotice.set(null);
      selector.applyExternalPrediction(seq, response.result.predictions, {
        kind: 'Ai',
        modelName: response.result.modelName,
        promptVersion: response.result.promptVersion,
      });
      if (response.origin === 'live' && response.result.estimatedCostUsd != null) {
        this.snackBar.open(
          this.tr('aiLiveDone', { cost: response.result.estimatedCostUsd.toFixed(4) }),
          this.tr('aiOk'),
          { duration: 4000 }
        );
      }
      void this.prefetchNext(model);
    } catch (error) {
      if (token !== this.pageToken) {
        return;
      }
      console.error('question canvas ai-detect failed', error);
      selector.failExternalDetection(seq);
      const status = (error as { status?: number })?.status;
      if (status != null && SUSPEND_STATUSES.includes(status)) {
        this.claudeSuspended = true;
      }
      const serverMessage = (error as { error?: { message?: string } })?.error?.message;
      const message = this.tr('aiFallback', { message: serverMessage || this.tr('aiFailed') });
      this.aiNotice.set(message);
      this.snackBar.open(message, this.tr('aiOk'), { duration: 6000 });
      selector.predict();
    } finally {
      if (token === this.pageToken) {
        this.aiBusy.set(false);
      }
    }
  }

  /**
   * Aynı sayfa + model için süren istek (önceden getirme) varsa onu bekler; yoksa `ai-detect` (force yok — kayıtlı sonuç
   * ücretsiz). `cancelOnPageChange`: açık sayfanın isteği sayfa değişince iptal edilir.
   */
  private detect(
    sha: string,
    image: string,
    model: string,
    cancelOnPageChange: boolean
  ): Promise<LabelingAiDetectResponse> {
    const key = `${sha}|${model}`;
    const pending = this.inflight.get(key);
    if (pending) {
      return pending;
    }
    const call = this.labelingApi.aiDetect({ imageBase64: image, model, force: false });
    const request = firstValueFrom(cancelOnPageChange ? call.pipe(takeUntil(this.pageChanged)) : call);
    this.inflight.set(key, request);
    request
      .then(
        () => this.fetched.add(key),
        (error) => {
          // İptal (EmptyError, status yok) başarısızlık sayılmaz: sayfa yeniden açılınca sorulur.
          if ((error as { status?: number })?.status != null) {
            this.failed.add(key);
          }
        }
      )
      .finally(() => this.inflight.delete(key));
    return request;
  }

  /**
   * Sonraki sayfayı (son sayfadan sonra ilk sayfa — `nextImage` başa sarar) arka planda Claude'a sorar: yalnız Claude
   * tercih edilmişse, sayfa başına bir kez, başka Claude isteği sürmüyorken. Otomatik ilerle açıkken yapılmaz (sayfalar
   * kullanıcı bakmadan geçilebilir). Kota / bütçe / yetki hatasında Claude askıya alınır; hata gösterilmez.
   */
  private async prefetchNext(model: string): Promise<void> {
    const selector = this.selector();
    if (
      !selector ||
      this.claudeSuspended ||
      this.destroyed ||
      this.preferredSource() !== 'Ai' ||
      selector.autoMode() ||
      selector.imageFiles.length < 2
    ) {
      return;
    }
    const next = selector.imageFiles[(selector.currentImageIndex + 1) % selector.imageFiles.length];
    if (!next) {
      return;
    }
    try {
      const image = await readAsDataUrl(next);
      const sha = await sha256OfDataUrl(image);
      const key = `${sha}|${model}`;
      if (this.destroyed || this.fetched.has(key) || this.failed.has(key) || this.inflight.size > 0) {
        return;
      }
      await this.detect(sha, image, model, false);
    } catch (error) {
      const status = (error as { status?: number })?.status;
      if (status != null && SUSPEND_STATUSES.includes(status)) {
        this.claudeSuspended = true;
      }
      console.warn('question canvas claude prefetch failed', error);
    }
  }

  private async loadPageRecord(sha: string, token: number): Promise<void> {
    try {
      const page = await firstValueFrom(this.labelingApi.getPage(sha));
      if (token === this.pageToken) {
        this.pageRecord.set(page);
      }
    } catch (error) {
      if ((error as { status?: number })?.status !== 404) {
        console.error('question canvas page lookup failed', error);
      }
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

  /** Kaydedilmemiş elle düzeltme varsa önce sorulur (snackbar eylemi), yoksa hemen uygulanır. */
  private confirmDiscard(action: () => void): void {
    if (!this.selector()?.hasUnsavedEdits()) {
      action();
      return;
    }
    const token = this.pageToken;
    this.snackBar
      .open(this.tr('unsavedEdits'), this.tr('discardAction'), { duration: 8000 })
      .onAction()
      .subscribe(() => {
        if (token === this.pageToken && !this.destroyed) {
          action();
        }
      });
  }

  private setPreferredSource(kind: PreferredSource): void {
    this.preferredSource.set(kind);
    writeStored(SOURCE_STORAGE_KEY, kind);
  }

  private tr(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${this.scope}.detection.${key}`, params) ?? '';
  }
}

function readAsDataUrl(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result as string);
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(file);
  });
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
