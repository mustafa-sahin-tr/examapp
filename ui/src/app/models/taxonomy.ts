export interface TaxonomyGrade {
  id: number;
  name: string;
}

export interface TaxonomySubTopic {
  id: number;
  name: string;
  topicId: number;
  questionCount: number;
}

export interface TaxonomyTopic {
  id: number;
  name: string;
  subjectId: number;
  gradeId: number;
  gradeName?: string;
  subTopics: TaxonomySubTopic[];
}

export interface TaxonomySubject {
  id: number;
  name: string;
  /** GradeSubject üzerinden bağlı sınıf id'leri; boşsa ders hiçbir sınıfa atanmamış. */
  gradeIds: number[];
  topics: TaxonomyTopic[];
}

/** GET api/admin/taxonomy sorgu filtresi — gradeId ve unassigned birlikte kullanılamaz. */
export interface TaxonomyFilter {
  gradeId?: number;
  unassigned?: boolean;
}

export interface TaxonomyTree {
  subjects: TaxonomySubject[];
  grades: TaxonomyGrade[];
}

/** GET api/exam/admin/provinces — tr alfabetik sıralı. */
export interface ProvinceDto {
  id: number;
  name: string;
}

/** GET api/exam/admin/districts?provinceId= — tr alfabetik; bilinmeyen il → []. */
export interface DistrictDto {
  id: number;
  name: string;
  provinceId: number;
}

/** Backend SchoolDto (Issue #91): il/ilçe/açık adres. */
export interface School {
  id: number;
  name: string;
  provinceId: number | null;
  provinceName: string | null;
  districtId: number | null;
  districtName: string | null;
  addressLine: string | null;
}

export interface ApiResult {
  success: boolean;
  message: string;
  objectId?: number;
}

export interface ClassifierCacheStatus {
  cachedContentName?: string | null;
  model?: string | null;
  refreshedAt?: string | null;
  subTopicCount: number;
  configuredInSettings: boolean;
  stale: boolean;
}

export interface ClassifierCacheRefreshResult extends ApiResult {
  cachedContentName?: string | null;
  subTopicCount: number;
  refreshedAt: string;
}
