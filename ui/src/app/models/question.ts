import { Answer } from './answer';
import { ClassificationSource } from './draws';
import { Subject } from './subject';
import { SubTopic } from './subtopic';

export interface Passage {
  id: number;
  title: string;
  text: string;
  imageUrl: string;
  x: number;
  y: number;
  width: number;
  height: number;
  isCanvasQuestion: boolean;
}

export interface Question {
  id: number;
  text: string;
  subText?: string;
  imageUrl: string;
  /**
   * issue #365 (S2): sunucunun `imageUrl`'den türetip imzaladığı `question-v2` varyantı (QuestionDto.ImageUrlV2,
   * salt okunur). Görsel adı `question.<ext>` değilse null. İstemci bu adresi kendisi türetmez.
   */
  imageUrlV2?: string | null;
  bookName?: string;
  category: Subject;
  answers: Answer[];
  passage?: Passage;
  showPassageFirst?: boolean;
  practiceCorrectAnswer?: string;
  isExample: boolean;
  subjectId: number;
  topicId: number;
  difficultyLevel?: number;
  subTopics?: SubTopic[];
  correctAnswer?: Answer;
  answerColCount: number;
  interactionType?: string;
  interactionPlan?: string;
  x: number;
  y: number;
  width: number;
  height: number;
  sanitizedHeight?: number;
  isCanvasQuestion: boolean;
  correctAnswerId?: number;
  order?: number;
  classificationSource?: ClassificationSource;
}
