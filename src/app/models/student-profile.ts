export interface StudentProfile {
  fullName: string;
  avatarUrl: string;
  xp: number;
  level: number;
  totalQuestionsSolved: number;
  correctAnswers: number;
  wrongAnswers: number;
  testsCompleted: number;
  totalRewards: number;
  leaderboardRank: number;
  badges: { name: string, imageUrl: string }[];
  recentTests: { name: string, score: number, totalQuestions: number }[];
}