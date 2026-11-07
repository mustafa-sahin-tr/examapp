/** Issue #897: BadgeService `ClassifierStatusDto` — etkin sınıflandırıcı ve son 24 saat (servis başlangıcından beri). */
export interface ClassifierStatus {
  provider: 'Claude' | 'Gemini';
  model: string;
  /** `QuestionAnalyzer:AIActive`; false: her soru atlanır. */
  active: boolean;
  apiKeyConfigured: boolean;
  bookTocMode: string;
  since: string;
  classified: number;
  skipped: number;
  /** Başarısız deneme; yeniden denenen mesaj her denemede sayılır. */
  failed: number;
  lastErrorAt: string | null;
  /** Yalnız hata türü (ör. `HttpRequestException`). */
  lastErrorKind: string | null;
}
