/** Issue #497: `TopicPerformanceItemDto`. Yüzdeler sunucuda hesaplanır, istemci yeniden üretmez. */
export interface TopicPerformanceItem {
  subjectId: number | null;
  subjectName: string | null;
  topicId: number | null;
  topicName: string | null;
  subTopicId: number | null;
  subTopicName: string | null;
  correct: number;
  wrong: number;
  blank: number;
  total: number;
  /** Doğru / (doğru + yanlış); boşlar girmez. */
  accuracyPct: number;
}

/** Issue #531: `TopicProgressWeekDto`. `accuracyPct` cevapsız haftada boştur. */
export interface TopicProgressWeek {
  /** Haftanın Pazartesi'si, `YYYY-MM-DD`. */
  weekStart: string;
  answered: number;
  correct: number;
  accuracyPct: number | null;
  lowSample: boolean;
}

/** Issue #531: `TopicProgressTimelineDto`. Eskiden yeniye. */
export interface TopicProgressTimeline {
  topicId: number | null;
  subTopicId: number | null;
  weeks: TopicProgressWeek[];
}

/** Issue #497: `TopicPerformanceDto`. `items` en düşük doğruluk önce gelir. */
export interface TopicPerformance {
  from: string;
  to: string;
  groupBy: string;
  items: TopicPerformanceItem[];
  unclassifiedCount: number;
}
