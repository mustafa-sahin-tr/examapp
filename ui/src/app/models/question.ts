import { Answer } from "./answer";
import { Subject } from "./subject";


export interface Passage {
    id: number,
    title: string,
    text: string,
    imageUrl: string,
    x: number,
    y: number,
    width: number,
    height: number,
    isCanvasQuestion: boolean
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
    correctAnswer?: Answer,
    answerColCount: number,
    x: number,
    y: number,
    width: number,
    height: number,
    isCanvasQuestion: boolean
}