import { DestroyRef, Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoService } from '@jsverse/transloco';
import { Subject, takeUntil } from 'rxjs';

import { ImageSelectorComponent, InteractionDraftApply } from '../image-selector/image-selector.component';
import { LabelingActivityBlock, LabelingActivityStructureResult, LabelingBankBox } from '../../models/labeling';
import { BlockInteractionType, isDraftableInteractionType } from '../../models/prediction';
import { LabelingService } from '../../services/labeling.service';

/** Issue #579: tek istekte en çok bu kadar blok (sunucu sınırı); fazlası ardışık isteklere bölünür. */
export const MAX_ACTIVITY_BLOCKS_PER_REQUEST = 20;
/** Bölge taşınınca / boyutlanınca yeniden istekten önce beklenen süre (sürükleme bitsin). */
export const STRUCTURE_BOX_DEBOUNCE_MS = 800;

/**
 * Issue #579 / #581: etkinlik bloğunun Claude taslağı (bölge adına göre). Tipi bölgenin güncel tipinden farklıysa (müfettişte
 * değişti) yeniden istenir; uygulanmış plan bölgenin o tipteki yazarlık durumundadır, öğretmen orada düzeltir ve çözümü
 * elle işaretler.
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

/** Tuvalin taslak akışına bağlandığı yüzey (hepsi sinyal okur; değişince taslaklar yeniden eşitlenir). */
export interface ActivityDraftsConfig {
  selector: () => ImageSelectorComponent | undefined;
  /** Taslak istenebilir: etkinlik bağlamı, izinli Claude modeli ve açık sayfa. */
  enabled: () => boolean;
  /** Müfettiş eylemleri kilitli (kayıt / geçiş / analiz sürüyor). */
  locked: () => boolean;
  /** Taslağı çıkaracak Claude modeli. */
  model: () => string;
  /** Çeviri anahtarlarının öneki (`question`). */
  scope: string;
}

/**
 * Issue #581: soru tuvalinde (etkinlik sayfası) tipli etkinlik blokları için #579 taslak yapısı. Sayfa açılınca tipli
 * blokların hepsi tek toplu istekte sorulur; tipi değişen blok yeniden, kutusu değişen blok sürükleme bitince yeniden
 * istenir. Öğretmenin elle yazarlığı taslakla ezilmez ("Taslağı uygula" onayı). Hatalı taslak kendiliğinden yeniden
 * istenmez. Kayıtlı sonuç sunucuda kırpma hash'iyle saklanır (force yoksa ücretsiz döner).
 */
@Injectable()
export class CanvasActivityDrafts {
  private readonly labelingApi = inject(LabelingService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly config = signal<ActivityDraftsConfig | null>(null);
  /** Bölge adı → taslak. Yeni tespitte / sayfada sıfırlanır. */
  readonly drafts = signal<Record<string, ActivityDraft>>({});
  readonly enabled = computed(() => this.config()?.enabled() ?? false);
  /** Taslakların ait olduğu tespit (`ImageSelectorComponent.detectionRevision`). */
  private revision: number | null = null;
  private requestSeq = 0;
  private pageToken = 0;
  /** Yeni sayfa / yeni tespit: süren yapı istekleri iptal edilir. */
  private readonly reset$ = new Subject<void>();
  /** Kutu değişikliğinden sonraki yeniden istek zamanlayıcısı (testlerde 0). */
  debounceMs = STRUCTURE_BOX_DEBOUNCE_MS;
  private boxRefreshTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    effect(() => {
      const config = this.config();
      const selector = config?.selector();
      if (!config || !selector) {
        return;
      }
      // Bölgeler (tür / tip değişikliği `regions` sinyalini yeniler), tespit, bağlam ve analiz izlenir.
      selector.regions();
      const revision = selector.detectionRevision();
      const enabled = config.enabled();
      const busy = selector.inProgress();
      untracked(() => this.sync(revision, enabled && !busy));
    });
    this.destroyRef.onDestroy(() => {
      this.pageToken++;
      this.reset$.next();
      this.clearBoxRefresh();
    });
  }

  attach(config: ActivityDraftsConfig): void {
    this.config.set(config);
  }

  /** Yeni sayfa yüklenmeye başladı: taslaklar silinir, süren istekler iptal edilir. */
  resetPage(): void {
    this.pageToken++;
    this.reset$.next();
    this.clearBoxRefresh();
    this.revision = null;
    if (Object.keys(this.drafts()).length > 0) {
      this.drafts.set({});
    }
  }

  draftOf(name: string): ActivityDraft | null {
    return this.drafts()[name] ?? null;
  }

  /** Hata sonrası "Tekrar dene": önbellek kullanılır (force=false, kayıtlı sonuç varsa ücretsiz). */
  retry(name: string): void {
    const block = this.typedBlocks().find((b) => b.key === name);
    if (block && this.enabled() && !this.locked()) {
      this.request([block], false);
    }
  }

