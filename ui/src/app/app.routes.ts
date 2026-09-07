import { Routes } from '@angular/router';
import { RegisterWizardComponent } from './pages/register/register-wizard.component';
import { authGuard } from './shared/guards/auth.guard';
import { adminGuard } from './shared/guards/admin.guard';
import { studentGuard } from './shared/guards/student.guard';
import { roleGuard } from './shared/guards/role.guard';
import { QuestionComponent } from './pages/question/question.component';
import { QuestionViewComponent } from './pages/question-view/question-view.component';
import { StudentProfileComponent } from './pages/student-profile/student-profile.component';
import { WorksheetListComponent } from './pages/worksheet-list/worksheet-list.component';
import { WorksheetListEnhancedComponent } from './pages/worksheet-list/worksheet-list-enhanced.component';
import { worksheetListResolver } from './shared/resolvers/worksheet-resolver';
import { ImageSelectorComponent } from './pages/image-selector/image-selector.component';
import { QuestionCanvasComponent } from './pages/question/question-canvas.component';
import { QuestionCanvasPreviewComponent } from './pages/question/question-canvas-preview.component';
import { WorksheetDetailComponent } from './pages/worksheet-detail/worksheet-detail.component';
import { ProgramCreateComponent } from './pages/program-create/program-create.component';
import { MyProgramsComponent } from './pages/my-programs/my-programs.component';
import { BadgeThropyComponent } from './shared/components/badge-thropy/badge-thropy.component';
import { TestSolveCanvasComponentv2 } from './pages/test-solve/test-solve-canvas-enhanced.component';
import { EnhancedLayoutComponent } from './components/enhanced-layout/enhanced-layout.component';
import { TestCreateEnhancedComponent } from './pages/test-create-enhanced/test-create-enhanced.component';
import { StudyPageComponent } from './components/study-page/study-page.component';
import { DashboardComponent } from './pages/dashboard/dashboard.component';
import { TestSolveCanvasComponentv3 } from './pages/test-solve/test-solve-canvas-v3.component';
import { QuestionTransferComponent } from './pages/question-transfer/question-transfer.component';
import { StudyPagesComponent } from './pages/study-pages/study-pages.component';
import { StudyPageEditorComponent } from './pages/study-pages/study-page-editor.component';

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
  { path: '', redirectTo: 'welcome', pathMatch: 'full' },
  // Back-compat: the old per-role routes now land on the wizard at step 2.
  { path: 'student-register', redirectTo: () => '/register?role=student' },
  { path: 'teacher-register', redirectTo: () => '/register?role=teacher' },
  { path: 'parent-register', redirectTo: () => '/register?role=parent' },
  // Main app (protected) routes — all nested under a single EnhancedLayoutComponent
  // instance so the sidenav is not destroyed/recreated on every navigation.
  {
    path: '',
    component: EnhancedLayoutComponent,
    children: [
      { path: 'dashboard', component: DashboardComponent, canActivate: [authGuard] },
      {
        path: 'tests',
        component: WorksheetListComponent,
        canActivate: [authGuard],
        resolve: { worksheets: worksheetListResolver },
      },
      // Two-step onboarding: step 1 role picker (skipped when the role is known
      // from ?role= or an already-assigned realm role), step 2 the role form.
      { path: 'register', component: RegisterWizardComponent },
      { path: 'question/:id', component: QuestionComponent, canActivate: [authGuard] },
      { path: 'questioncanvas', component: QuestionCanvasComponent, canActivate: [authGuard] },
      { path: 'questioncanvas/preview', component: QuestionCanvasPreviewComponent, canActivate: [authGuard] },
      {
        path: 'questioncanvas/preview/:testId',
        component: QuestionCanvasPreviewComponent,
        canActivate: [authGuard],
      },
      { path: 'questioncanvas/:id', component: QuestionCanvasComponent, canActivate: [authGuard] },
      { path: 'question', component: QuestionComponent, canActivate: [authGuard] },
      { path: 'imageselect', component: ImageSelectorComponent, canActivate: [authGuard] },
      {
        path: 'tests-enhanced',
        component: WorksheetListEnhancedComponent,
        canActivate: [authGuard],
        resolve: { worksheets: worksheetListResolver },
      },
      { path: 'questions/view', component: QuestionViewComponent, canActivate: [authGuard] },
      { path: 'testsolve/:testInstanceId', component: TestSolveCanvasComponentv3, canActivate: [authGuard] },
      { path: 'testsolve/v2/:testInstanceId', component: TestSolveCanvasComponentv2, canActivate: [authGuard] },
      { path: 'test/:testId', component: WorksheetDetailComponent, canActivate: [authGuard] },
      { path: 'student-profile', component: StudentProfileComponent, canActivate: [authGuard] },
      { path: 'exam', component: TestCreateEnhancedComponent, canActivate: [authGuard, roleGuard('Teacher')] },
      { path: 'exam/:id', component: TestCreateEnhancedComponent, canActivate: [authGuard, roleGuard('Teacher')] },
      { path: 'programs', component: MyProgramsComponent, canActivate: [authGuard, roleGuard('Student')] },
      {
        path: 'programs/:id/detail',
        loadComponent: () =>
          import('./pages/my-programs/program-detail.component').then((m) => m.ProgramDetailComponent),
        canActivate: [authGuard, roleGuard('Student')],
      },
      { path: 'program-create', component: ProgramCreateComponent, canActivate: [authGuard, roleGuard('Student')] },
      { path: 'certificates', component: BadgeThropyComponent, canActivate: [authGuard] },
      { path: 'study', component: StudyPageComponent, canActivate: [authGuard, roleGuard('Student')] },
      { path: 'study-pages', component: StudyPagesComponent, canActivate: [authGuard, roleGuard('Teacher')] },
      { path: 'study-pages/new', component: StudyPageEditorComponent, canActivate: [authGuard, roleGuard('Teacher')] },
      { path: 'study-pages/:id', component: StudyPageEditorComponent, canActivate: [authGuard, roleGuard('Teacher')] },
      {
        path: 'question-transfer',
        component: QuestionTransferComponent,
        canActivate: [authGuard, roleGuard('Teacher')],
      },
      {
        path: 'assignment-permission-requests',
        canActivate: [authGuard, roleGuard('Teacher')],
        loadComponent: () =>
          import('./pages/assignment-permission-requests/assignment-permission-requests.component').then(
            (m) => m.AssignmentPermissionRequestsComponent
          ),
      },
      {
        path: 'admin',
        canActivate: [authGuard, adminGuard],
        loadComponent: () => import('./pages/admin/admin-home/admin-home.component').then((m) => m.AdminHomeComponent),
      },
      {
        path: 'my-calendar',
        canActivate: [authGuard, studentGuard],
        loadComponent: () =>
          import('./pages/my-calendar/my-calendar.component').then((m) => m.MyCalendarComponent),
      },
    ],
  },
  { path: '**', redirectTo: 'welcome' },
];
