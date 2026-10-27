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
  /** true: en az bir teste bağlı; false: hiçbir teste bağlı değil; null: farketmez. */
  inTest?: boolean | null;
  /** Issue #580: `pending` — onaylanmamış etkinlik çözümü olan sorular. */
  solutionStatus?: QuestionSolutionStatus | null;
  skip?: number;
  take?: number;
}

/**
 * Issue #580: sorunun çözüm onay durumu ve kaynağı. Elle yazılan (ve çözümsüz) sorular `approved`; cevap anahtarından / AI
 * gelen çözüm Admin ya da QuestionTrainer onaylayana kadar `pending`.
 */
export type QuestionSolutionStatus = 'pending' | 'approved';
export type QuestionSolutionSource = 'None' | 'Human' | 'Key' | 'AI';

/** `POST api/exam/questions/{id}/solution/approve` yanıtı. */
export interface QuestionSolutionState {
  questionId: number;
  solutionSource: QuestionSolutionSource;
  solutionStatus: QuestionSolutionStatus;
  solutionPromptVersion: number | null;
  solutionApprovedByName: string | null;
  solutionApprovedAt: string | null;
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
  /** issue #472: "Havuzdan ekle" ile teste bağlanabilir mi (kendi sorusu ya da sahibi belli soru). */
  canLink: boolean;
  createTime: string;
  /** Issue #580: çözüm onay durumu / kaynağı / üreten talimat sürümü (eski yanıtta yok). */
  solutionStatus?: QuestionSolutionStatus | null;
  solutionSource?: QuestionSolutionSource | null;
  solutionPromptVersion?: number | null;
}

export interface QuestionBankPage {
  items: QuestionBankItem[];
  total: number;
  skip: number;
  take: number;
}

/** issue #472: `POST api/exam/questions/test/{testId}/bank-questions` yanıtı. */
export interface LinkBankQuestionsResult {
  addedQuestionIds: number[];
  alreadyInTestQuestionIds: number[];
}