  /** "Yeniden çıkar (ücretli)": kayıtlı sonuç yok sayılır, Claude'a yeniden sorulur — önce onay. */
  reExtract(name: string): void {
    if (!this.enabled() || this.locked() || !this.typedBlocks().some((b) => b.key === name)) {
      return;
    }
    const token = this.pageToken;
    const revision = this.revision;
    this.snackBar
      .open(this.tr('reExtractConfirm'), this.tr('reExtractAction'), { duration: 8000 })
      .onAction()
      .subscribe(() => {
        // Onay başka bir sayfada / yeni tespitte gelirse ücretli çağrı yapılmaz.
        const block = this.typedBlocks().find((b) => b.key === name);
        if (token === this.pageToken && revision === this.revision && block && this.enabled() && !this.locked()) {
          this.request([block], true);
        }
      });
  }

  /**
   * "Taslağı uygula": elle yazarlık korunduğu için bekleyen taslağı tuvale yükler — öğretmenin el düzenlemeleri kaybolur,
   * önce onay.
   */
  applyDraft(name: string): void {
    const draft = this.draftOf(name);
    if (!draft || draft.status !== 'ok' || draft.apply !== 'kept' || this.locked()) {
      return;
    }
    const token = this.pageToken;
    this.snackBar
      .open(this.tr('applyConfirm'), this.tr('applyAction'), { duration: 8000 })
      .onAction()
      .subscribe(() => {
        const latest = this.draftOf(name);
        const selector = this.selector();
        if (token !== this.pageToken || latest?.requestId !== draft.requestId || !selector) {
          return;
        }
        const apply = selector.applyInteractionDraft(name, draft.interactionType, draft.result?.plan, { replace: true });
        this.drafts.update((all) =>
          all[name]?.requestId === draft.requestId ? { ...all, [name]: { ...all[name], apply } } : all
        );
      });
  }

  private selector(): ImageSelectorComponent | undefined {
    return this.config()?.selector();
  }

  private locked(): boolean {
    return this.config()?.locked() ?? true;
  }

  /** Tuvaldeki tipli etkinlik blokları (bölge adı + sayfa pikseli kutu + tip). */
  private typedBlocks(): LabelingActivityBlock<BlockInteractionType>[] {
    const selector = this.selector();
    if (!selector) {
      return [];
    }
    return selector.regions().flatMap((region) => {
      const type = selector.getInteractionType(region.name);
      // Issue #583: openText bloğu yapı çıkarımına gönderilmez (sunucu taslak çıkarmaz; varsayılan planla açılır).
      if (selector.getBlockKind(region.name) !== 'activity' || !isDraftableInteractionType(type)) {
        return [];
      }
      return [{ key: region.name, ...roundBox(region), interactionType: type }];
    });
  }

