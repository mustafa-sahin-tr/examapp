export interface Teacher {
  id: number;
  userId: number;
  user?: any; // Eğer gerekirse
  schoolName: string;
  /** `TeacherDto.SchoolId` — okulsuz öğretmende null (issue #191). */
  schoolId?: number | null;
  themePreset?: string; // 🎨 Theme tercihi
  themeCustomConfig?: string; // 🎨 Custom theme config (JSON)
}
