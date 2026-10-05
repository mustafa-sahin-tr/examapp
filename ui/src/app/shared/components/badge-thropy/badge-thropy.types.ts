import { BadgeMedallionState } from '../badge-medallion/badge-state.util';

export interface BadgePathPoint {
  xPercent: number;
  yPercent: number;
}

export interface BadgeThropyItem {
  id: string;
  name: string;
  /** Issue #149: Material Symbols adı; yoksa `iconUrl`, o da yoksa varsayılan glif. */
  icon: string | null;
  /** Eski SVG yolu (ham; yalnız `resolveBadgeIcon` üzerinden kullanılır). */
  iconUrl: string | null;
  description: string;
  currentValue: number;
  targetValue: number;
  progressPercent: number;
  completedLabel: string;
  totalLabel: string;
  /** Biçimlenmiş kalan miktar (hedef − mevcut, ≥ 0). */
  remainingLabel: string;
  isCompleted: boolean;
  /** Issue #149: medalyon durumu (`deriveBadgeState`, yükleme anında türetilir). */
  state: BadgeMedallionState;
  earnedDateUtc: string | null;
  pathKey?: string | null;
  pathName?: string | null;
  pathOrder?: number | null;
}

export interface BadgeThropyPath {
  key: string;
  name: string;
  badges: BadgeThropyItem[];
  completedCount: number;
  completionPercent: number;
}
