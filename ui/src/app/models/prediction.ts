import { DRAG_PLACE } from './drag-place';
import { MATCH } from './match';
import { MATRIX_CHOICE } from './matrix-choice';
import { SLOT_FILL } from './slot-fill';
import { TEXT_SELECT } from './text-select';

/**
 * Issue #578: sayfadaki bloğun türü (Claude 1. aşaması). Yoksa / null (YOLO, eski kayıtlar) çoktan seçmeli soru sayılır.
 * `info_box` (konu anlatımı), `answer_key` ve `noise` (optik form, karekod, örnek çözüm …) kırpılmaz: eğitime, bankaya ve
 * teste soru olarak gitmez.
 */
export type BlockKind = 'mcq_question' | 'activity' | 'info_box' | 'answer_key' | 'noise';

export const BLOCK_KINDS: readonly BlockKind[] = ['mcq_question', 'activity', 'info_box', 'answer_key', 'noise'];

/**
 * Issue #578 / epic #569: etkinliğin bileşen ailesi — A Matris seçim, B Cevap kutusu, C Sürükle-yerleştir, D Metin üzerinde
 * işaretleme, E Bölge / boyama, F Izgara tuvali, G Oyun motorları, H Açık uçlu, I Özel simülasyon. Yalnız `activity`'de.
 */
export type ActivityComponent = 'A' | 'B' | 'C' | 'D' | 'E' | 'F' | 'G' | 'H' | 'I';

export const ACTIVITY_COMPONENTS: readonly ActivityComponent[] = ['A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I'];

/** Issue #578: Claude'un etkinliğe yazdığı (kontrolcüsü olan) etkileşim tipi; yalnız `activity`'de. */
export type BlockInteractionType =
    | typeof MATRIX_CHOICE
    | typeof SLOT_FILL
    | typeof DRAG_PLACE
    | typeof MATCH
    | typeof TEXT_SELECT;

const BLOCK_INTERACTION_TYPES: readonly string[] = [MATRIX_CHOICE, SLOT_FILL, DRAG_PLACE, MATCH, TEXT_SELECT];

export interface Prediction {
    class_id: number;
    x: number;
    y: number;
    width: number;
    height: number;
    subpredictions: Prediction[];
    /** Issue #578: yalnız üst düzey kutu; yoksa `mcq_question`. */
    kind?: BlockKind | null;
    /** Issue #578: yalnız `kind = activity`. */
    component?: ActivityComponent | null;
    /** Issue #578: yalnız `kind = activity` ve bileşenle tutarlıysa (bkz. `isConsistentInteraction`). */
    interaction_type?: BlockInteractionType | null;
    /**
     * Issue #581: basılı cevap maskeleri (yalnız üst düzey kutu; kutunun yuvarlanmış sol üst köşesine göre, sayfa pikseli,
     * en çok 20). Manuel kayıtta saklanır, kayıt uygulanınca tuvale geri gelir.
     */
    masks?: PredictionMask[] | null;
}

/** Issue #581: bölgeye göreli maske dikdörtgeni (exam API `RegionBoxDto`). */
export interface PredictionMask {
    x: number;
    y: number;
    width: number;
    height: number;
}

export interface PredictionList {
    success: boolean;
    predictions: Prediction[];
}

/** Kutunun blok türü; bilinmeyen / eksik değer `mcq_question`. */
export function blockKindOf(prediction: Pick<Prediction, 'kind'>): BlockKind {
    const kind = prediction.kind;
    return kind && BLOCK_KINDS.includes(kind) ? kind : 'mcq_question';
}

/** Kırpılmayan türler: soru olarak eğitime, bankaya ya da teste gitmez. */
export function isExcludedBlockKind(kind: BlockKind): boolean {
    return kind === 'info_box' || kind === 'answer_key' || kind === 'noise';
}

export function isActivityComponent(value: unknown): value is ActivityComponent {
    return typeof value === 'string' && (ACTIVITY_COMPONENTS as readonly string[]).includes(value);
}

export function isBlockInteractionType(value: unknown): value is BlockInteractionType {
    return typeof value === 'string' && BLOCK_INTERACTION_TYPES.includes(value);
}

/**
 * Bileşen ↔ etkileşim tipi tutarlılığı (sunucu `LabelingBlocks.IsConsistent`; elle kayıt bunu doğrular): A → matrixChoice,
 * B → slotFill, C → dragPlace | match, D → textSelect (#582). Diğer ailelerin kontrolcüsü yok.
 */
export function isConsistentInteraction(component: ActivityComponent | null, type: string): boolean {
    switch (component) {
        case 'A':
            return type === MATRIX_CHOICE;
        case 'B':
            return type === SLOT_FILL;
        case 'C':
            return type === DRAG_PLACE || type === MATCH;
        case 'D':
            return type === TEXT_SELECT;
        default:
            return false;
    }
}

/**
 * Bileşen elle seçilince kullanılacak etkileşim tipi: A / B / D tek tipli; C'de mevcut dragPlace / match korunur, yoksa
 * dragPlace; E–I ve bileşensiz etkinlik etkileşimsiz (null). Issue #582: D bloğu (eski #578 kayıtlarında tipi null gelse de)
 * textSelect sayılır.
 */
export function interactionForComponent(
    component: ActivityComponent | null,
    current: string | null
): BlockInteractionType | null {
    switch (component) {
        case 'A':
            return MATRIX_CHOICE;
        case 'B':
            return SLOT_FILL;
        case 'C':
            return current === MATCH ? MATCH : DRAG_PLACE;
        case 'D':
            return TEXT_SELECT;
        default:
            return null;
    }
}

/**
 * Issue #581: yalnız verilen türlerdeki blokları bırakır (soru tuvali etkinlik sayfasında: soru + etkinlik). Geçersiz liste
 * olduğu gibi döner.
 */
export function onlyBlockKinds(list: PredictionList, kinds: readonly BlockKind[]): PredictionList {
    if (!list || !Array.isArray(list.predictions)) {
        return list;
    }
    return { ...list, predictions: list.predictions.filter((p) => kinds.includes(blockKindOf(p))) };
}

/**
 * question-detector `GET /labeled-pages`: eğitim veri setine kaydedilmiş sayfa id'leri.
 * Id = sha256(ham görsel baytları).hexdigest()[:16] (`dataset_store.content_id`).
 */
export interface LabeledPagesResponse {
    pageIds: string[];
}
