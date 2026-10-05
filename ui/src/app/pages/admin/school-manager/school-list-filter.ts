import { ParamMap, Params } from '@angular/router';
import { DistrictDto, ProvinceDto, School } from '../../../models/taxonomy';
import { parsePositiveInt } from '../../../shared/utils/paging.util';

/**
 * Issue #281 — Admin okul listesinin istemci tarafı filtresi. `GET api/admin/schools` sayfalamasız tam
 * liste döndüğü için ad / il / ilçe süzmesi tarayıcıda yapılır. Mantık saf fonksiyonlardadır ki
 * komponentten bağımsız birim test edilebilsin.
 */
export interface SchoolListFilter {
  /** Ad araması (ham metin; karşılaştırma `normalizeSearchText` ile yapılır). */
  q: string;
  provinceId: number | null;
  /** Yalnız `provinceId` doluyken anlamlıdır. */
  districtId: number | null;
}

export const EMPTY_SCHOOL_LIST_FILTER: SchoolListFilter = { q: '', provinceId: null, districtId: null };

/** URL'e yazılan query param adları (öğretmen/öğrenci listelerindeki `schoolId` ile çakışmaz). */
export const SCHOOL_LIST_QUERY_KEYS = { q: 'q', provinceId: 'provinceId', districtId: 'districtId' } as const;

/** Elle yazılmış absürt `?q=` değerleri için üst sınır. */
export const MAX_SEARCH_LENGTH = 200;

/**
 * Büyük/küçük harf ve Türkçe karakter duyarsız karşılaştırma anahtarı.
 *
 * - `tr-TR` küçültme: `İ → i`, `I → ı` (genel `toLowerCase` `İ`'yi `i̇` yapar ve `I`'yı `i` yapar — tr metinde yanlış).
 * - Ardından `ı → i` katlanır: "ILKOKUL" (→ "ılkokul") ile "ilkokul" eşleşir.
 * - NFD + birleşik işaret silme: `ç/ş/ğ/ö/ü → c/s/g/o/u` ve olası `i̇` noktası; "cankaya" "Çankaya"yı bulur.
 * - Boşluklar tek boşluğa indirilir ve kırpılır.
 */
export function normalizeSearchText(value: string | null | undefined): string {
  if (!value) return '';
  return value
    .toLocaleLowerCase('tr-TR')
    .replace(/ı/g, 'i')
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/\s+/g, ' ')
    .trim();
}

/** Ad araması, il ve ilçe birlikte (AND) uygulanır; boş kriter elemez. Sıra korunur. */
export function filterSchools(schools: readonly School[], filter: SchoolListFilter): School[] {
  const needle = normalizeSearchText(filter.q);
  return schools.filter(
    (sc) =>
      (!needle || normalizeSearchText(sc.name).includes(needle)) &&
      (filter.provinceId == null || sc.provinceId === filter.provinceId) &&
      (filter.districtId == null || sc.districtId === filter.districtId),
  );
}

/** Herhangi bir kriter dolu mu (boşluktan ibaret arama sayılmaz). */
/** URL'de geçerli bir okul filtre param'ı var mı (admin-home'un Okullar sekmesini açması için, #281). */
export function hasSchoolListFilterParams(params: ParamMap): boolean {
  return isSchoolListFilterActive(schoolListFilterFromParams(params));
}

export function isSchoolListFilterActive(filter: SchoolListFilter): boolean {
  return !!normalizeSearchText(filter.q) || filter.provinceId != null || filter.districtId != null;
}

/**
 * URL query param'larından filtreyi okur. Geçersiz değerler yok sayılır: sayı olmayan/pozitif olmayan
 * id → null; il yokken ilçe → null; aşırı uzun arama kırpılır.
 */
export function schoolListFilterFromParams(params: ParamMap): SchoolListFilter {
  const q = (params.get(SCHOOL_LIST_QUERY_KEYS.q) ?? '').slice(0, MAX_SEARCH_LENGTH);
  const provinceId = parseId(params.get(SCHOOL_LIST_QUERY_KEYS.provinceId));
  const districtId = provinceId == null ? null : parseId(params.get(SCHOOL_LIST_QUERY_KEYS.districtId));
  return { q, provinceId, districtId };
}

/** Filtreyi `queryParamsHandling: 'merge'` ile yazılacak param'lara çevirir; boş değer → null (URL'den silinir). */
export function schoolListFilterToParams(filter: SchoolListFilter): Params {
  const q = filter.q.trim();
  return {
    [SCHOOL_LIST_QUERY_KEYS.q]: q ? q : null,
    [SCHOOL_LIST_QUERY_KEYS.provinceId]: filter.provinceId,
    [SCHOOL_LIST_QUERY_KEYS.districtId]: filter.provinceId == null ? null : filter.districtId,
  };
}

/**
 * URL'den gelen il/ilçe id'lerini yüklü listelere karşı doğrular: liste yüklüyse ve id içinde yoksa
 * (ör. elle yazılmış `?provinceId=9999`) kriter yok sayılır. Liste henüz yüklenmemişse id olduğu gibi kalır.
 *
 * @param districts il id'si → ilçeleri; ilin anahtarı yoksa ilçeleri henüz yüklenmemiştir.
 */
export function sanitizeSchoolListFilter(
  filter: SchoolListFilter,
  provinces: readonly ProvinceDto[],
  districts: Readonly<Record<number, readonly DistrictDto[]>>,
): SchoolListFilter {
  let { provinceId, districtId } = filter;
  if (provinceId != null && provinces.length > 0 && !provinces.some((p) => p.id === provinceId)) {
    provinceId = null;
  }
  if (provinceId == null) {
    districtId = null;
  } else if (districtId != null && provinceId in districts) {
    if (!districts[provinceId].some((d) => d.id === districtId)) districtId = null;
  }
  return { q: filter.q, provinceId, districtId };
}

function parseId(raw: string | null): number | null {
  const id = parsePositiveInt(raw, 0, Number.MAX_SAFE_INTEGER);
  return id > 0 ? id : null;
}
