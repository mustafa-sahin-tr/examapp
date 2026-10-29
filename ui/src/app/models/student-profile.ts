import { Grade } from './student';

export interface StudentProfile {
  fullName: string;
  gradeId: number;
  avatarUrl: string;
  xp: number;
  level: number;
  totalQuestionsSolved: number;
  correctAnswers: number;
  wrongAnswers: number;
  testsCompleted: number;
  leaderboardRank: number;
  recentTests: { name: string; score: number; totalQuestions: number }[];
  themePreference?: string; // User'ın tercih ettiği tema
  /** Issue #438: hesap veli tarafından açıldı (kullanıcı adı + şifre) — "Hesabım / şifre değiştir" kartı gösterilmez. */
  parentManaged?: boolean;
}
