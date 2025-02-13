import { Subject } from "rxjs";
import { Answer } from "./answer";
import { Question } from "./question";

export enum TestStatus {
    NotStarted = -1,
    Started = 0,
    Completed = 1,
    Expired = 2
}

export interface TestInstanceQuestion {
    id: number;
    question: Question;
    order: number;
    selectedAnswerId: number;
    timeTaken: number;
}
export interface TestInstance {
    id: number;
    testName: string;
    status: TestStatus;
    maxDurationSeconds: number;
    testInstanceQuestions: TestInstanceQuestion[];
    isPracticeTest: boolean;
}

export interface Exam {
    id: number;
    name: string;
    description: string;
    maxDurationSeconds: number;
    totalQuestions: number;    
    instanceStatus : TestStatus;
    testInstanceId: number;
}

export interface   Test {
    id: number | null;
    name: string;
    description: string;
    gradeId: number;
    maxDurationSeconds: number;
    isPracticeTest: boolean;    
}