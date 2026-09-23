/**
 * Admin listelerinde (öğretmen #152, öğrenci #153) okul filtresi değeri:
 * - `'all'`        → filtre yok
 * - `'unassigned'` → okula bağlı olmayanlar (`unassigned=true`)
 * - `number`       → belirli okul (`schoolId`)
 */
export type SchoolFilterValue = 'all' | 'unassigned' | number;

/** "Okulsuz" seçeneğinin `admin.schoolFilter.<anahtar>` etiketi: öğretmen → `unassigned`, öğrenci → `unassignedStudent`. */
export type SchoolFilterUnassignedLabel = 'unassigned' | 'unassignedStudent';

/** Filtre değerini backend query alanlarına çevirir; ikisi asla birlikte dolmaz. */
export function schoolFilterToQuery(value: SchoolFilterValue): { schoolId: number | null; unassigned: boolean } {
  if (value === 'unassigned') return { schoolId: null, unassigned: true };
  if (value === 'all') return { schoolId: null, unassigned: false };
  return { schoolId: value, unassigned: false };
}

/** URL query param'larından filtre değerini okur; geçersiz değer → `'all'`. */
export function schoolFilterFromParams(schoolId: string | null, unassigned: string | null): SchoolFilterValue {
  if (unassigned === 'true') return 'unassigned';
  const id = Number(schoolId);
  return schoolId && Number.isInteger(id) && id > 0 ? id : 'all';
}