  /**
   * Taslakları tuvalle eşitler: yeni tespitte hepsi silinir (adlar yeniden "Soru 1"den başlar); tipi kalkan / bölgesi
   * silinen bloğun taslağı düşer (aynı adı alan yeni bölge eskisinin taslağını devralmasın); taslağı olmayan ya da tipi
   * değişen bloklar tek istekte sorulur. Kutusu değişen bloğun taslağı bayatlar ve sürükleme bitince yeniden istenir.
   */
  private sync(revision: number, enabled: boolean): void {
    if (revision !== this.revision) {
      this.revision = revision;
      this.reset$.next();
      this.clearBoxRefresh();
      if (Object.keys(this.drafts()).length > 0) {
        this.drafts.set({});
      }
    }
    const blocks = this.typedBlocks();
    const drafts = this.drafts();
    const keys = new Set(blocks.map((b) => b.key));
    const next = { ...drafts };
    let changed = false;
    for (const key of Object.keys(drafts)) {
      if (!keys.has(key)) {
        delete next[key];
        changed = true;
      }
    }
    if (!enabled || !this.selector()?.imageData()) {
      if (changed) {
        this.drafts.set(next);
      }
      return;
    }
    const missing: LabelingActivityBlock<BlockInteractionType>[] = [];
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
      this.drafts.set(next);
    }
    if (moved) {
      this.scheduleBoxRefresh();
    }
    if (missing.length > 0) {
      this.request(missing, false);
    }
  }

  /** Son kutu değişikliğinden `debounceMs` sonra kutusu hâlâ farklı olan bloklar yeniden istenir. */
  private scheduleBoxRefresh(): void {
    this.clearBoxRefresh();
    const token = this.pageToken;
    const revision = this.revision;
    this.boxRefreshTimer = setTimeout(() => {
      this.boxRefreshTimer = null;
      if (token !== this.pageToken || revision !== this.revision || !this.enabled() || this.selector()?.inProgress()) {
        return;
      }
      const drafts = this.drafts();
      const moved = this.typedBlocks().filter((b) => {
        const draft = drafts[b.key];
        return !!draft && draft.interactionType === b.interactionType && !sameBox(draft.box, b);
      });
      // Aynı kırpma (kutu geri alındı) önbellekten, yeni kırpma canlı çıkarılır; force yok.
      this.request(moved, false);
    }, this.debounceMs);
  }

  private clearBoxRefresh(): void {
    if (this.boxRefreshTimer != null) {
      clearTimeout(this.boxRefreshTimer);
      this.boxRefreshTimer = null;
    }
  }

  /**
   * Bloklar en çok 20'lik isteklerle ve SIRAYLA sorulur (sunucu her istekte bütçeyi önceden ayırır; paralel istekler
   * birbirinin bütçesini tüketirdi). Tümü baştan "yükleniyor" olur; bir istek başarısızsa kalanlar gönderilmez, aynı
   * hatayla işaretlenir.
   */
  private request(blocks: LabelingActivityBlock<BlockInteractionType>[], force: boolean): void {
    const image = this.selector()?.imageData();
    const model = this.config()?.model() ?? '';
    if (!image || !model || blocks.length === 0) {
      return;
    }
    const batches: Array<{ blocks: LabelingActivityBlock<BlockInteractionType>[]; requestId: number }> = [];
    for (let i = 0; i < blocks.length; i += MAX_ACTIVITY_BLOCKS_PER_REQUEST) {
      batches.push({ blocks: blocks.slice(i, i + MAX_ACTIVITY_BLOCKS_PER_REQUEST), requestId: ++this.requestSeq });
    }
    this.drafts.update((all) => {
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
    const revision = this.revision;
    const current = () => token === this.pageToken && revision === this.revision;
    const run = (index: number): void => {
      const batch = batches[index];
      if (!batch || !current()) {
        return;
      }
      this.requestBatch(image, model, batch.blocks, batch.requestId, force, current, (error) => {
        if (error === null) {
          run(index + 1);
          return;
        }
        // Kalan istekler gönderilmez (ör. 429 bütçe): aynı mesajla işaretlenir.
        const rest = batches.slice(index + 1);
        if (rest.length > 0) {
          this.fail(rest.flatMap((b) => b.blocks.map((block) => ({ key: block.key, requestId: b.requestId }))), error);
        }
      });
    };
    run(0);
  }

  private requestBatch(
    image: string,
    model: string,
    blocks: LabelingActivityBlock<BlockInteractionType>[],
    requestId: number,
    force: boolean,
    current: () => boolean,
    done: (error: string | null) => void
  ): void {
    this.labelingApi
      .activityStructure({ imageBase64: image, model, force, blocks })
      .pipe(takeUntil(this.reset$), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          if (!current()) {
            return;
          }
          const results = new Map((response?.results ?? []).map((r) => [r.key, r]));
          const selector = this.selector();
          const next = { ...this.drafts() };
          for (const block of blocks) {
            const draft = next[block.key];
            if (!draft || draft.requestId !== requestId) {
              continue;
            }
            const result = results.get(block.key);
            if (!result) {
              next[block.key] = { ...draft, status: 'error', error: this.tr('missingResult') };
            } else if (result.status !== 'ok') {
              next[block.key] = { ...draft, status: 'error', result, error: result.error || this.tr('failed') };
            } else {
              const apply: InteractionDraftApply =
                result.interactionType === block.interactionType && !!selector
                  ? selector.applyInteractionDraft(block.key, block.interactionType, result.plan)
                  : 'invalid';
              next[block.key] = { ...draft, status: 'ok', result, error: null, apply };
            }
          }
          this.drafts.set(next);
          done(null);
        },
        error: (error) => {
          if (!current()) {
            return;
          }
          console.error('question canvas activity-structure failed', error);
          const status = (error as { status?: number })?.status;
          const serverMessage = (error as { error?: { message?: string } })?.error?.message;
          // 429 (saatlik tavan / günlük bütçe) ve diğer hatalar: sunucunun mesajı gösterilir.
          const message = serverMessage || this.tr(status === 503 ? 'unavailable' : 'failed');
          this.fail(blocks.map((b) => ({ key: b.key, requestId })), message);
          this.snackBar.open(message, this.tr('ok'), { duration: 6000 });
          done(message);
        },
      });
  }

  private fail(items: Array<{ key: string; requestId: number }>, message: string): void {
    const next = { ...this.drafts() };
    for (const { key, requestId } of items) {
      const draft = next[key];
      if (draft?.requestId === requestId) {
        next[key] = { ...draft, status: 'error', error: message };
      }
    }
    this.drafts.set(next);
  }

  private tr(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${this.config()?.scope ?? ''}.activityDraft.${key}`, params) ?? '';
  }
}

function sameBox(a: LabelingBankBox, b: LabelingBankBox): boolean {
  return a.x === b.x && a.y === b.y && a.width === b.width && a.height === b.height;
}

function roundBox(box: LabelingBankBox): LabelingBankBox {
  return { x: Math.round(box.x), y: Math.round(box.y), width: Math.round(box.width), height: Math.round(box.height) };
}
