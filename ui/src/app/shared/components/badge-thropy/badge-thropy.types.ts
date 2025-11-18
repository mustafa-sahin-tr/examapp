export interface BadgePathPoint {
  xPercent: number;
  yPercent: number;
}

export interface BadgePathLayout {
  viewBox: string;
  height: number;
  pathD: string;
  arrowId: string;
  points: BadgePathPoint[];
}

export interface BadgeThropyItem {
  id: string;
  name: string;
  iconUrl: string;
  description: string;
  currentValue: number;
  targetValue: number;
  progressPercent: number;
  completedLabel: string;
  totalLabel: string;
  isCompleted: boolean;
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
  layout: BadgePathLayout | null;
}
