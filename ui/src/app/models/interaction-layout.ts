/**
 * Issue #590 (dilim b): görselsiz plan düzeni (`layout`) — kutu koordinatı yerine öğelerin dizilişi. Backend
 * `InteractionLayout` ile aynı sözleşme: `list` (her öğe kendi etiketiyle satır), `text` (satır içi metin; `[[id]]` öğenin
 * yeri), `table` (hücreler düz metin; `[[id]]` içeren hücre cevap kutusu). Düzen metninde çözüm yoktur.
 */
export type LayoutKind = 'list' | 'text' | 'table';

export interface ListLayout {
  kind: 'list';
}

export interface TextLayout {
  kind: 'text';
  text: string;
}

export interface TableLayout {
  kind: 'table';
  header?: string[] | null;
  rows: string[][];
}

export type InteractionLayout = ListLayout | TextLayout | TableLayout;

/** Backend sınırları (`InteractionLayout`). */
export const LAYOUT_LIMITS = {
  maxTextLength: 4000,
  maxTableRows: 30,
  maxTableColumns: 8,
  maxCellLength: 200,
} as const;

/** Yer tutucu: `[[id]]`. */
export const LAYOUT_PLACEHOLDER = /\[\[([A-Za-z0-9_-]{1,32})\]\]/g;

/** Metin parçası: düz metin ya da öğe (kutu / hedef) yeri. */
export type LayoutSegment = { text: string; id?: undefined } | { id: string; text?: undefined };

/** Metni düz metin + yer tutucu parçalarına böler (boş düz parça atlanır). */
export function splitPlaceholders(text: string): LayoutSegment[] {
  const segments: LayoutSegment[] = [];
  let last = 0;
  for (const match of text.matchAll(LAYOUT_PLACEHOLDER)) {
    const index = match.index ?? 0;
    if (index > last) segments.push({ text: text.slice(last, index) });
    segments.push({ id: match[1] });
    last = index + match[0].length;
  }
  if (last < text.length) segments.push({ text: text.slice(last) });
  return segments;
}

function stringRow(value: unknown): string[] | null {
  return Array.isArray(value) && value.every((cell) => typeof cell === 'string') ? (value as string[]) : null;
}

/** Plandaki `layout` değerini toleranslı okur; izinli tür dışı / bozuk = null. */
export function parseLayout(value: unknown, kinds: readonly LayoutKind[]): InteractionLayout | null {
  if (!value || typeof value !== 'object') return null;
  const raw = value as Record<string, unknown>;
  const kind = raw['kind'];
  if (typeof kind !== 'string' || !kinds.includes(kind as LayoutKind)) return null;
  switch (kind) {
    case 'list':
      return { kind: 'list' };
    case 'text':
      return typeof raw['text'] === 'string' ? { kind: 'text', text: raw['text'] } : null;
    case 'table': {
      const rows = Array.isArray(raw['rows']) ? raw['rows'].map(stringRow) : [];
      if (rows.length === 0 || rows.some((r) => r === null)) return null;
      const header = stringRow(raw['header']);
      return { kind: 'table', header, rows: rows as string[][] };
    }
    default:
      return null;
  }
}
