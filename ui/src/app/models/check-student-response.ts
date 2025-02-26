import { Student } from "./student";

export interface CheckStudentResponse {
    hasStudentRecord: boolean;
    student: Student | null;
  }