import { Answer } from "./answer";
import { Subject } from "./subject";


export interface Passage {
    id: number,
    title: string,
    text: string,
    imageUrl: string,
}

export interface Question {
    id: number,
    text: string,
    subText?: string,
    imageUrl: string,
    category: Subject,
    answers: Answer[],
    passage?: Passage,
    practiceCorrectAnswer?: string,
    isExample: boolean,
    subjectId: number,
    topicId: number,
    subtTopicId: number,
    correctAnswer: number,
}