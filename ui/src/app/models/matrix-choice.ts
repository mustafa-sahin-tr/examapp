/**
 * Issue #574: matris seçim etkileşimi (`InteractionType = matrixChoice`) — "satır × seçenek" kalıbı (D/Y tabloları, ✓ ile
 * sınıflandırma, çoklu seçim, <, >, =). Plan öğrenciye gider, çözüm gitmez (#570): çözüm ayrı alanda
 * (`interactionSolution`) yalnız yetkili/izinli durumda gelir. Kutu koordinatları soru bölgesine göredir (sürükle-bırak
 * `dropZones` ile aynı kural). Puansız: kontrol satır başına doğru/yanlış döner.
 */
export const MATRIX_CHOICE = 'matrixChoice' as const;

/** Backend sözleşmesinin sınırları (plan doğrulaması). */
export const MATRIX_CHOICE_LIMITS = {
  minColumns: 2,
  maxColumns: 10,
  minRows: 1,
  maxRows: 60,
  maxIdLength: 32,
  maxLabelLength: 200,
} as const;

export interface MatrixBox {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface MatrixChoiceColumn {
  id: string;
  label: string;
}

export interface MatrixChoiceRow {
  id: string;
  /** İsteğe bağlı satır metni (yoksa oynatıcı "Satır n" yazar ve satır kutusunu görselde vurgular). */
  label?: string | null;
  /** Satır kutusu (soru bölgesine göre). Issue #590: görselsiz yazarlıkta yok (etiket zorunlu). */
  box?: MatrixBox | null;
  /** İsteğe bağlı hücre kutuları: kolon id → kutu. Tüm satırlarda tüm kolonlar varsa oynatıcı görsel üstünde dokundurur. */
  cells?: Record<string, MatrixBox> | null;
}

export interface MatrixChoicePlanV1 {
  version: 1;
  type: typeof MATRIX_CHOICE;
  multiPerRow: boolean;
  /** Issue #590: oynatıcı satırları karışık sırayla gösterir (kontrol sırası plan sırası kalır). */
  shuffleRows?: boolean;
  columns: MatrixChoiceColumn[];
  rows: MatrixChoiceRow[];
}

/** Satır id → seçilen kolon id'leri (çözüm ve öğrenci seçimi aynı biçimde). */
export type MatrixSelections = Record<string, string[]>;

/** Öğrenci cevabı (`answerPayload`). Boş `selections` boş cevap sayılır. */
export interface MatrixChoiceAnswerPayload {
  version: 1;
  type: typeof MATRIX_CHOICE;
  selections: MatrixSelections;
}

/** Yazarlıkta hızlı kolon şablonları. */
export type MatrixColumnPreset = 'trueFalse' | 'checkTable' | 'compare';

export const MATRIX_COLUMN_PRESETS: Record<MatrixColumnPreset, { labels: string[]; multiPerRow: boolean }> = {
  trueFalse: { labels: ['D', 'Y'], multiPerRow: false },
  checkTable: { labels: ['1', '2', '3'], multiPerRow: false },
  compare: { labels: ['<', '>', '='], multiPerRow: false },
};

/**
 * Güvenlik (#574 L2): id'ler öğrenciye giden plandan/payload'dan gelir; `constructor`, `__proto__` gibi id'ler düz nesnede
 * prototipe çarpmasın diye eşlemeler prototipsiz kurulur ve okuma yalnız kendi özelliğinden yapılır.
 */
export function emptyMatrixSelections(): MatrixSelections {
  return Object.create(null) as MatrixSelections;
}

/** Satırın seçimi (yalnız kendi özelliği; yoksa boş dizi). */
export function selectionOf(selections: MatrixSelections, rowId: string): string[] {
  return Object.hasOwn(selections, rowId) && Array.isArray(selections[rowId]) ? selections[rowId] : [];
}

/** Satırın seçimini değiştirilmiş yeni (prototipsiz) eşleme; boş dizi satırı siler. */
export function withSelection(selections: MatrixSelections, rowId: string, columns: string[]): MatrixSelections {
  const next = emptyMatrixSelections();
  Object.keys(selections).forEach((key) => {
    if (key !== rowId) next[key] = selections[key];
  });
  if (columns.length > 0) next[rowId] = columns;
  return next;
}

/** Hücre kutusu (yalnız kendi özelliği). */
export function cellOf(row: MatrixChoiceRow, columnId: string): MatrixBox | null {
  const cells = row.cells;
  return cells && Object.hasOwn(cells, columnId) ? cells[columnId] : null;
}

function isBox(value: unknown): value is MatrixBox {
  if (!value || typeof value !== 'object') return false;
  const b = value as Record<string, unknown>;
  return ['x', 'y', 'width', 'height'].every((k) => typeof b[k] === 'number' && Number.isFinite(b[k] as number));
}

/** Plan JSON'unu güvenli ayrıştırır; tür farklı / bozuk / kolon-satır yoksa null. */
export function parseMatrixChoicePlan(json: string | null | undefined): MatrixChoicePlanV1 | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Partial<MatrixChoicePlanV1> | null;
    if (!parsed || !isMatrixChoiceType(parsed.type)) return null;
    if (!Array.isArray(parsed.columns) || !Array.isArray(parsed.rows)) return null;
    const columns = parsed.columns
      .filter((c): c is MatrixChoiceColumn => !!c && typeof c.id === 'string' && !!c.id)
      .map((c) => ({ id: c.id, label: typeof c.label === 'string' ? c.label : c.id }));
    const columnIds = new Set(columns.map((c) => c.id));
    const rows = parsed.rows
      .filter((r): r is MatrixChoiceRow => !!r && typeof r.id === 'string' && !!r.id)
      .map((r) => {
        const cells = Object.create(null) as Record<string, MatrixBox>;
        if (r.cells && typeof r.cells === 'object') {
          Object.entries(r.cells).forEach(([columnId, box]) => {
            if (columnIds.has(columnId) && isBox(box)) cells[columnId] = box;
          });
        }
        return {
          id: r.id,
          label: typeof r.label === 'string' && r.label.trim() ? r.label : null,
          box: isBox(r.box) ? r.box : null,
          cells,
        };
      });
    if (columns.length === 0 || rows.length === 0) return null;
    return {
      version: 1,
      type: MATRIX_CHOICE,
      multiPerRow: parsed.multiPerRow === true,
      shuffleRows: parsed.shuffleRows === true,
      columns,
      rows,
    };
  } catch {
    return null;
  }
}

