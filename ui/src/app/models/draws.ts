export interface AnswerChoice {
  label: string; // "A", "B", "C", "D"
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
  answers: AnswerChoice[];
  passage?: PassageRegion;
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
  | null;
