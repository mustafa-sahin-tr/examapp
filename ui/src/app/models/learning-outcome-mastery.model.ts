export type MasteryLevel = 'notStarted' | 'familiar' | 'proficient' | 'mastered';

export type MasteryItemKind = 'outcome' | 'subTopic' | 'topic';

/** Issue #696: `LearningOutcomeMasteryItemDto`. Sayılar son cevaplardandır; seviye sunucuda hesaplanır, istemci yeniden üretmez. */
export interface MasteryItem {
  kind: MasteryItemKind;
  learningOutcomeId: number | null;
  code: string | null;
  statement: string | null;
  curriculumVersion: string | null;
  subjectId: number | null;
  subjectName: string | null;
  topicId: number | null;
  topicName: string | null;
  subTopicId: number | null;
  subTopicName: string | null;
  level: MasteryLevel;
  isTentative: boolean;
  answered: number;
  correct: number;
  accuracyPct: number | null;
}

export interface MasteryRules {
  recentAnswers: number;
  proficientPct: number;
  masteredPct: number;
  masteredMinAnswers: number;
  reliableMinAnswers: number;
}

/** Issue #696: `LearningOutcomeMasteryDto`. `items` ders, konu, alt konu, koda göre sıralı gelir. */
export interface LearningOutcomeMastery {
  to: string;
  items: MasteryItem[];
  unclassifiedCount: number;
  rules: MasteryRules;
}
