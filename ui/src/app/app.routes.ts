import { Routes } from '@angular/router';
import { StudentRegisterComponent } from './pages/student-register/student-register.component';
import { authGuard } from './shared/guards/auth.guard';
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
import { TeacherRegisterComponent } from './pages/teacher-register/teacher-register.component';
import { TestSolveCanvasComponentv2 } from './pages/test-solve/test-solve-canvas-enhanced.component';
import { EnhancedLayoutComponent } from './components/enhanced-layout/enhanced-layout.component';
import { TestCreateEnhancedComponent } from './pages/test-create-enhanced/test-create-enhanced.component';
import { StudyPageComponent } from './components/study-page/study-page.component';
import { DashboardComponent } from './pages/dashboard/dashboard.component';
import { TestSolveCanvasComponentv3 } from './pages/test-solve/test-solve-canvas-v3.component';
import { QuestionTransferComponent } from './pages/question-transfer/question-transfer.component';

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
  // Main app (protected) routes
  // Main app routes, each wrapped in EnhancedLayoutComponent
  {
    path: 'dashboard',
    component: EnhancedLayoutComponent,
    children: [{ path: '', component: DashboardComponent }],
  },
  {
    path: 'tests',
    component: EnhancedLayoutComponent,
    children: [{ path: '', component: WorksheetListComponent, resolve: { worksheets: worksheetListResolver } }],
  },
  {
    path: 'student-register',
    component: EnhancedLayoutComponent,
    children: [{ path: '', component: StudentRegisterComponent }],
  },
  {
    path: 'teacher-register',
    component: EnhancedLayoutComponent,
    children: [{ path: '', component: TeacherRegisterComponent }],
  },
  {
    path: 'question/:id',
    component: EnhancedLayoutComponent,
    children: [{ path: '', component: QuestionComponent }],
  },
  {
    path: 'questioncanvas',
    component: EnhancedLayoutComponent,
    children: [{ path: '', component: QuestionCanvasComponent }],
  },
  {
    path: 'questioncanvas/preview',
    component: EnhancedLayoutComponent,
    children: [{ path: '', component: QuestionCanvasPreviewComponent }],
  },
  {
    path: 'questioncanvas/preview/:testId',
    component: EnhancedLayoutComponent,
    children: [{ path: '', component: QuestionCanvasPreviewComponent }],
  },
  {
    path: 'questioncanvas/:id',
    component: EnhancedLayoutComponent,
    children: [{ path: '', component: QuestionCanvasComponent }],
  },
  {
    path: 'question',
    component: EnhancedLayoutComponent,
    children: [{ path: '', component: QuestionComponent }],
  },
  {
    path: 'imageselect',
    component: EnhancedLayoutComponent,
    children: [{ path: '', component: ImageSelectorComponent }],
  },
  {
    path: 'tests-enhanced',
    component: EnhancedLayoutComponent,
    children: [{ path: '', component: WorksheetListEnhancedComponent, resolve: { worksheets: worksheetListResolver } }],
  },
  {
    path: 'questions/view',
    component: EnhancedLayoutComponent,
    children: [{ path: '', component: QuestionViewComponent }],
  },
  {
    path: 'testsolve/:testInstanceId',
    component: EnhancedLayoutComponent,
    children: [{ path: '', component: TestSolveCanvasComponentv3 }],
  },
  {
    path: 'testsolve/v2/:testInstanceId',
    component: EnhancedLayoutComponent,
    children: [{ path: '', component: TestSolveCanvasComponentv2 }],
  },
  {
    path: 'test/:testId',
    component: EnhancedLayoutComponent,
    children: [{ path: '', component: WorksheetDetailComponent }],
  },
  {
    path: 'student-profile',
    component: EnhancedLayoutComponent,
    children: [{ path: '', component: StudentProfileComponent }],
  },
  {
    path: 'exam',
    component: EnhancedLayoutComponent,
    children: [{ path: '', component: TestCreateEnhancedComponent }],
  },
  {
    path: 'exam/:id',
    component: EnhancedLayoutComponent,
    children: [{ path: '', component: TestCreateEnhancedComponent }],
  },
  {
    path: 'programs',
    component: EnhancedLayoutComponent,
    children: [{ path: '', component: MyProgramsComponent }],
  },
  {
    path: 'programs/:id/detail',
    component: EnhancedLayoutComponent,
    children: [
      {
        path: '',
        loadComponent: () =>
          import('./pages/my-programs/program-detail.component').then((m) => m.ProgramDetailComponent),
      },
    ],
  },
  {
    path: 'program-create',
    component: EnhancedLayoutComponent,
    children: [{ path: '', component: ProgramCreateComponent }],
  },
  {
    path: 'certificates',
    component: EnhancedLayoutComponent,
    children: [{ path: '', component: BadgeThropyComponent }],
  },
  {
    path: 'study',
    component: EnhancedLayoutComponent,
    children: [{ path: '', component: StudyPageComponent }],
  },
  {
    path: 'question-transfer',
    component: EnhancedLayoutComponent,
    children: [{ path: '', component: QuestionTransferComponent }],
  },
  { path: '**', redirectTo: 'welcome' },
];
