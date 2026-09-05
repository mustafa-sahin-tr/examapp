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
  topics: TaxonomyTopic[];
}

export interface TaxonomyTree {
  subjects: TaxonomySubject[];
  grades: TaxonomyGrade[];
}

export interface School {
  id: number;
  name: string;
  city?: string | null;
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
