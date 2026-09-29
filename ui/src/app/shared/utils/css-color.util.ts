/**
 * CSS renk token'larını çalışma zamanında okuyup alfa tonları üretmek için yardımcılar (issue #285).
 *
 * **Yalnız hex desteklenir** (3/4/6/8 hane; projedeki tüm renk token'ları hex): hex girdi `rgba(r, g, b, a)`'ya
 * çevrilir. Diğer her şey (boş token → `currentColor`, `rgb()`, `hsl()`, `color-mix()`, isimli renk vb.)
 * `color-mix(in srgb, <renk> <yüzde>%, transparent)` fallback'ine düşer: token'dan türetilmiş, alfa başına farklı
 * bir ton. ngx-charts'ın d3 skalası bu dizgeyi renk olarak ayrıştıramaz ve string enterpolasyonu yapar; duraklar
 * arasında yalnızca yüzde değeri farklı olduğu için yalnızca o enterpole edilir. SVG `fill` sonucu CSS olarak çözer.
 */

/** Ayrıştırılmış sRGB renk; kanallar 0..255, alfa 0..1. */
export interface RgbaColor {
  r: number;
  g: number;
  b: number;
  a: number;
}

/** Token çözülemediğinde (tanımsız ya da DOM yok) dönen, SVG için güvenli değer. */
export const UNRESOLVED_CSS_COLOR = 'currentColor';

/**
 * `--primaryColor` gibi bir custom property'yi `<html>` üzerinden çözer. Tarayıcı custom property'lerde
 * iç içe `var()` referanslarını hesaplanmış değerde zaten yerine koyar. Token yoksa `currentColor`.
 */
export function readCssToken(name: string): string {
  if (typeof getComputedStyle === 'undefined' || typeof document === 'undefined') {
    return UNRESOLVED_CSS_COLOR;
  }
  const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  return value || UNRESOLVED_CSS_COLOR;
}

const clamp = (value: number, min: number, max: number): number => Math.min(max, Math.max(min, value));

function parseHex(color: string): RgbaColor | null {
  const match = /^#([0-9a-f]{3,4}|[0-9a-f]{6}|[0-9a-f]{8})$/i.exec(color);
  if (!match) {
    return null;
  }
  const raw = match[1];
  const full = raw.length <= 4 ? raw.split('').map((c) => c + c).join('') : raw;
  const channel = (i: number): number => parseInt(full.slice(i * 2, i * 2 + 2), 16);
  return {
    r: channel(0),
    g: channel(1),
    b: channel(2),
    a: full.length === 8 ? channel(3) / 255 : 1,
  };
}

/** Hex rengi (3/4/6/8 hane) sRGB'ye çevirir; hex değilse `null` (yalnız hex desteklenir). */
export function parseCssColor(color: string): RgbaColor | null {
  return parseHex(color.trim());
}

/**
 * Rengi `alpha` opaklığında bir tona çevirir. Kaynak rengin kendi alfası korunur (çarpılır).
 * Ayrıştırılamayan girdi için `color-mix(in srgb, <renk> <yüzde>%, transparent)` döner — alfa başına
 * yine farklı bir ton, token bağı korunur.
 */
export function withAlpha(color: string, alpha: number): string {
  const parsed = parseCssColor(color);
  if (!parsed) {
    const percent = Math.round(clamp(alpha, 0, 1) * 1000) / 10;
    return `color-mix(in srgb, ${color.trim() || UNRESOLVED_CSS_COLOR} ${percent}%, transparent)`;
  }
  const a = Math.round(parsed.a * clamp(alpha, 0, 1) * 1000) / 1000;
  return `rgba(${parsed.r}, ${parsed.g}, ${parsed.b}, ${a})`;
}
