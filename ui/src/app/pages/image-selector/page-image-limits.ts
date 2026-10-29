/**
 * Issue #597: sunucu sayfa görselini çözerken piksel sınırı uygular (`SkiaImaging.MaxPixels` / `MaxEdge`, exam API);
 * kitap sayfaları (ör. 6405×8000 = 51 MP) bunu aşınca kayıt reddediliyordu. Sınırı aşan sayfa tuvale yüklenirken
 * sınıra sığacak kadar küçültülür; kutular küçültülmüş görsel üzerinde çizildiği için koordinatlar tutarlı kalır.
 * Sınır içindeki sayfalara dokunulmaz (sayfa SHA-256'sı — işaretleme önbelleği, eğitim verisi — değişmesin).
 */
export const PAGE_IMAGE_MAX_PIXELS = 40_000_000;
export const PAGE_IMAGE_MAX_EDGE = 12_000;

/** Sınıra sığan boyut; görsel zaten sığıyorsa `null`. */
export function fitWithinPageLimits(width: number, height: number): { width: number; height: number } | null {
  if (width <= 0 || height <= 0) {
    return null;
  }
  if (width * height <= PAGE_IMAGE_MAX_PIXELS && Math.max(width, height) <= PAGE_IMAGE_MAX_EDGE) {
    return null;
  }
  const scale = Math.min(Math.sqrt(PAGE_IMAGE_MAX_PIXELS / (width * height)), PAGE_IMAGE_MAX_EDGE / Math.max(width, height));
  return { width: Math.max(1, Math.floor(width * scale)), height: Math.max(1, Math.floor(height * scale)) };
}

/**
 * Yüklenmiş görsel sınırı aşıyorsa küçültülmüş data URL'i döner (PNG kaynak PNG kalır, diğerleri JPEG), aşmıyorsa `null`.
 */
export function downscaleOversizedPage(img: HTMLImageElement, dataUrl: string): string | null {
  const size = fitWithinPageLimits(img.naturalWidth, img.naturalHeight);
  if (!size) {
    return null;
  }
  const canvas = document.createElement('canvas');
  canvas.width = size.width;
  canvas.height = size.height;
  const ctx = canvas.getContext('2d');
  if (!ctx) {
    return null;
  }
  ctx.imageSmoothingEnabled = true;
  ctx.imageSmoothingQuality = 'high';
  ctx.drawImage(img, 0, 0, size.width, size.height);
  return dataUrl.startsWith('data:image/png') ? canvas.toDataURL('image/png') : canvas.toDataURL('image/jpeg', 0.92);
}
