/**
 * #395: karşılama sayfasının kodda duran verisi. Sayfa statiktir, API çağrısı yapmaz.
 * Güncelleme adımları: `docs/feature-notes.md` (Karşılama sayfası).
 */

/** Örnek değerler. Örnek oldukları sürece bölüm gösterilmez (#880); gerçek sayılar yazılınca `false` yapılır. */
export const LANDING_STATS_ARE_SAMPLE = true;
export const LANDING_STATS = [
  { key: 'teachers', icon: 'co_present', value: 1250 },
  { key: 'students', icon: 'backpack', value: 18400 },
  { key: 'questions', icon: 'quiz', value: 240000 },
  { key: 'solvedTests', icon: 'task_alt', value: 96000 },
] as const;

/** Temsili yorumlar. Temsili oldukları sürece bölüm gösterilmez (#880); tümü izinli gerçek yorum olunca `false`. Kart sayısı 1–6. */
export const LANDING_TESTIMONIALS_ARE_FICTIONAL = true;
export const LANDING_TESTIMONIALS = [
  { key: 'elif', initial: 'E', tone: 'av1' },
  { key: 'murat', initial: 'M', tone: 'av2' },
  { key: 'zeynep', initial: 'Z', tone: 'av3' },
  { key: 'deniz', initial: 'D', tone: 'av4' },
  { key: 'selin', initial: 'S', tone: 'av5' },
  { key: 'can', initial: 'C', tone: 'av1' },
] as const;

export const LANDING_CONTACT_EMAIL = 'admin@hedefokul.com';

/** Kayıt ve giriş auth-ui üzerinden yürür (#347); `intent` Keycloak dönüşünde profil adımına taşınır. */
export const LANDING_LOGIN_URL = '/app/login';
export const LANDING_REGISTER_STUDENT_URL = '/app/login?intent=student';
export const LANDING_REGISTER_TEACHER_URL = '/app/login?intent=teacher';

export const LANDING_NAV_LINKS = [
  { id: 'ozellikler', key: 'features', icon: 'widgets' },
  { id: 'ogretmenler', key: 'teachers', icon: 'co_present' },
  { id: 'ogrenciler', key: 'students', icon: 'backpack' },
  { id: 'veliler', key: 'parents', icon: 'family_restroom' },
  { id: 'ozel-ders', key: 'tutoring', icon: 'video_chat' },
  { id: 'nasil-calisir', key: 'howItWorks', icon: 'route' },
  { id: 'sss', key: 'faq', icon: 'help' },
] as const;

export const LANDING_ROLES = [
  { key: 'student', icon: 'backpack', tone: 'r1', ctaUrl: LANDING_REGISTER_STUDENT_URL, primary: true },
  { key: 'teacher', icon: 'co_present', tone: 'r2', ctaUrl: LANDING_REGISTER_TEACHER_URL, primary: false },
  { key: 'tutor', icon: 'video_chat', tone: 'r3', ctaUrl: LANDING_REGISTER_TEACHER_URL, primary: false },
  // #880: veli kendini kaydetmez, öğrencinin davetiyle hesap açar; kart girişe götürür. Menüdeki "Veliler" ayrı bölüme gider.
  { key: 'parent', icon: 'family_restroom', tone: 'r4', ctaUrl: LANDING_LOGIN_URL, primary: false },
] as const;

export const LANDING_FEATURES = [
  { key: 'detect', icon: 'document_scanner' },
  { key: 'classify', icon: 'auto_awesome' },
  { key: 'assign', icon: 'assignment_add' },
  { key: 'solve', icon: 'timer' },
  { key: 'daily', icon: 'bolt' },
  { key: 'program', icon: 'calendar_month' },
  { key: 'badges', icon: 'military_tech' },
  { key: 'notifications', icon: 'notifications_active' },
] as const;

export const LANDING_STEP_ROLES = ['teacher', 'student', 'tutor'] as const;

/** `TopicSeed` ders/sınıf eşlemesi (2.–11. sınıf). Taksonomide lise İngilizcesi yok. */
export const LANDING_SUBJECT_GROUPS = [
  { key: 'primary', subjects: ['turkish', 'math', 'life', 'science', 'social', 'english'] },
  { key: 'middle', subjects: ['turkish', 'math', 'science', 'social', 'english', 'religion', 'history'] },
  { key: 'high', subjects: ['math', 'literature', 'physics', 'chemistry', 'biology', 'historyHigh', 'geography', 'philosophy'] },
] as const;

export const LANDING_FAQ_KEYS = ['pricing', 'parent', 'teacherApproval', 'detection', 'access', 'video', 'languages'] as const;
