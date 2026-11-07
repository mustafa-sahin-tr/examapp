/**
 * Issue #867: son çalışılan işaretleme klasörleri. Klasör tarayıcıda `webkitdirectory` ile seçildiği için tutamak
 * saklanamaz; "Devam et" klasörü yeniden seçtirir, akış işaretlenmiş sayfaları zaten atlar. Burada yalnız ad, konum ve
 * tarih yerelde (localStorage) tutulur. Saf fonksiyonlar — birim testleri doğrudan bunları sınar.
 */

export const RECENT_FOLDERS_STORAGE_KEY = 'labeling.recentFolders';
export const RECENT_FOLDERS_LIMIT = 5;

export interface RecentLabelingFolder {
  /** Seçilen kök klasörün adı (`webkitRelativePath`'in ilk parçası). */
  name: string;
  /** Klasördeki görsel sayısı. */
  total: number;
  /** Ad sırasında son gösterilen sayfanın sırası (1'den); `total` ise klasör bitti. */
  position: number;
  /** Son gösterilen dosyanın sayfa numarası (dosya adından), yoksa `position`. */
  page: number;
  /** ISO zaman. */
  updatedAt: string;
}

/** Klasör seçimindeki dosyaların kök klasör adı; göreli yol yoksa null. */
export function rootFolderName(files: readonly Pick<File, 'webkitRelativePath'>[]): string | null {
  const path = files.find((f) => f.webkitRelativePath)?.webkitRelativePath ?? '';
  const name = path.split('/')[0];
  return path.includes('/') && name ? name : null;
}

/** Klasörü listenin başına alır (aynı adlı eski kaydın yerine), en çok `RECENT_FOLDERS_LIMIT` kayıt. */
export function upsertRecentFolder(
  list: readonly RecentLabelingFolder[],
  entry: RecentLabelingFolder,
): RecentLabelingFolder[] {
  return [entry, ...list.filter((f) => f.name !== entry.name)].slice(0, RECENT_FOLDERS_LIMIT);
}

export function isRecentFolderDone(folder: RecentLabelingFolder): boolean {
  return folder.total > 0 && folder.position >= folder.total;
}

export function parseRecentFolders(raw: string | null): RecentLabelingFolder[] {
  if (!raw) {
    return [];
  }
  try {
    const value: unknown = JSON.parse(raw);
    if (!Array.isArray(value)) {
      return [];
    }
    return value
      .filter(
        (f): f is RecentLabelingFolder =>
          !!f &&
          typeof f.name === 'string' &&
          f.name.length > 0 &&
          Number.isInteger(f.total) &&
          Number.isInteger(f.position) &&
          Number.isInteger(f.page) &&
          typeof f.updatedAt === 'string',
      )
      .slice(0, RECENT_FOLDERS_LIMIT);
  } catch {
    return [];
  }
}
