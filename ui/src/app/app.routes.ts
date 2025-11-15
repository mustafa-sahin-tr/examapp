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

export const routes: Routes = [
  {
    path: '',
    component: EnhancedLayoutComponent,
    // canActivate: [authGuard],
    children: [
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
      { path: 'student-register', component: StudentRegisterComponent },
      { path: 'teacher-register', component: TeacherRegisterComponent },
      { path: 'question/:id', component: QuestionComponent },
      { path: 'questioncanvas', component: QuestionCanvasComponent },
      { path: 'questioncanvas/:id', component: QuestionCanvasComponent },
      { path: 'question', component: QuestionComponent },
      { path: 'imageselect', component: ImageSelectorComponent },
      { path: 'tests', component: WorksheetListComponent, resolve: { worksheets: worksheetListResolver } },
      { path: 'dashboard', component: DashboardComponent },
      {
        path: 'tests-enhanced',
        component: WorksheetListEnhancedComponent,
        resolve: { worksheets: worksheetListResolver },
      },
      { path: 'questions/view', component: QuestionViewComponent },
      // { path: 'testsolve/:testInstanceId', component: TestSolveCanvasComponent }, // 🆕 Test çözme sayfası
      { path: 'testsolve/:testInstanceId', component: TestSolveCanvasComponentv2 }, // 🆕 Test çözme sayfası
      { path: 'testsolve/v3/:testInstanceId', component: TestSolveCanvasComponentv3 }, // 🆕 Test çözme sayfası
      { path: 'test/:testId', component: WorksheetDetailComponent }, // 🆕 Test çözme sayfası
      { path: 'student-profile', component: StudentProfileComponent }, // 🆕 Test çözme sayfası
      // { path: 'exam', component: TestCreateComponent }, // 🆕 Test çözme sayfası
      { path: 'exam', component: TestCreateEnhancedComponent }, // 🆕 Test çözme sayfası
      { path: 'exam/:id', component: TestCreateEnhancedComponent }, // 🆕 Test çözme sayfası
      { path: 'programs', component: MyProgramsComponent },
      {
        path: 'programs/:id/detail',
        loadComponent: () =>
          import('./pages/my-programs/program-detail.component').then((m) => m.ProgramDetailComponent),
      },
      { path: 'program-create', component: ProgramCreateComponent },
      { path: 'certificates', component: BadgeThropyComponent }, //
      { path: 'study', component: StudyPageComponent },
    ],
  },
  { path: '**', redirectTo: 'tests' },
];
