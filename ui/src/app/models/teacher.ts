export interface Teacher {
  id: number;
  userId: number;
  user?: any; // Eğer gerekirse
  schoolName: string;
  themePreset?: string; // 🎨 Theme tercihi
  themeCustomConfig?: string; // 🎨 Custom theme config (JSON)
}
