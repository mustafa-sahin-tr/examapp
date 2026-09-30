import { CreateAvailabilitySlotRequest, CreateRecurringRuleRequest, RuleDayOfWeek } from '../../../models/booking.model';
import { activeIntlLocale } from '../../utils/active-locale.util';
import { formatSlotRange } from '../../utils/booking-format.util';

/**
 * Grid üzerinde tıkla-seç ile kurulan TASLAK müsaitlik aralığının saf mantığı (issue #176).
 * Kütüphaneden (FullCalendar) ve Angular'dan bağımsızdır. Taslak mutlak anlardan (`Date`) oluşur; geçmiş,
 * çakışma ve süre kontrolleri an bazlıdır. 90 gün ufku backend sözleşmesi gereği UTC gününe göre hesaplanır
 * (bkz. `toSlotRequest`). Issue #300'den beri gün sınırı yoktur: aralık UTC ya da yerel gece yarısını aşabilir.
 */

/** Grid hücresi = FullCalendar `slotDuration` (30 dk). */
export const DRAFT_CELL_MINUTES = 30;

/**
 * Backend üst sınırlarının istemci kopyası (`api/ExamApp.Api/Services/Bookings/BookingService.cs`:
 * `MaxSlotDurationHours = 4`, `MaxAdvanceDays = 90`). Yalnızca erken geri bildirim içindir;
 * asıl doğrulama sunucudadır ve hatası grid'de gösterilir.
 */
export const DRAFT_MAX_MINUTES = 4 * 60;
export const DRAFT_MAX_ADVANCE_DAYS = 90;

const MINUTE_MS = 60_000;
const CELL_MS = DRAFT_CELL_MINUTES * MINUTE_MS;

/** Yarı açık yerel zaman aralığı: [start, end). */
export interface DraftRange {
  start: Date;
  end: Date;
}

/** Tıklamanın taslağa uygulanmama nedeni; `grid.hint.<neden>` çeviri anahtarıyla eşleşir. */
export type DraftRejection = 'past' | 'occupied' | 'tooLong' | 'tooFarAhead';

export interface DraftClickResult {
  /** Tıklama sonrası taslak. Reddedilen tıklamada mevcut taslak AYNEN döner. */
  draft: DraftRange | null;
  rejection: DraftRejection | null;
}

function sameLocalDay(a: Date, b: Date): boolean {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
}

function overlaps(a: DraftRange, b: DraftRange): boolean {
  return a.start.getTime() < b.end.getTime() && a.end.getTime() > b.start.getTime();
}

/**
 * Bir hücre tıklamasını taslağa uygular.
 *
 * Kurallar:
 * - Taslak yoksa → o hücrede yeni 30 dk'lık taslak.
 * - Taslağın DIŞINA tıklama → taslak o hücreyi kapsayacak şekilde genişler. Tıklama başka bir yerel gündeyse
 *   yalnızca birleşik aralık süre sınırına sığıyorsa genişler (gece yarısını aşan aralık: Pzt 23:00 + Sal 00:30);
 *   sığmıyorsa taslak o hücreye TAŞINIR (başka güne yeni taslak).
 * - Taslağın İÇİNE tıklama daraltır: ilk hücre → baştan kısalır; son hücre → sondan kısalır;
 *   ortadaki hücre → taslak o hücrede biter. Tek hücrelik taslağa tıklamak taslağı kaldırır.
 * - Aday aralık geçmişte başlıyorsa, mevcut bir slotla kesişiyorsa (aradaki slot dahil), 4 saati aşıyorsa
 *   ya da 90 günden ilerideyse reddedilir ve mevcut taslak korunur.
 * - Gün sınırı YOKTUR (issue #300): backend `endTime <= startTime`'ı ertesi gün bitiş sayar (`SlotTimeRange`), bu
 *   yüzden UTC gece yarısını (TR'de yerel 03:00) ya da yerel gece yarısını aşan taslak geçerlidir.
 *
 * @param cellStart Tıklanan hücrenin yerel başlangıcı (FullCalendar `dateClick.date`).
 * @param busy Mevcut slotların aralıkları (grid'de göründükleri yerel konumlarıyla).
 */
