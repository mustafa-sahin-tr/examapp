export interface AnswerChoice {
  label: string;  // "A", "B", "C", "D"
  x: number;
  y: number;
  width: number;
  height: number;
  isCorrect?: boolean;
}

export interface QuestionRegion {
  name: string;
  x: number;
  y: number;
  width: number;
  height: number;
  answers: AnswerChoice[];
}