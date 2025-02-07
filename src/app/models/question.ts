import { Answer } from "./answer";
import { Subject } from "./subject";

export interface Question {
    id: number,
    text: string,
    subText?: string,
    imageUrl: string,
    category: Subject,
    answers: Answer[]
}