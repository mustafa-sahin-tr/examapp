import { Student } from "./student";
import { Teacher } from "./teacher";

export interface CheckkTeacherResponse {
    hasTeacherRecord: boolean;
    teacher: Teacher | null;
  }