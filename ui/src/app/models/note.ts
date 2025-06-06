export interface Note {
  id?: number;
  text: string;
  contentId: number;
  userId: number;
  createdAt?: Date;
  updatedAt?: Date;
}
