import { RoutedMenuEntry, resolveActiveMenuItemId } from './active-menu-item';

/** Issue #385: URL → seçili menü öğesi türetmesi (saf fonksiyon). */
describe('resolveActiveMenuItemId (issue #385)', () => {
  const menu = (id: string, route: string): RoutedMenuEntry => ({ id, route, type: 'menu' });
  const divider: RoutedMenuEntry = { id: 'divider1', route: '', type: 'divider' };

  const STUDENT_ITEMS: RoutedMenuEntry[] = [
    menu('dashboard', '/dashboard'),
    menu('exams', '/tests'),
    menu('practice', '/practice'),
    menu('study', '/study'),
    menu('programsm', '/programs'),
    menu('my-calendar', '/my-calendar'),
    menu('tutors', '/tutors'),
    divider,
    menu('reports', '/certificates'),
    menu('settings', '/student-profile'),
  ];

  const TEACHER_ITEMS: RoutedMenuEntry[] = [
    menu('dashboard', '/dashboard'),
    menu('exams', '/tests'),
    menu('my-calendar', '/my-calendar'),
    divider,
    menu('study-pages', '/study-pages'),
    menu('study-links', '/study-links'),
    menu('exam', '/exam'),
    menu('reports', '/certificates'),
  ];

  const ADMIN_ITEMS: RoutedMenuEntry[] = [
    menu('reports', '/certificates'),
    menu('admin-dashboard', '/admin/dashboard'),
    menu('admin', '/admin'),
    menu('admin-teachers', '/admin/teachers'),
    menu('questiontransfer', '/question-transfer'),
  ];

  const STUDENT_CASES: Array<[url: string, expected: string | null]> = [
    ['/dashboard', 'dashboard'],
    ['/programs', 'programsm'],
    ['/programs/5/detail', 'programsm'],
    ['/program-create', 'programsm'],
    ['/test/12', 'exams'],
    ['/test/12?reminder=edit', 'exams'],
    ['/tests?section=popular', 'exams'],
    ['/tests-enhanced', 'exams'],
    ['/testsolve/44', 'exams'],
    ['/tutors/9', 'tutors'],
    ['/study', 'study'],
    ['/student-profile#stats', 'settings'],
    ['/practice?daily=1', 'practice'],
    // Karşılığı olmayan sayfa: hiçbiri seçili değil — Dashboard'a düşülmez.
    ['/notifications', null],
    ['/lessons/3/video', null],
    ['/', null],
  ];

  for (const [url, expected] of STUDENT_CASES) {
    it(`student: ${url} → ${expected}`, () => {
      expect(resolveActiveMenuItemId(url, STUDENT_ITEMS)).toBe(expected);
    });
  }

  const TEACHER_CASES: Array<[url: string, expected: string | null]> = [
    ['/exam', 'exam'],
    ['/exam/7', 'exam'],
    ['/questioncanvas', 'exam'],
    ['/questioncanvas/15', 'exam'],
    ['/questioncanvas/preview/3', 'exam'],
    ['/question', 'exam'],
    ['/question/21', 'exam'],
    ['/questions/view?id=4', 'exam'],
    ['/imageselect', 'exam'],
    // Segment sınırı: `/questions` (view dışı) alias'a girmez.
    ['/questions', null],
    ['/test/12', 'exams'],
    ['/study-pages/new', 'study-pages'],
    ['/study-pages/4', 'study-pages'],
    ['/study-links', 'study-links'],
    // Segment sınırı: /study, /study-pages öğesinin öneki sayılmaz.
    ['/study', null],
    // Rolüne kapalı öğe seçilmez: öğretmen menüsünde Programlarım yok.
    ['/programs/5/detail', null],
  ];

  for (const [url, expected] of TEACHER_CASES) {
    it(`teacher: ${url} → ${expected}`, () => {
      expect(resolveActiveMenuItemId(url, TEACHER_ITEMS)).toBe(expected);
    });
  }

  const ADMIN_CASES: Array<[url: string, expected: string | null]> = [
    ['/admin', 'admin'],
    ['/admin/dashboard', 'admin-dashboard'],
    ['/admin/teachers?page=2', 'admin-teachers'],
    ['/admin/schools', 'admin'],
    ['/question-transfer', 'questiontransfer'],
    ['/dashboard', null],
  ];

  for (const [url, expected] of ADMIN_CASES) {
    it(`admin: ${url} → ${expected}`, () => {
      expect(resolveActiveMenuItemId(url, ADMIN_ITEMS)).toBe(expected);
    });
  }

  it('studyPrefix_DoesNotMatchStudyPagesForStudent', () => {
    const items = [menu('study', '/study'), menu('study-pages', '/study-pages')];
    expect(resolveActiveMenuItemId('/study-pages/1', items)).toBe('study-pages');
    expect(resolveActiveMenuItemId('/study', items)).toBe('study');
  });
});
