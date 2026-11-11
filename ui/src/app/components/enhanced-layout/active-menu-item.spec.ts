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
        menu('my-calendar', '/my-calendar'),
        menu('tutors', '/tutors'),
        menu('teacher-messages', '/teacher-messages'),
        divider,
        menu('reports', '/certificates'),
        menu('settings', '/student-settings'),
    ];

    const TEACHER_ITEMS: RoutedMenuEntry[] = [
        menu('dashboard', '/dashboard'),
        menu('exams', '/tests'),
        menu('my-calendar', '/my-calendar'),
        divider,
        menu('study-pages', '/study-pages'),
        menu('teacher-requests', '/assignment-permission-requests'),
        menu('student-messages', '/student-messages'),
        menu('reports', '/certificates'),
    ];

    const ADMIN_ITEMS: RoutedMenuEntry[] = [
        menu('reports', '/certificates'),
        menu('admin-dashboard', '/admin/dashboard'),
        menu('admin', '/admin'),
        menu('admin-teachers', '/admin/teachers'),
        menu('questiontransfer', '/question-transfer'),
        menu('open-text-reviews', '/open-text-reviews'),
    ];

    const STUDENT_CASES: Array<[
        url: string,
        expected: string | null
    ]> = [
        ['/dashboard', 'dashboard'],
        ['/programs', 'my-calendar'],
        ['/programs/5/detail', 'my-calendar'],
        ['/program-create', 'my-calendar'],
        ['/my-bookings', 'tutors'],
        ['/ask-teacher', 'teacher-messages'],
        ['/test/12', 'exams'],
        ['/test/12?reminder=edit', 'exams'],
        ['/tests?section=popular', 'exams'],
        ['/testsolve/44', 'exams'],
        ['/tutors/9', 'tutors'],
        ['/study', 'study'],
        ['/student-settings#stats', 'settings'],
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

    const TEACHER_CASES: Array<[
        url: string,
        expected: string | null
    ]> = [
        ['/teacher/ask-queue?q=3', 'student-messages'],
        ['/exam', 'exams'],
        ['/exam/7', 'exams'],
        ['/questioncanvas', 'exams'],
        ['/questioncanvas/15', 'exams'],
        ['/question/21', 'exams'],
        ['/student-school-requests', 'teacher-requests'],
        ['/open-text-reviews', 'teacher-requests'],
        // Segment sınırı: `/questions` (view dışı) alias'a girmez.
        ['/questions', null],
        ['/test/12', 'exams'],
        ['/study-pages/new', 'study-pages'],
        ['/study-pages/4', 'study-pages'],
        ['/study-links', 'study-pages'],
        ['/teacher/flashcards/4', 'study-pages'],
        ['/teacher/coach/7', 'student-messages'],
        // Segment sınırı: /study, /study-pages öğesinin öneki sayılmaz.
        ['/study', null],
    ];

    for (const [url, expected] of TEACHER_CASES) {
        it(`teacher: ${url} → ${expected}`, () => {
            expect(resolveActiveMenuItemId(url, TEACHER_ITEMS)).toBe(expected);
        });
    }

    const ADMIN_CASES: Array<[
        url: string,
        expected: string | null
    ]> = [
        ['/admin', 'admin'],
        ['/admin/dashboard', 'admin-dashboard'],
        ['/admin/teachers?page=2', 'admin-teachers'],
        ['/admin/schools', 'admin'],
        ['/question-transfer', 'questiontransfer'],
        // Takma adın hedefi Admin menüsünde yok; kendi öğesi seçilir.
        ['/open-text-reviews', 'open-text-reviews'],
        ['/dashboard', null],
    ];

    for (const [url, expected] of ADMIN_CASES) {
        it(`admin: ${url} → ${expected}`, () => {
            expect(resolveActiveMenuItemId(url, ADMIN_ITEMS)).toBe(expected);
        });
    }

    it('mockExamTabs_KeepTestsSelected_ButAdminHasItsOwnItem', () => {
        expect(resolveActiveMenuItemId('/my-mock-exams/5/result', STUDENT_ITEMS)).toBe('exams');
        expect(resolveActiveMenuItemId('/mock-exams/5', [menu('exams', '/tests')])).toBe('exams');
        expect(resolveActiveMenuItemId('/mock-exams/5', [menu('mock-exams', '/mock-exams')])).toBe('mock-exams');
    });

    it('studyPrefix_DoesNotMatchStudyPagesForStudent', () => {
        const items = [menu('study', '/study'), menu('study-pages', '/study-pages')];
        expect(resolveActiveMenuItemId('/study-pages/1', items)).toBe('study-pages');
        expect(resolveActiveMenuItemId('/study', items)).toBe('study');
    });
});
