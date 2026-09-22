/**
 * Liderlik tablosu sözleşmesi (issue #193) — `api/ExamApp.Api/Models/Dtos/LeaderboardDto.cs` ile birebir.
 * `GET /api/exam/leaderboard?scope=global|school&skip=0&take=20`.
 *
 * PII minimizasyonu: satırlarda kimlik alanı (userId, studentId, studentNumber, schoolId) YOKTUR;
 * "bu satır benim" bilgisi sunucudan `isMe` ile gelir.
 */

/** Query string'de gönderilen kapsam. Okul kimliği client'tan gönderilmez; sunucu çözer. */
export type LeaderboardScope = 'global' | 'school';

export interface LeaderboardEntry {
  /** Kapsam içindeki sıra (1 tabanlı, kapsam içinde tekil). */
  rank: number;
  xp: number;
  level: number;
  /** auth-api erişilemezse boş string gelir. */
  fullName: string;
  avatarUrl: string;
  /** Satır istek sahibinin kendi kaydı mı. */
  isMe: boolean;
}

export interface LeaderboardResult {
  success: boolean;
  message?: string | null;
  /** Uygulanan kapsam. */
  scope: LeaderboardScope;
  /** School kapsamında sunucu tarafında çözülen okul; global'de null. */
  schoolId: number | null;
  /** İstek sahibinin bir okulu var mı — "Okulum" sekmesi buna göre gösterilir. Global çağrıda da dolu. */
  schoolScopeAvailable: boolean;
  /** Kapsamdaki toplam öğrenci sayısı (sayfalamadan bağımsız). */
  totalCount: number;
  skip: number;
  take: number;
  entries: LeaderboardEntry[];
  /** İstek sahibinin kapsam içindeki sırası; öğrenci değilse veya kapsam dışıysa null. */
  myRank: number | null;
  myXp: number | null;
}

/** 400 gövdesi: `{ message }`. */
export interface LeaderboardErrorBody {
  message?: string | null;
}