export function applyCellClick(
  current: DraftRange | null,
  cellStart: Date,
  busy: readonly DraftRange[],
  now: Date
): DraftClickResult {
  const cell: DraftRange = { start: cellStart, end: new Date(cellStart.getTime() + CELL_MS) };
  const reject = (rejection: DraftRejection): DraftClickResult => ({ draft: current, rejection });

  let candidate: DraftRange;
  if (!current || (!sameLocalDay(current.start, cell.start) && !fitsWithCell(current, cell))) {
    candidate = cell;
  } else if (cell.start.getTime() >= current.start.getTime() && cell.end.getTime() <= current.end.getTime()) {
    // Taslağın içi: daralt.
    if (current.end.getTime() - current.start.getTime() <= CELL_MS) {
      return { draft: null, rejection: null };
    }
    if (cell.start.getTime() === current.start.getTime()) {
      candidate = { start: cell.end, end: current.end };
    } else if (cell.end.getTime() === current.end.getTime()) {
      candidate = { start: current.start, end: cell.start };
    } else {
      candidate = { start: current.start, end: cell.end };
    }
  } else {
    candidate = {
      start: new Date(Math.min(current.start.getTime(), cell.start.getTime())),
      end: new Date(Math.max(current.end.getTime(), cell.end.getTime())),
    };
  }

  // Backend kuralı: başlangıç <= şimdi ise geçmiş. Tıklanan hücre değil ADAY aralığın başlangıcı sınanır:
  // beklerken ilk hücresi geçmişe düşen taslağı baştan daraltmak (doğru düzeltme) böylece reddedilmez,
  // geçmişe düşmüş taslağı genişletmek ise reddedilir.
  if (candidate.start.getTime() <= now.getTime()) {
    return reject('past');
  }

  // Backend: isteğin (UTC) günü > UTC bugün + 90 ise reddeder; aynı ufuk UTC gününe göre hesaplanır.
  const horizonEnd = Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() + DRAFT_MAX_ADVANCE_DAYS + 1);
  if (candidate.start.getTime() >= horizonEnd) {
    return reject('tooFarAhead');
  }

  if (busy.some((range) => overlaps(candidate, range))) {
    return reject('occupied');
  }
  if (candidate.end.getTime() - candidate.start.getTime() > DRAFT_MAX_MINUTES * MINUTE_MS) {
    return reject('tooLong');
  }

  return { draft: candidate, rejection: null };
}

/** Taslak + hücre birleşimi süre sınırına sığıyor mu (başka yerel güne tıklamada genişletme mi taşıma mı kararı). */
function fitsWithCell(current: DraftRange, cell: DraftRange): boolean {
  const start = Math.min(current.start.getTime(), cell.start.getTime());
  const end = Math.max(current.end.getTime(), cell.end.getTime());
  return end - start <= DRAFT_MAX_MINUTES * MINUTE_MS;
}

function pad2(value: number): string {
  return `${value}`.padStart(2, '0');
}

/**
 * Taslağı `POST /booking/slots` gövdesine çevirir.
 *
 * Sözleşme: backend `date` + `startTime`/`endTime`'ı UTC kabul eder (`BookingService.ToUtc`) ve `startUtc`/`endUtc`'yi
 * buradan üretir; grid, liste ve öğrenci tarafı bu alanları yerel saate çevirerek gösterir. Bu yüzden tıklanan
 * ANIN UTC duvar saati gönderilir — yerel saat gönderilseydi slot, tıklanan hücreden UTC farkı kadar kayık
 * görünürdü.
 *
 * Gün aşan taslakta (UTC gece yarısını geçen, ör. TR'de yerel 02:30 – 03:30) `endTime` `startTime`'dan küçük olur
 * (23:30 → 00:30) ve `date` başlangıcın UTC günüdür (yerel günden bir önceki gün olabilir); backend bunu ertesi gün
 * bitiş olarak yorumlar (issue #300, `SlotTimeRange`). Eşit saatler (sıfır süre) taslakta oluşamaz (en az 30 dk).
 *
 * Ön koşul: anlar 30 dk'lık hücreye hizalıdır (`applyCellClick` çıktısı böyledir). `draft` dışarıdan da
 * yazılabildiği için hizasız girdi hata vermez; saniye ve milisaniye atılır, saat/dakika olduğu gibi gönderilir.
 */
