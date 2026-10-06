/**
 * MinIO görsel adresleri için imza-güvenli yardımcılar (issue #365 S2).
 *
 * API yanıtındaki görsel adresleri artık imzalı `/img/{bucket}/{key}?X-Amz-...` URL'leridir. Bu adreslerin
 * yolu veya query'si istemcide değiştirilemez (imza bozulur) ve aynı nesne her yanıtta farklı bir imzayla
 * gelebilir. Bu yüzden:
 * - varyant (ör. `question-v2`) URL'si istemcide türetilmez, sunucunun imzaladığı alan kullanılır;
 * - önbellek/kimlik anahtarı olarak tam URL değil, query/hash'siz yol kullanılır.
 */

/**
 * Görselin kalıcı kimliği: query ve hash atılmış adres. Aynı nesnenin farklı imzalı URL'leri aynı anahtarı
 * üretir; önbellek anahtarı ve "görsel değişti mi" karşılaştırması için kullanılır, `src` olarak KULLANILMAZ.
 */
export function storageImageKey(url: string | null | undefined): string {
  if (!url) {
    return '';
  }
  // `data:` URL'lerinde base64 gövdesi `?`/`#` içermez; blob/http(s)/göreli adresler query'den itibaren kesilir.
  return url.split(/[?#]/)[0];
}

/** Canvas soru görünümünün göstereceği soru görseli: sunucunun imzaladığı v2 varyantı, yoksa asıl görsel. */
export function questionCanvasImageUrl(
  region: { imageUrl?: string | null; imageUrlV2?: string | null } | null | undefined
): string | null {
  const v2 = region?.imageUrlV2?.trim();
  if (v2) {
    return v2;
  }
  const original = region?.imageUrl?.trim();
  return original ? original : null;
}
