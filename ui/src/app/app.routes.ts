import { Routes } from '@angular/router';
import { RegisterWizardComponent } from './pages/register/register-wizard.component';
import { authGuard } from './shared/guards/auth.guard';
import { adminGuard } from './shared/guards/admin.guard';
import { adminHomeRedirectGuard } from './shared/guards/admin-home-redirect.guard';
import { adminSchoolFilterRedirectGuard } from './shared/guards/admin-school-filter-redirect.guard';
import { rootRedirect } from './shared/guards/root-redirect';
import { independentTeacherGuard } from './shared/guards/independent-teacher.guard';
import { studentGuard } from './shared/guards/student.guard';
import { roleGuard } from './shared/guards/role.guard';
import { approvedTeacherGuard } from './shared/guards/approved-teacher.guard';
import { entitlementGuard } from './shared/guards/entitlement.guard';
import { settingsGuard } from './shared/guards/settings.guard';
import { parentGuard, parentHomeRedirectGuard } from './shared/guards/parent.guard';
import { parentRequirementGuard } from './shared/guards/parent-requirement.guard';
import { PARENT_REQUIRED_PATH } from './models/parent-status.model';
import { TEACHER_APPROVAL_PENDING_PATH } from './models/teacher-approval.model';
import { QuestionComponent } from './pages/question/question.component';
import { StudentProfileComponent } from './pages/student-profile/student-profile.component';
import { WorksheetListComponent } from './pages/worksheet-list/worksheet-list.component';
import { worksheetListResolver } from './shared/resolvers/worksheet-resolver';
import { QuestionCanvasComponent } from './pages/question/question-canvas.component';
import { WorksheetDetailComponent } from './pages/worksheet-detail/worksheet-detail.component';
import { ProgramCreateComponent } from './pages/program-create/program-create.component';
import { MyProgramsComponent } from './pages/my-programs/my-programs.component';
import { BadgeThropyComponent } from './shared/components/badge-thropy/badge-thropy.component';
import { EnhancedLayoutComponent } from './components/enhanced-layout/enhanced-layout.component';
import { TestCreateEnhancedComponent } from './pages/test-create-enhanced/test-create-enhanced.component';
import { DashboardSwitchComponent } from './pages/dashboard/dashboard-switch.component';
import { TestSolveCanvasComponentv3 } from './pages/test-solve/test-solve-canvas-v3.component';
import { testSolveLeaveGuard } from './pages/test-solve/test-solve-leave.guard';
import { QuestionTransferComponent } from './pages/question-transfer/question-transfer.component';
import { QuestionGenerationComponent } from './pages/question-generation/question-generation.component';
import { MockExamsComponent } from './pages/mock-exams/mock-exams.component';
import { MockExamBuilderComponent } from './pages/mock-exams/mock-exam-builder.component';
import { ExamTemplatesComponent } from './pages/mock-exams/exam-templates.component';
import { TargetProgramsComponent } from './pages/mock-exams/target-programs.component';
import { MockExamResultComponent, StudentMockExamsComponent } from './pages/mock-exams/student-mock-exams.component';
import { StudyPagesComponent } from './pages/study-pages/study-pages.component';
import { StudyPageEditorComponent } from './pages/study-pages/study-page-editor.component';

/** Issue #709: soru yazma ekranları; API de yalnız Teacher/Admin + onaylı öğretmen kabul eder (QuestionsController). */
const authoringGuards = [roleGuard('Teacher', 'Admin'), approvedTeacherGuard];

