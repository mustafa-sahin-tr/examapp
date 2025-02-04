import { Answer } from "./answer";
import { Category } from "./category";

export interface Question {
    id: number,
    text: string,
    imageUrl: string,
    category: Category,
    answers: Answer[]
}