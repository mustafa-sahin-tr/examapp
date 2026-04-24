export interface AnswerChoice {
  label: string; // "A", "B", "C", "D"
  tag?: string; // Backend tag (A,B,C,D,E)
  order?: number; // Backend order (0,1,2,...)
  x: number;
  y: number;
  width: number;
  height: number;
  isCorrect?: boolean;
  id: number;
  scale?: number;
  imageUrl: string;
}

export interface PassageRegion {
  id: number;
  title: string;
  x: number;
  y: number;
  width: number;
  height: number;
  imageUrl: string;
  imageId: string;
}

export enum ClassificationSource {
  Human = 0,
  AI = 1,
}

export interface QuestionRegion {
  id: number;
  name: string;
  x: number;
  y: number;
  width: number;
  height: number;
  passageId: string;
  // Optional classification metadata (used to sync preview navigation with UI forms)
  subjectId?: number;
  topicId?: number;
  subtopicId?: number;
  // Optional multi-subtopic ids (used for preview classification selector)
  subtopicIds?: number[];
  answers: AnswerChoice[];
  passage?: PassageRegion;
  // Optional authoring/solve hint: show passage first, then reveal question.
  showPassageFirst?: boolean;
  imageId: string;
  imageUrl: string;
  exampleAnswer: string | null | undefined;
  isExample: boolean;
  order?: number;
  classificationSource?: ClassificationSource;
}

export type RegionOrAnswerHit =
  | { type: 'answer'; value: { questionIndex: number; answerIndex: number } }
  | { type: 'question'; value: { questionIndex: number; answerIndex: null } }
  | { type: 'passage'; value: { passageId: string } }
  | { type: 'dropzone'; value: { questionIndex: number; dropZoneId: string } }
  | null;
