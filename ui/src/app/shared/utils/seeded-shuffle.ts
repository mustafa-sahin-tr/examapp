/**
 * Issue #576: tohumlu, kararlı karıştırma (Fisher–Yates + mulberry32). Aynı tohum ve aynı liste her çizimde aynı sırayı
 * verir; kart / sağ öğe sırası plan sırasını (ve dolayısıyla cevabı) ele vermesin diye istemcide de karıştırılır.
 * Güvenlik özelliği değildir (sunucu da kayıtta karıştırır), yalnız derinlemesine savunma.
 */
export function seededShuffle<T>(items: readonly T[], seed: string): T[] {
  const result = [...items];
  const random = mulberry32(hashString(seed));
  for (let i = result.length - 1; i > 0; i--) {
    const j = Math.floor(random() * (i + 1));
    [result[i], result[j]] = [result[j], result[i]];
  }
  // Karıştırma plan sırasını aynen verdiyse (4 öğede 1/24) öğrenci "karışmadı" sanır ve sıra cevabı ele verebilir:
  // bir adım döndür. Kararlılık korunur (aynı tohum → aynı sıra).
  if (result.length > 1 && result.every((item, index) => item === items[index])) {
    result.push(result.shift()!);
  }
  return result;
}

function hashString(value: string): number {
  let h = 2166136261;
  for (let i = 0; i < value.length; i++) {
    h ^= value.charCodeAt(i);
    h = Math.imul(h, 16777619);
  }
  return h >>> 0;
}

function mulberry32(seed: number): () => number {
  let a = seed;
  return () => {
    a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
