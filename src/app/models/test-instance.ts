import { Answer } from "./answer";
import { Question } from "./question";

export interface TestInstanceQuestion {
    id: number;
    question: Question;
    selectedAnswerId: number;
    timeTaken: number;
}
export interface TestInstance {
    id: number;
    testName: string;
    testInstanceQuestions: TestInstanceQuestion[];
}