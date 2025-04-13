export interface StudentStatisticsResponse {
    total: StudentTestSummary;
    grouped: StudentTestGroupSummary[];
  }
  
  export interface StudentTestSummary {
    totalSolvedTests: number;
    completedTests: number;
    totalTimeSpentMinutes: number;
    totalCorrectAnswers: number;
    totalWrongAnswers: number;
  }
  
  export interface StudentTestGroupSummary {
    gradeId: number;
    testName: string;
    totalSolvedTests: number;
    completedTests: number;
    totalTimeSpentMinutes: number;
    totalCorrectAnswers: number;
    totalWrongAnswers: number;
  }
  