/** `{rowId: [colId]}` nesnesini temizler (yalnız string dizileri; plan verilirse plandaki satır/kolonlar). */
export function normalizeMatrixSelections(value: unknown, plan?: MatrixChoicePlanV1 | null): MatrixSelections {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return emptyMatrixSelections();
  const rowIds = plan ? new Set(plan.rows.map((r) => r.id)) : null;
  const columnIds = plan ? new Set(plan.columns.map((c) => c.id)) : null;
  const result = emptyMatrixSelections();
  Object.entries(value as Record<string, unknown>).forEach(([rowId, cols]) => {
    if (rowIds && !rowIds.has(rowId)) return;
    if (!Array.isArray(cols)) return;
    const unique = Array.from(new Set(cols.filter((c): c is string => typeof c === 'string' && (!columnIds || columnIds.has(c)))));
    if (unique.length > 0) result[rowId] = unique;
  });
  return result;
}

/** Çözüm JSON'u (`{"r1":["c2"]}`) — bozuk / boşsa `{}`. */
export function parseMatrixChoiceSolution(json: string | null | undefined, plan?: MatrixChoicePlanV1 | null): MatrixSelections {
  if (!json) return emptyMatrixSelections();
  try {
    return normalizeMatrixSelections(JSON.parse(json), plan);
  } catch {
    return emptyMatrixSelections();
  }
}

/** Öğrenci cevabından seçimler (`{"type":"matrixChoice","selections":{...}}`) — başka tür / bozuksa `{}`. */
export function parseMatrixChoiceSelections(json: string | null | undefined, plan?: MatrixChoicePlanV1 | null): MatrixSelections {
  if (!json) return emptyMatrixSelections();
  try {
    const parsed = JSON.parse(json) as Partial<MatrixChoiceAnswerPayload> | null;
    if (!parsed || !isMatrixChoiceType(parsed.type)) return emptyMatrixSelections();
    return normalizeMatrixSelections(parsed.selections, plan);
  } catch {
    return emptyMatrixSelections();
  }
}

export function buildMatrixChoicePayload(selections: MatrixSelections): string {
  const payload: MatrixChoiceAnswerPayload = { version: 1, type: MATRIX_CHOICE, selections };
  return JSON.stringify(payload);
}

/** Tüm satırlarda tüm kolonlar için hücre kutusu var mı (oynatıcı görsel üstü dokunma modunu bununla seçer). */
export function matrixHasAllCells(plan: MatrixChoicePlanV1 | null | undefined): boolean {
  if (!plan || plan.rows.length === 0) return false;
  return plan.rows.every((row) => plan.columns.every((col) => !!cellOf(row, col.id)));
}

/** Tür karşılaştırması büyük/küçük harf duyarsız (#574 L3): `MatrixChoice` de matris sayılır. */
export function isMatrixChoiceType(type: unknown): boolean {
  return typeof type === 'string' && type.toLowerCase() === MATRIX_CHOICE.toLowerCase();
}

/** Seçimsiz matris cevabı (Temizle / son seçimi kaldırma): sunucu boş cevap sayar, ekranlar da cevapsız saymalı. */
export function isBlankMatrixChoicePayload(json: string | null | undefined): boolean {
  if (!json) return true;
  try {
    const parsed = JSON.parse(json) as Partial<MatrixChoiceAnswerPayload> | null;
    if (!parsed || !isMatrixChoiceType(parsed.type)) return false;
    return Object.keys(normalizeMatrixSelections(parsed.selections)).length === 0;
  } catch {
    return false;
  }
}
