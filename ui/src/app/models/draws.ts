export interface AnswerChoice {
  label: string;  // "A", "B", "C", "D"
  x: number;
  y: number;
  width: number;
  height: number;
  isCorrect?: boolean;
  id: number;
}

export interface PassageRegion {
    id: number;
    title: string;
    x: number;
    y: number;
    width: number;
    height: number;
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
}