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
  answers: AnswerChoice[];
  passage?: PassageRegion;
  // Optional authoring/solve hint: show passage first, then reveal question.
  showPassageFirst?: boolean;
  imageId: string;
  imageUrl: string;
  exampleAnswer: string | null | undefined;
  isExample: boolean;
  order?: number;
  layoutPlan?: CanvasLayoutPlan;
}

export interface CanvasLayoutPlan {
  layoutClass: string;
  answerColumns: number;
  hasPassage?: boolean;
  overrides?: CanvasLayoutPlanOverrides;
  questionFlex?: number;
  answersFlex?: number;
}

export interface CanvasLayoutPlanOverrides {
  initialScale?: number;
  minScale?: number;
  maxScale?: number;
  answerScale?: number;
  question?: {
    maxHeight?: number;
    maxWidth?: number;
  };
  answers?: {
    maxHeight?: number;
    maxWidth?: number;
  };
}

export type RegionOrAnswerHit =
  | { type: 'answer'; value: { questionIndex: number; answerIndex: number } }
  | { type: 'question'; value: { questionIndex: number; answerIndex: null } }
  | { type: 'passage'; value: { passageId: string } }
  | { type: 'dropzone'; value: { questionIndex: number; dropZoneId: string } }
  | null;
