import { ClassificationSource } from './draws';
import { SubTopic } from './subtopic';

/** issue #471: `GET api/exam/questions/bank` filtreleri. Boş alanlar gönderilmez. */
export interface QuestionBankFilter {
  /** `mine` (varsayılan): yalnız kendi sorularım; `all`: görebildiğim tüm sorular. */
  scope?: 'mine' | 'all';
  gradeId?: number | null;
  subjectId?: number | null;
  topicId?: number | null;
  subTopicIds?: number[];
  minDifficulty?: number | null;
  maxDifficulty?: number | null;
  source?: 'AI' | 'Human' | null;
  /** true: ders + konusu belirli; false: sınıflandırması bitmemiş. */
  classified?: boolean | null;
  hasCorrectAnswer?: boolean | null;
  skip?: number;
  take?: number;
}

export interface QuestionBankItem {
  id: number;
  imageUrl: string | null;
  subjectId: number | null;
  subjectName: string | null;
  topicId: number | null;
  topicName: string | null;
  gradeId: number | null;
  subTopics: Pick<SubTopic, 'id' | 'name' | 'topicId'>[];
  difficultyLevel: number;
  classificationSource: ClassificationSource | null;
  isClassified: boolean;
  hasCorrectAnswer: boolean;
  testCount: number;
  isOwn: boolean;
  createTime: string;
}

export interface QuestionBankPage {
  items: QuestionBankItem[];
  total: number;
  skip: number;
  take: number;
}