export const routes: Routes = [
  // Public landing and legal pages (top-level)
  {
    path: 'welcome',
    loadComponent: () => import('./pages/public/landing/landing.component').then((m) => m.LandingComponent),
  },
  {
    path: 'privacy-policy',
    loadComponent: () =>
      import('./pages/public/privacy-policy/privacy-policy.component').then((m) => m.PrivacyPolicyComponent),
  },
  {
    path: 'terms',
    loadComponent: () => import('./pages/public/terms/terms.component').then((m) => m.TermsComponent),
  },
  {
    path: 'score-calculator',
    loadComponent: () =>
      import('./pages/public/score-calculator/score-calculator.component').then((m) => m.ScoreCalculatorComponent),
  },
  // #875: oturum açmış kullanıcı panele, ziyaretçi tanıtım sayfasına.
  { path: '', redirectTo: rootRedirect, pathMatch: 'full' },
  // Back-compat: the old per-role routes now land on the wizard at step 2.
  { path: 'student-register', redirectTo: () => '/register?role=student' },
  { path: 'teacher-register', redirectTo: () => '/register?role=teacher' },
  { path: 'parent-register', redirectTo: () => '/register?role=parent' },
  {
    // Issue #701: kağıt çıktısı; uygulama kabuğu baskıya girmesin diye layout dışında.
    path: 'exam/:id/print',
    canActivate: [authGuard, roleGuard('Teacher'), approvedTeacherGuard, entitlementGuard('print-test')],
    loadComponent: () => import('./pages/test-print/test-print.component').then((m) => m.TestPrintComponent),
  },
  // Main app (protected) routes — all nested under a single EnhancedLayoutComponent
  // instance so the sidenav is not destroyed/recreated on every navigation.
  {
    path: '',
    component: EnhancedLayoutComponent,
    // Issue #441: hoşgörü süresi dolmuş öğrenci yalnız "veli bilgisi ekle" ekranını görür.
    canActivateChild: [parentRequirementGuard],
    children: [
      // Issue #53: Teacher rolü → TeacherDashboardComponent, diğerleri → mevcut DashboardComponent.
      // Issue #287: onaysız öğretmen teacher dashboard yerine başvuru durumu sayfasına gider (öğrenci etkilenmez).
      // Issue #420: veli /dashboard yerine veli paneline (/parent) düşer.
      // #874: yalnız admin olan hesap öğrenci paneli yerine admin paneline.
      {
        path: 'dashboard',
        component: DashboardSwitchComponent,
        canActivate: [authGuard, parentHomeRedirectGuard, adminHomeRedirectGuard, approvedTeacherGuard],
      },
      {
        // Issue #287: öğretmen hesabı onay bekliyor / reddedildi — onaysız öğretmenin tek öğretmen ekranı.
        path: TEACHER_APPROVAL_PENDING_PATH,
        canActivate: [authGuard, roleGuard('Teacher')],
        loadComponent: () =>
          import('./pages/teacher-approval-pending/teacher-approval-pending.component').then(
            (m) => m.TeacherApprovalPendingComponent
          ),
      },
      {
        path: 'tests',
        component: WorksheetListComponent,
        // Issue #287: teacher girişi /tests'e düşer; onaysız öğretmen resolver (exam/list) çalışmadan durum sayfasına gider.
        canActivate: [authGuard, approvedTeacherGuard],
        resolve: { worksheets: worksheetListResolver },
      },
      // Two-step onboarding: step 1 role picker (skipped when the role is known
      // from ?role= or an already-assigned realm role), step 2 the role form.
      { path: 'register', component: RegisterWizardComponent },
      { path: 'question/:id', component: QuestionComponent, canActivate: [authGuard, ...authoringGuards] },
      // #1087: soru tuvali öğretmenin soru girişi; planda `manual-labeling` ister (admin muaf).
      { path: 'questioncanvas', component: QuestionCanvasComponent, canActivate: [authGuard, ...authoringGuards, entitlementGuard('manual-labeling')] },
      { path: 'questioncanvas/:id', component: QuestionCanvasComponent, canActivate: [authGuard, ...authoringGuards, entitlementGuard('manual-labeling')] },
      {
        // Soru tespit modeli için işaretleme modu: yalnız eğitim verisi (send-to-fix), test/soru kaydı yok.
        // Gateway send-to-fix'i de yalnız bu iki realm rolüne açar. Öğretmenin soru girişi soru tuvalidir (#1087).
        path: 'labeling',
        canActivate: [authGuard, roleGuard('Admin', 'QuestionTrainer')],
        loadComponent: () => import('./pages/labeling/labeling.component').then((m) => m.LabelingComponent),
      },
      {
        // Issue #577: işaretleme kitap kaydı (ofset, cevap anahtarı, içindekiler) — işaretleme ile aynı roller.
        path: 'books',
        canActivate: [authGuard, roleGuard('Admin', 'QuestionTrainer')],
        loadComponent: () => import('./pages/books/books.component').then((m) => m.BooksComponent),
      },
      {
        // Issue #1018 (epic #1017): PDF'ten kitap aktarımı. Liste herkese açık (demo #1032); yükleme planda `book-import` ister (#1087).
        path: 'book-imports',
        canActivate: [authGuard, roleGuard('Admin', 'QuestionTrainer', 'Teacher'), approvedTeacherGuard],
        loadComponent: () => import('./pages/book-imports/book-imports.component').then((m) => m.BookImportsComponent),
      },
      {
        path: 'book-imports/:id',
        canActivate: [authGuard, roleGuard('Admin', 'QuestionTrainer', 'Teacher'), approvedTeacherGuard],
        loadComponent: () =>
          import('./pages/book-imports/book-import-detail.component').then((m) => m.BookImportDetailComponent),
      },
      {
        // Issue #1053 (epic #1048): hızlı soru ekleme — backend Teacher/Admin/QuestionTrainer + onaylı öğretmen.
        path: 'quick-add',
        canActivate: [authGuard, roleGuard('Teacher', 'Admin', 'QuestionTrainer'), approvedTeacherGuard, entitlementGuard('quick-add')],
        loadComponent: () => import('./pages/quick-add/quick-add.component').then((m) => m.QuickAddComponent),
      },
      {
        path: 'quick-add/:id',
        canActivate: [authGuard, roleGuard('Teacher', 'Admin', 'QuestionTrainer'), approvedTeacherGuard, entitlementGuard('quick-add')],
        loadComponent: () => import('./pages/quick-add/quick-add-detail.component').then((m) => m.QuickAddDetailComponent),
      },
      {
        path: 'question-reports',
        canActivate: [authGuard, ...authoringGuards],
        loadComponent: () => import('./pages/question-reports/question-reports.component').then((m) => m.QuestionReportsComponent),
      },
      {
        // Issue #471: soru bankası araması — backend Teacher/Admin + onaylı öğretmen.
        path: 'question-bank',
        canActivate: [authGuard, roleGuard('Teacher', 'Admin'), approvedTeacherGuard],
        loadComponent: () => import('./pages/question-bank/question-bank.component').then((m) => m.QuestionBankComponent),
      },
      {
        // Issue #590: görselsiz etkinlik maddesi editörü (form tabanlı yazarlık) — backend Teacher/Admin + test sahibi.
        path: 'activity-editor',
        canActivate: [authGuard, roleGuard('Teacher', 'Admin'), approvedTeacherGuard, entitlementGuard('activity-editor')],
        loadComponent: () =>
          import('./pages/activity-editor/activity-editor.component').then((m) => m.ActivityEditorComponent),
      },
      { path: 'testsolve/:testInstanceId', component: TestSolveCanvasComponentv3, canActivate: [authGuard, approvedTeacherGuard], canDeactivate: [testSolveLeaveGuard] },
      {
        // Issue #573: etkinlik sayfası oynatıcısı (madde madde, kontrol/çözüm; puansız). Test oturumu gelirse /testsolve'a döner.
        path: 'activity/:testInstanceId',
        canActivate: [authGuard, approvedTeacherGuard],
        loadComponent: () =>
          import('./pages/activity-player/activity-player.component').then((m) => m.ActivityPlayerComponent),
      },
      { path: 'test/:testId', component: WorksheetDetailComponent, canActivate: [authGuard, approvedTeacherGuard] },
      // Issue #373: yalnız öğrenci verisiyle çalışır; öğretmen/admin boş sayfa + 500 görüyordu → /dashboard'a yönlendirilir.
      { path: 'student-profile', component: StudentProfileComponent, canActivate: [authGuard, studentGuard] },
      {
        // #847: öğrencinin ayarları (sınıf, tema, hesap, veliler) profilden ayrı.
        path: 'student-settings',
        canActivate: [authGuard, studentGuard],
        loadComponent: () =>
          import('./pages/student-settings/student-settings.component').then((m) => m.StudentSettingsComponent),
      },
      {
        // Issue #440/#441: veli bilgisi ekle / e-postayı düzelt (süre dolunca öğrencinin tek ekranı).
        path: PARENT_REQUIRED_PATH,
        canActivate: [authGuard, studentGuard],
        loadComponent: () =>
          import('./pages/parent-required/parent-required.component').then((m) => m.ParentRequiredComponent),
      },
      { path: 'exam', component: TestCreateEnhancedComponent, canActivate: [authGuard, roleGuard('Teacher'), approvedTeacherGuard] },
      { path: 'exam/:id', component: TestCreateEnhancedComponent, canActivate: [authGuard, roleGuard('Teacher'), approvedTeacherGuard] },
      // Issue #1089: çalışma programı ve takibi planda `study-tools` ister.
      { path: 'programs', component: MyProgramsComponent, canActivate: [authGuard, roleGuard('Student'), entitlementGuard('study-tools')] },
      {
        path: 'programs/:id/detail',
        loadComponent: () =>
          import('./pages/my-programs/program-detail.component').then((m) => m.ProgramDetailComponent),
        canActivate: [authGuard, roleGuard('Student'), entitlementGuard('study-tools')],
      },
      { path: 'program-create', component: ProgramCreateComponent, canActivate: [authGuard, roleGuard('Student'), entitlementGuard('study-tools')] },
      // #889: rozetler /badges; eski /certificates bağlantıları (bildirimler, yer imleri) oraya yönlenir.
      { path: 'badges', component: BadgeThropyComponent, canActivate: [authGuard] },
      { path: 'certificates', redirectTo: 'badges' },
      {
        // Issue #146: kalıcı bildirim listesi (rozet kazanımı ve diğer BadgeService bildirimleri) — tüm roller.
        path: 'notifications',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./pages/notifications/notifications.component').then((m) => m.NotificationsComponent),
      },
      // Issue #382: /study sabit örnek veriyle çalışıyor ve hiçbir şey kaydetmiyordu; gerçek veriye bağlanana kadar
      // menüden kaldırıldı, eski yer imleri/doğrudan URL öğrencinin ana sayfasına düşer.
      { path: 'study', redirectTo: '/dashboard', pathMatch: 'full' },
      { path: 'study-pages', component: StudyPagesComponent, canActivate: [authGuard, roleGuard('Teacher'), approvedTeacherGuard] },
      { path: 'study-pages/new', component: StudyPageEditorComponent, canActivate: [authGuard, roleGuard('Teacher'), approvedTeacherGuard] },
      { path: 'study-pages/:id', component: StudyPageEditorComponent, canActivate: [authGuard, roleGuard('Teacher'), approvedTeacherGuard] },
      {
        // Issue #933: çalışma sayfasının öğrenci görünümü (öğretmen de açabilir; soru çözme yalnız öğrenci).
        path: 'study-page/:id',
        canActivate: [authGuard, approvedTeacherGuard],
        loadComponent: () =>
          import('./pages/study-page-view/study-page-view.component').then((m) => m.StudyPageViewComponent),
      },
      {
        // Issue #61: öğretmenin tüm konu/alt konular için çalışma linki yönetimi (admin taksonomi ekranından bağımsız).
        path: 'study-links',
        canActivate: [authGuard, roleGuard('Teacher'), approvedTeacherGuard],
        loadComponent: () =>
          import('./pages/study-links/study-links.component').then((m) => m.StudyLinksComponent),
      },
      {
        // Issue #365: soru aktarımı (soru bankası export/import) yalnız Admin — backend de Admin-only.
        // Issue #693: denemeler. Kurulum öğretmen ve admin, şablonlar admin, çözüm öğrenci.
        path: 'mock-exams',
        component: MockExamsComponent,
        canActivate: [authGuard, ...authoringGuards],
      },
      { path: 'mock-exams/:id', component: MockExamBuilderComponent, canActivate: [authGuard, ...authoringGuards] },
      { path: 'exam-templates', component: ExamTemplatesComponent, canActivate: [authGuard, adminGuard] },
      { path: 'target-programs', component: TargetProgramsComponent, canActivate: [authGuard, adminGuard] },
      {
        path: 'my-target',
        canActivate: [authGuard, roleGuard('Student')],
        loadComponent: () => import('./pages/my-target/my-target.component').then((m) => m.MyTargetComponent),
      },
      {
        path: 'my-score-calculator',
        canActivate: [authGuard, roleGuard('Student')],
        data: { embedded: true },
        loadComponent: () =>
          import('./pages/public/score-calculator/score-calculator.component').then((m) => m.ScoreCalculatorComponent),
      },
      { path: 'my-mock-exams', component: StudentMockExamsComponent, canActivate: [authGuard, roleGuard('Student')] },
      { path: 'my-mock-exams/:id/result', component: MockExamResultComponent, canActivate: [authGuard, roleGuard('Student')] },
      {
        // Kazanımdan görselsiz soru: içerik ekibi ve (#1087) planında `question-generation` olan öğretmen.
        path: 'question-generation',
        component: QuestionGenerationComponent,
        canActivate: [
          authGuard,
          roleGuard('Admin', 'QuestionTrainer', 'Teacher'),
          approvedTeacherGuard,
          entitlementGuard('question-generation'),
        ],
      },
      {
        path: 'question-transfer',
        component: QuestionTransferComponent,
        canActivate: [authGuard, adminGuard],
      },
      {
        path: 'assignment-permission-requests',
        canActivate: [authGuard, roleGuard('Teacher'), approvedTeacherGuard],
        loadComponent: () =>
          import('./pages/assignment-permission-requests/assignment-permission-requests.component').then(
            (m) => m.AssignmentPermissionRequestsComponent
          ),
      },
      {
        // Issue #583: açık uçlu etkinlik cevaplarının değerlendirme kuyruğu (backend Teacher/Admin + onaylı öğretmen; kapsam sunucuda).
        path: 'open-text-reviews',
        canActivate: [authGuard, roleGuard('Teacher', 'Admin'), approvedTeacherGuard],
        loadComponent: () =>
          import('./pages/open-text-reviews/open-text-reviews.component').then((m) => m.OpenTextReviewsComponent),
      },
      {
        // Issue #361: okulun onaylı öğretmeni — kendi okulunu seçen öğrencilerin üyelik onayı (kapsam sunucuda).
        path: 'student-school-requests',
        canActivate: [authGuard, roleGuard('Teacher'), approvedTeacherGuard],
        loadComponent: () =>
          import('./pages/student-school-requests/student-school-requests.component').then(
            (m) => m.StudentSchoolRequestsComponent
          ),
      },
      {
        // Issue #361: platform admin — tüm okulların bekleyen öğrenci üyelikleri (öğretmen sayfasıyla aynı komponent).
        path: 'admin/student-school-requests',
        canActivate: [authGuard, adminGuard],
        loadComponent: () =>
          import('./pages/student-school-requests/student-school-requests.component').then(
            (m) => m.StudentSchoolRequestsComponent
          ),
      },
      {
        path: 'admin',
        // #897: okul filtreli eski bağlantı Okullar sayfasına.
        canActivate: [authGuard, adminGuard, adminSchoolFilterRedirectGuard],
        loadComponent: () => import('./pages/admin/admin-home/admin-home.component').then((m) => m.AdminHomeComponent),
      },
      {
        // Issue #86: admin dashboard sayaç kartları (Phase 1).
        path: 'admin/dashboard',
        canActivate: [authGuard, adminGuard],
        loadComponent: () =>
          import('./pages/admin/admin-dashboard/admin-dashboard.component').then((m) => m.AdminDashboardComponent),
      },
      {
        // Issue #94: bağımsız öğretmen başvuruları onay paneli (admin-home sekmesi olarak da gömülü).
        path: 'admin/teacher-approvals',
        canActivate: [authGuard, adminGuard],
        loadComponent: () =>
          import('./pages/admin/teacher-approvals/teacher-approvals.component').then((m) => m.TeacherApprovalsComponent),
      },
      {
        // Issue #152: öğretmen listesi (server-side sayfalama + okul filtresi).
        path: 'admin/teachers',
        canActivate: [authGuard, adminGuard],
        loadComponent: () =>
          import('./pages/admin/admin-teachers/admin-teachers.component').then((m) => m.AdminTeachersComponent),
      },
      {
        // Issue #153: öğrenci listesi (server-side sayfalama + okul filtresi; #152 ile aynı kalıp).
        path: 'admin/students',
        canActivate: [authGuard, adminGuard],
        loadComponent: () =>
          import('./pages/admin/admin-students/admin-students.component').then((m) => m.AdminStudentsComponent),
      },
      {
        // Issue #1030: admin plan ataması.
        path: 'admin/subscriptions',
        canActivate: [authGuard, adminGuard],
        loadComponent: () =>
          import('./pages/admin/admin-subscriptions/admin-subscriptions.component').then((m) => m.AdminSubscriptionsComponent),
      },
      {
        // Issue #150: okul yönetimi (Taksonomi'den ayrıldı; admin-home "Okullar" sekmesi olarak da gömülü).
        path: 'admin/schools',
        canActivate: [authGuard, adminGuard],
        loadComponent: () =>
          import('./pages/admin/school-manager/school-manager.component').then((m) => m.SchoolManagerComponent),
      },
      {
        // Issue #1056: kitap aktarımı ve hızlı ekleme AI maliyet raporu.
        path: 'admin/book-import-costs',
        canActivate: [authGuard, adminGuard],
        loadComponent: () =>
          import('./pages/admin/admin-import-costs/admin-import-costs.component').then((m) => m.AdminImportCostsComponent),
      },
      {
        // Issue #148: rozet tanımları yönetimi (oluştur / düzenle / aktifleştir-devre dışı bırak; silme yok).
        path: 'admin/badge-definitions',
        canActivate: [authGuard, adminGuard],
        loadComponent: () =>
          import('./pages/admin/badge-definitions/badge-definitions.component').then((m) => m.BadgeDefinitionsComponent),
      },
      {
        // Issue #305: tüm worksheet'lerdeki şikayet edilmiş yorumlar (gizle / aç, yoruma git).
        path: 'admin/comment-reports',
        canActivate: [authGuard, adminGuard],
        loadComponent: () =>
          import('./pages/admin/admin-comment-reports/admin-comment-reports.component').then(
            (m) => m.AdminCommentReportsComponent
          ),
      },
      {
        // Issue #63: "Soru Çöz" pratik akışı (kapsam seç → tek tek rastgele soru → anlık geri bildirim).
        path: 'practice',
        canActivate: [authGuard, studentGuard],
        loadComponent: () =>
          import('./pages/practice-solve/practice-solve.component').then((m) => m.PracticeSolveComponent),
      },
      {
        // Issue #532: kaydedilen sorular.
        path: 'saved-questions',
        canActivate: [authGuard, studentGuard, entitlementGuard('study-tools')],
        loadComponent: () =>
          import('./pages/saved-questions/saved-questions.component').then((m) => m.SavedQuestionsComponent),
      },
      {
        // Issue #698: yanlış defteri.
        path: 'mistakes',
        canActivate: [authGuard, studentGuard, entitlementGuard('study-tools')],
        loadComponent: () =>
          import('./pages/mistake-notebook/mistake-notebook.component').then((m) => m.MistakeNotebookComponent),
      },
      {
        // Issue #699: bilgi kartları (öğrenci); `review` :id'den önce gelir.
        path: 'flashcards',
        canActivate: [authGuard, studentGuard],
        data: { mode: 'student' },
        loadComponent: () => import('./pages/flashcards/flashcard-decks.component').then((m) => m.FlashcardDecksComponent),
      },
      {
        path: 'flashcards/review',
        canActivate: [authGuard, studentGuard],
        loadComponent: () => import('./pages/flashcards/flashcard-review.component').then((m) => m.FlashcardReviewComponent),
      },
      {
        path: 'flashcards/:id',
        canActivate: [authGuard, studentGuard],
        data: { mode: 'student' },
        loadComponent: () =>
          import('./pages/flashcards/flashcard-deck-detail.component').then((m) => m.FlashcardDeckDetailComponent),
      },
      {
        // Issue #699: öğretmenin desteleri ve atamaları.
        path: 'teacher/flashcards',
        canActivate: [authGuard, roleGuard('Teacher'), approvedTeacherGuard],
        data: { mode: 'teacher' },
        loadComponent: () => import('./pages/flashcards/flashcard-decks.component').then((m) => m.FlashcardDecksComponent),
      },
      {
        path: 'teacher/flashcards/:id',
        canActivate: [authGuard, roleGuard('Teacher'), approvedTeacherGuard],
        data: { mode: 'teacher' },
        loadComponent: () =>
          import('./pages/flashcards/flashcard-deck-detail.component').then((m) => m.FlashcardDeckDetailComponent),
      },
      {
        // Issue #700: koç paneli; öğrenci kapsamı sunucuda (kendi okulu ya da onaylı özel ders).
        // Issue #893: iki panel — liste solda kalır, seçili öğrenci sağdaki çocuk rotada açılır.
        path: 'teacher/coach',
        canActivate: [authGuard, roleGuard('Teacher'), approvedTeacherGuard, entitlementGuard('coach-panel')],
        loadComponent: () => import('./pages/coach/coach-students.component').then((m) => m.CoachStudentsComponent),
        children: [
          {
            path: ':studentId',
            loadComponent: () => import('./pages/coach/coach-student.component').then((m) => m.CoachStudentComponent),
          },
        ],
      },
      {
        // Issue #1029: Aboneliğim — plan, bu ayki kullanım ve sıfırlanma tarihi; "Planı yükselt" → /plans.
        path: 'subscription',
        canActivate: [authGuard, roleGuard('Teacher', 'Student', 'Admin')],
        loadComponent: () =>
          import('./pages/my-subscription/my-subscription.component').then((m) => m.MySubscriptionComponent),
      },
      {
        // Issue #1028: kayıttaki plan adımı sonradan (yükseltme); Aboneliğim (#1029) buraya bağlanır.
        path: 'plans',
        canActivate: [authGuard, roleGuard('Teacher', 'Student', 'Admin')],
        loadComponent: () =>
          import('./pages/subscription-plans/subscription-plans.component').then((m) => m.SubscriptionPlansComponent),
      },
      {
        // Issue #417: öğretmen/admin Ayarlar sayfası (dil, hesap/şifre, öğretmen profili bağlantıları).
        // Öğrenci /student-profile'a yönlenir; onay bekleyen öğretmene de açık (approvedTeacherGuard yok).
        path: 'settings',
        canActivate: [authGuard, settingsGuard],
        loadComponent: () => import('./pages/settings/settings.component').then((m) => m.SettingsComponent),
      },
      {
        // Issue #696: öğretmenin baktığı öğrencinin kazanım haritası; kapsam sunucuda.
        path: 'teacher/students/:studentId/outcomes',
        canActivate: [authGuard, roleGuard('Teacher'), approvedTeacherGuard],
        loadComponent: () =>
          import('./pages/teacher-student-outcomes/teacher-student-outcomes.component').then((m) => m.TeacherStudentOutcomesComponent),
      },
      {
        // Issue #95: bağımsız öğretmenin özel ders profili (dersler, ücret, online/yüz yüze).
        // Issue #287: onay bekleyen öğretmene de açık — bağımsız öğretmen başvurusunun formu (backend izin verir).
        path: 'tutor-profile',
        // Issue #384: okula bağlı öğretmen bu sayfayı görmez (menüde de gizli).
        canActivate: [authGuard, roleGuard('Teacher'), independentTeacherGuard],
        loadComponent: () =>
          import('./pages/tutor-profile/tutor-profile.component').then((m) => m.TutorProfileComponent),
      },
      {
        // Issue #95: öğrencinin bağımsız öğretmen araması.
        path: 'tutors',
        canActivate: [authGuard, studentGuard],
        loadComponent: () =>
          import('./pages/tutor-search/tutor-search.component').then((m) => m.TutorSearchComponent),
      },
      {
        // Issue #95: arama sonucundan açılan tekil öğretmen public profili.
        path: 'tutors/:id',
        canActivate: [authGuard, studentGuard],
        loadComponent: () =>
          import('./pages/tutor-search/tutor-public-profile.component').then((m) => m.TutorPublicProfileComponent),
      },
      {
        // Issue #96: takvim öğretmene de açık — onaylı ders randevuları burada görünür.
        path: 'my-calendar',
        canActivate: [authGuard, roleGuard('Student', 'Teacher'), approvedTeacherGuard],
        loadComponent: () =>
          import('./pages/my-calendar/my-calendar.component').then((m) => m.MyCalendarComponent),
      },
      {
        // Issue #96: öğretmenin müsaitlik aralıkları.
        path: 'availability',
        // Issue #418: randevu bağımsız öğretmen özelliği — okula bağlı öğretmen bu sayfayı görmez (menüde de gizli).
        canActivate: [authGuard, roleGuard('Teacher'), approvedTeacherGuard, independentTeacherGuard, entitlementGuard('tutoring')],
        loadComponent: () =>
          import('./pages/teacher-availability/teacher-availability.component').then(
            (m) => m.TeacherAvailabilityComponent
          ),
      },
      {
        // Issue #96: öğretmene gelen randevu talepleri.
        path: 'booking-requests',
        // Issue #418: randevu bağımsız öğretmen özelliği — okula bağlı öğretmen bu sayfayı görmez (menüde de gizli).
        canActivate: [authGuard, roleGuard('Teacher'), approvedTeacherGuard, independentTeacherGuard],
        loadComponent: () =>
          import('./pages/teacher-booking-requests/teacher-booking-requests.component').then(
            (m) => m.TeacherBookingRequestsComponent
          ),
      },
      {
        // Issue #96: öğrencinin kendi randevu talepleri.
        path: 'my-bookings',
        canActivate: [authGuard, studentGuard],
        loadComponent: () =>
          import('./pages/student-bookings/student-bookings.component').then((m) => m.StudentBookingsComponent),
      },
      {
        // Issue #106: öğrencinin öğretmenlerine doğrudan mesajı (öğretmen arama + konuşmalar).
        path: 'teacher-messages',
        canActivate: [authGuard, studentGuard],
        loadComponent: () =>
          import('./pages/teacher-messages/teacher-messages.component').then((m) => m.TeacherMessagesComponent),
      },
      {
        // Issue #106: öğretmenin öğrenci mesajları gelen kutusu (onaysız öğretmen giremez; uç da 403 döner).
        path: 'student-messages',
        canActivate: [authGuard, roleGuard('Teacher'), approvedTeacherGuard],
        loadComponent: () =>
          import('./pages/student-messages/student-messages.component').then((m) => m.StudentMessagesComponent),
      },
      {
        // Issue #420: veli paneli — çocuk seçici (?child=<studentId>) + özet kartları. Login sonrası veli buraya düşer.
        path: 'parent',
        canActivate: [authGuard, parentGuard],
        loadComponent: () =>
          import('./pages/parent-dashboard/parent-dashboard.component').then((m) => m.ParentDashboardComponent),
      },
      {
        // Issue #419: velinin "Çocuklarım" sayfası — davet koduyla çocuk ekleme + bağlı çocuklar (tam panel #420).
        path: 'my-children',
        canActivate: [authGuard, roleGuard('Parent')],
        loadComponent: () =>
          import('./pages/parent-children/parent-children.component').then((m) => m.ParentChildrenComponent),
      },
      {
        // Issue #439: veli çocuğu adına öğretmen arar (öğrenci aramasıyla aynı komponent, veli modu).
        path: 'my-children/:studentId/tutors',
        canActivate: [authGuard, parentGuard],
        loadComponent: () =>
          import('./pages/tutor-search/tutor-search.component').then((m) => m.TutorSearchComponent),
      },
      {
        // Issue #439: veli modunda öğretmen profili — slot seçip çocuk adına talep (veli onayı atlanır).
        path: 'my-children/:studentId/tutors/:id',
        canActivate: [authGuard, parentGuard],
        loadComponent: () =>
          import('./pages/tutor-search/tutor-public-profile.component').then((m) => m.TutorPublicProfileComponent),
      },
      {
        // Issue #97: onaylı bir randevunun video görüşme odası (iki taraf da girer).
        path: 'lessons/:bookingId/video',
        canActivate: [authGuard, roleGuard('Student', 'Teacher'), approvedTeacherGuard],
        loadComponent: () =>
          import('./pages/lesson-video/lesson-video.component').then((m) => m.LessonVideoComponent),
      },
    ],
  },
  { path: '**', redirectTo: 'welcome' },
];