export function toSlotRequest(draft: DraftRange): CreateAvailabilitySlotRequest {
  return {
    date: utcDateOnly(draft.start),
    startTime: utcTimeOnly(draft.start),
    endTime: utcTimeOnly(draft.end),
  };
}

function utcDateOnly(d: Date): string {
  return `${d.getUTCFullYear()}-${pad2(d.getUTCMonth() + 1)}-${pad2(d.getUTCDate())}`;
}

function utcTimeOnly(d: Date): string {
  return `${pad2(d.getUTCHours())}:${pad2(d.getUTCMinutes())}:00`;
}

/** Tekrarlayan kuralda bitiş gününün taslak gününe göre en az kaç gün sonra olabileceği (ilk tekrar). */
export const RECURRING_UNTIL_MIN_DAYS = 7;
/** Backend `RecurringAvailabilityService.MaxRuleSpanYears`: bitiş en fazla başlangıç + 1 yıl. */
export const RECURRING_MAX_SPAN_YEARS = 1;

/** Datepicker sınırları: yerel gece yarısı anları (Material `min`/`max` yerel günle karşılaştırır). */
export interface RecurringUntilBounds {
  min: Date;
  max: Date;
}

/**
 * "Şu tarihe kadar tekrarla" seçicisinin sınırları: en erken taslak günü + 7 (ilk tekrar), en geç taslak günü + 1 yıl
 * eksi 1 gün. Bir gün pay bırakılır çünkü backend sınırı UTC gününe göredir ve yaz saati geçişi UTC gününü bir gün
 * kaydırabilir; sınırın kendisi zaten sunucuda da doğrulanır.
 */
export function recurringUntilBounds(draft: DraftRange): RecurringUntilBounds {
  const s = draft.start;
  return {
    min: new Date(s.getFullYear(), s.getMonth(), s.getDate() + RECURRING_UNTIL_MIN_DAYS),
    max: new Date(s.getFullYear() + RECURRING_MAX_SPAN_YEARS, s.getMonth(), s.getDate() - 1),
  };
}

/**
 * Taslağı `POST /booking/recurring-rules` gövdesine çevirir (issue #179).
 *
 * `toSlotRequest` ile aynı sözleşme: gün, saat ve `effectiveFrom` tıklanan anın UTC bileşenleridir; `dayOfWeek`
 * de UTC günüdür (`getUTCDay()`, 0=Pazar — backend `System.DayOfWeek` ile aynı). Yerel gün gönderilseydi gece
 * yarısına yakın bir taslak (TR'de yerel 00:00–03:00) bir gün kayık kurala dönüşürdü.
 *
 * `until` datepicker'dan gelen yerel gündür (gece yarısı anı). Kullanıcı "bu haftaya kadar" derken grid'de
 * gördüğü yerel günü kasteder; bu yüzden bitiş, o yerel günde taslağın kendi saatinde gerçekleşecek tekrarın
 * UTC günü olarak hesaplanır — böylece seçilen gün her zaman seriye dahildir. Null = süresiz.
 *
 * Gün aşan taslakta `endTime < startTime` gider; backend her tekrarın bitişini ertesi güne koyar (issue #300).
 */
export function toRecurringRuleRequest(draft: DraftRange, until: Date | null): CreateRecurringRuleRequest {
  const s = draft.start;
  const untilAtDraftTime = until
    ? new Date(until.getFullYear(), until.getMonth(), until.getDate(), s.getHours(), s.getMinutes())
    : null;
  return {
    dayOfWeek: s.getUTCDay() as RuleDayOfWeek,
    startTime: utcTimeOnly(s),
    endTime: utcTimeOnly(draft.end),
    effectiveFrom: utcDateOnly(s),
    effectiveUntil: untilAtDraftTime ? utcDateOnly(untilAtDraftTime) : null,
  };
}

/** Onay çubuğu metni: "Cum 25 Eyl · 14:00 – 15:30" (aktif dil, yerel saat). */
export function formatDraftLabel(draft: DraftRange): string {
  const day = new Intl.DateTimeFormat(activeIntlLocale(), { weekday: 'short', day: 'numeric', month: 'short' }).format(
    draft.start
  );
  return `${day} · ${formatSlotRange(draft.start.toISOString(), draft.end.toISOString())}`;
}
