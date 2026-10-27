/**
 * Issue #576: etkileşim planlarının ortak id kuralı (backend `InteractionIds` ile aynı): `^[A-Za-z0-9_-]{1,32}$`, prototip
 * adları (`__proto__`, `constructor`, `prototype`) yasak. Plan / payload id'leri öğrenciye giden JSON'dan gelir; eşlemeler
 * prototipsiz kurulur ve okuma yalnız kendi özelliğinden yapılır (#574 L2).
 */
export const INTERACTION_ID_PATTERN = /^[A-Za-z0-9_-]{1,32}$/;

export const RESERVED_INTERACTION_IDS: ReadonlySet<string> = new Set(['__proto__', 'constructor', 'prototype']);

export function isValidInteractionId(id: unknown): id is string {
  return typeof id === 'string' && INTERACTION_ID_PATTERN.test(id) && !RESERVED_INTERACTION_IDS.has(id);
}

/** Soru bölgesine göre kutu (görsel pikseli). */
export interface InteractionBox {
  x: number;
  y: number;
  width: number;
  height: number;
}

/** Sonlu sayılardan oluşan, genişliği/yüksekliği pozitif kutu. */
export function isInteractionBox(value: unknown): value is InteractionBox {
  if (!value || typeof value !== 'object') return false;
  const b = value as Record<string, unknown>;
  return (
    ['x', 'y', 'width', 'height'].every((k) => typeof b[k] === 'number' && Number.isFinite(b[k] as number)) &&
    (b['width'] as number) > 0 &&
    (b['height'] as number) > 0
  );
}

/** Kutuyu yalnız dört alanıyla kopyalar (plan anahtar listesi dışında alan gitmesin). */
export function copyBox(box: InteractionBox): InteractionBox {
  return { x: box.x, y: box.y, width: box.width, height: box.height };
}

/** Kırpma kartı / öğe metni: boş ya da yalnız boşluk metin yok sayılır. */
export function cleanLabel(value: unknown): string | null {
  return typeof value === 'string' && value.trim() ? value.trim() : null;
}

/** Tür karşılaştırması büyük/küçük harf duyarsız (#574 L3). */
export function isInteractionType(type: unknown, expected: string): boolean {
  return typeof type === 'string' && type.toLowerCase() === expected.toLowerCase();
}
