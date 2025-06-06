import { Routes } from '@angular/router';
import { RegisterComponent } from './pages/register/register.component';
import { LoginComponent } from './pages/login/login.component';
import { StudentRegisterComponent } from './pages/student-register/student-register.component';
import { authGuard } from './shared/guards/auth.guard';
import { QuestionComponent } from './pages/question/question.component';
import { QuestionViewComponent } from './pages/question-view/question-view.component';
import { StudentProfileComponent } from './pages/student-profile/student-profile.component';
import { TestCreateComponent } from './pages/test-create/test-create.component';
import { HomeComponent } from './home/home.component';
import { WorksheetListComponent } from './pages/worksheet-list/worksheet-list.component';
import { WorksheetListEnhancedComponent } from './pages/worksheet-list/worksheet-list-enhanced.component';
import { worksheetListResolver } from './shared/resolvers/worksheet-resolver';
import { ImageSelectorComponent } from './pages/image-selector/image-selector.component';
import { QuestionCanvasComponent } from './pages/question/question-canvas.component';
import { TestSolveCanvasComponent } from './pages/test-solve/test-solve-canvas.component';
import { WorksheetDetailComponent } from './pages/worksheet-detail/worksheet-detail.component';
import { AppComponent } from './app.component';
import { PublicLayoutComponent } from './pages/public/public-layout/public-layout.component';
import { LayoutComponent } from './pages/layout/layout.component';
import { ProgramCreateComponent } from './pages/program-create/program-create.component';
import { MyProgramsComponent } from './pages/my-programs/my-programs.component';
import { BadgeThropyComponent } from './shared/components/badge-thropy/badge-thropy.component';
import { TeacherRegisterComponent } from './pages/teacher-register/teacher-register.component';
import { CallbackComponent } from './pages/callback/callback.component';
import { LogoutComponent } from './pages/logout/logout.component';
import { TestSolveCanvasComponentv2 } from './pages/test-solve/test-solve-canvas-enhanced.component';
import { EnhancedLayoutComponent } from './components/enhanced-layout/enhanced-layout.component';
import { TestCreateEnhancedComponent } from './pages/test-create-enhanced/test-create-enhanced.component';
import { StudyPageComponent } from './components/study-page/study-page.component';

export const routes: Routes = [
  {
    path: '',
    component: EnhancedLayoutComponent,
    canActivate: [authGuard],
    children: [
      { path: 'student-register', component: StudentRegisterComponent },
      { path: 'teacher-register', component: TeacherRegisterComponent },
      { path: 'question/:id', component: QuestionComponent },
      { path: 'questioncanvas', component: QuestionCanvasComponent },
      { path: 'questioncanvas/:id', component: QuestionCanvasComponent },
      { path: 'question', component: QuestionComponent },
      { path: 'imageselect', component: ImageSelectorComponent },
      { path: 'tests', component: WorksheetListComponent, resolve: { worksheets: worksheetListResolver } },
      {
        path: 'tests-enhanced',
        component: WorksheetListEnhancedComponent,
        resolve: { worksheets: worksheetListResolver },
      },
      { path: 'questions/view', component: QuestionViewComponent },
      // { path: 'testsolve/:testInstanceId', component: TestSolveCanvasComponent }, // 🆕 Test çözme sayfası
      { path: 'testsolve/:testInstanceId', component: TestSolveCanvasComponentv2 }, // 🆕 Test çözme sayfası
      { path: 'test/:testId', component: WorksheetDetailComponent }, // 🆕 Test çözme sayfası
      { path: 'student-profile', component: StudentProfileComponent }, // 🆕 Test çözme sayfası
      // { path: 'exam', component: TestCreateComponent }, // 🆕 Test çözme sayfası
      { path: 'exam', component: TestCreateEnhancedComponent }, // 🆕 Test çözme sayfası
      { path: 'exam/:id', component: TestCreateComponent }, // 🆕 Test çözme sayfası
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
  {
    path: '',
    component: PublicLayoutComponent,
    children: [
      { path: 'callback', component: CallbackComponent },
      { path: 'logout', component: LogoutComponent },
      { path: 'register', component: RegisterComponent },
      { path: 'login', component: LoginComponent },
    ],
  },
  { path: '**', redirectTo: 'tests' },
];
