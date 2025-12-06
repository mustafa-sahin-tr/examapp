export interface Answer {
  id: number;
  index: number;
  text: string;
  imageUrl: string;
  x: number;
  y: number;
  width: number;
  height: number;
  isCanvasQuestion: boolean;
  tag?: string; // Backend tag (A,B,C,D,E)
  order?: number; // Backend order (0,1,2,...)
}
