/** Soru bloğu ile şık bloğu arası boşluk (px); SCSS'teki `.layout-planned` ile aynı olmalı. */
export const BLOCK_GAP_PX = 16;
/** Şık kartları arası boşluk (px); SCSS'teki `.layout-planned` ile aynı olmalı. */
export const CHOICE_GAP_PX = 12;
/** Bir kartın ölçeklenmeyen payı: 2 × (padding 8 + kenarlık 2). */
const CARD_CHROME_PX = 20;
/** Kartın dokunma hedefi için en küçük yüksekliği (px). */
const CARD_MIN_HEIGHT_PX = 48;
/** Yuvarlama yüzünden kaydırma çubuğu çıkmasın diye bırakılan pay (px). */
const FIT_MARGIN_PX = 2;
/** Bu genişliğin altında soru hep üstte, şıklar en çok iki sütun olur. */
export const COMPACT_WIDTH_PX = 600;
/** Ölçekleri bu oranda yakın olan düzenler eşit sayılır; sıradaki öncelik kazanır. */
const SCALE_TIE_RATIO = 0.95;
const SCALE_SEARCH_STEPS = 24;

export interface PlanSize {
  width: number;
  height: number;
}

export interface QuestionLayoutInput {
  question: PlanSize;
  answers: PlanSize[];
  /** Kullanılabilir genişlik (px). */
  width: number;
  /** Kullanılabilir yükseklik (px); kaydırılan alanda null: yalnız genişlik sınırlar. */
  height: number | null;
  /** Yükseklik yüzünden bunun altına küçültülmez; içerik kaydırılır. */
  minScale: number;
}

export interface QuestionLayoutPlan {
  arrangement: 'top' | 'side';
  answerColumns: number;
  scale: number;
  /** Seçilen ölçekte içerik yüksekliğe sığıyor mu (yükseklik sınırı yoksa true). */
  fits: boolean;
}

interface Candidate {
  arrangement: 'top' | 'side';
  answerColumns: number;
}

function answerRows(answers: PlanSize[], columns: number): PlanSize[][] {
  const rows: PlanSize[][] = [];
  for (let i = 0; i < answers.length; i += columns) {
    rows.push(answers.slice(i, i + columns));
  }
  return rows;
}

function answersHeightAt(answers: PlanSize[], columns: number, scale: number): number {
  const rows = answerRows(answers, columns);
  const rowsHeight = rows.reduce((sum, row) => {
    const tallest = Math.max(...row.map((answer) => answer.height));
    return sum + Math.max(CARD_MIN_HEIGHT_PX, tallest * scale + CARD_CHROME_PX);
  }, 0);
  return rowsHeight + Math.max(0, rows.length - 1) * CHOICE_GAP_PX;
}

function widthLimitedScale(candidate: Candidate, input: QuestionLayoutInput): number {
  const { question, answers, width } = input;
  const columns = candidate.answerColumns;
  const widestAnswer = Math.max(0, ...answers.map((answer) => answer.width));
  const chrome = columns * CARD_CHROME_PX + (columns - 1) * CHOICE_GAP_PX;

  if (candidate.arrangement === 'side') {
    return (width - BLOCK_GAP_PX - chrome) / (question.width + columns * widestAnswer);
  }
  const answersLimit = widestAnswer > 0 ? (width - chrome) / (columns * widestAnswer) : Number.POSITIVE_INFINITY;
  return Math.min(width / question.width, answersLimit);
}

function contentHeightAt(candidate: Candidate, input: QuestionLayoutInput, scale: number): number {
  const questionHeight = input.question.height * scale;
  const answersHeight = answersHeightAt(input.answers, candidate.answerColumns, scale);
  return candidate.arrangement === 'side'
    ? Math.max(questionHeight, answersHeight)
    : questionHeight + (input.answers.length ? BLOCK_GAP_PX : 0) + answersHeight;
}

function evaluate(candidate: Candidate, input: QuestionLayoutInput): QuestionLayoutPlan {
  const widthScale = Math.min(1, widthLimitedScale(candidate, input));
  if (input.height === null) {
    return { ...candidate, scale: widthScale, fits: true };
  }

  const limit = input.height - FIT_MARGIN_PX;
  if (contentHeightAt(candidate, input, widthScale) <= limit) {
    return { ...candidate, scale: widthScale, fits: true };
  }

  let low = 0;
  let high = widthScale;
  for (let i = 0; i < SCALE_SEARCH_STEPS; i++) {
    const middle = (low + high) / 2;
    if (contentHeightAt(candidate, input, middle) <= limit) {
      low = middle;
    } else {
      high = middle;
    }
  }

  const floor = Math.min(widthScale, input.minScale);
  const scale = Math.max(low, floor);
  return { ...candidate, scale, fits: contentHeightAt(candidate, input, scale) <= limit };
}

/** Öncelik sırasıyla denenecek düzenler: soru üstte, şıklar mümkün olan en çok sütunda. */
function candidatesFor(input: QuestionLayoutInput): Candidate[] {
  const count = Math.max(1, input.answers.length);
  const compact = input.width < COMPACT_WIDTH_PX;
  const columnChoices = (compact ? [2, 1] : [4, 2, 1]).map((columns) => Math.min(columns, count));
  const unique = [...new Set(columnChoices)];

  const top = unique.map((answerColumns): Candidate => ({ arrangement: 'top', answerColumns }));
  if (compact) {
    return top;
  }
  const side = [...new Set([2, 1].map((columns) => Math.min(columns, count)))].map(
    (answerColumns): Candidate => ({ arrangement: 'side', answerColumns }),
  );
  return [...top, ...side];
}

/**
 * Soruyu ve şıkları kaynak piksele değil ekrandaki alana göre yerleştirir: her düzenin sığabildiği
 * en büyük ölçeği hesaplar, küçültmeden önce düzeni değiştirir. Görsel yüklenmesini beklemez,
 * yalnız kayıtlı boyutları kullanır; bu yüzden bir sorunun ilk ve sonraki açılışı aynı görünür.
 */
export function planQuestionLayout(input: QuestionLayoutInput): QuestionLayoutPlan {
  const plans = candidatesFor(input).map((candidate) => evaluate(candidate, input));
  // Düzenin kendi genişlik sınırı alt sınırın altına düşürüyorsa (dar sütunlar) sığması yetmez:
  // okunur ölçeğe ulaşan bir düzen varsa o seçilir. Telefonda hiçbiri ulaşamaz; en büyüğü kalır.
  const largest = Math.max(...plans.map((plan) => plan.scale));
  const readableScale = Math.min(input.minScale, largest) * SCALE_TIE_RATIO;
  const readable = plans.filter((plan) => plan.scale >= readableScale);
  const fitting = readable.filter((plan) => plan.fits);
  if (fitting.length) {
    const best = Math.max(...fitting.map((plan) => plan.scale));
    return fitting.find((plan) => plan.scale >= best * SCALE_TIE_RATIO) ?? fitting[0];
  }
  // Hiçbiri sığmıyorsa en az kaydırma gerektiren düzen.
  const heightOf = (plan: QuestionLayoutPlan) => contentHeightAt(plan, input, plan.scale);
  return readable.reduce((shortest, plan) => (heightOf(plan) < heightOf(shortest) ? plan : shortest));
}
