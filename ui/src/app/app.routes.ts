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
import { worksheetListResolver } from './shared/resolvers/worksheet-resolver';
import { ImageSelectorComponent } from './pages/image-selector/image-selector.component';
import { QuestionCanvasComponent } from './pages/question/question-canvas.component';
import { TestSolveCanvasComponent } from './pages/test-solve/test-solve-canvas.component';
import { WorksheetDetailComponent } from './pages/worksheet-detail/worksheet-detail.component';
import { AppComponent } from './app.component';
import { PublicLayoutComponent } from './pages/public/public-layout/public-layout.component';
import { LayoutComponent } from './pages/layout/layout.component';

export const routes: Routes = [
  { path: '', 
    component: LayoutComponent,
    canActivate: [authGuard],
    children: [
      { path: 'student-register', component: StudentRegisterComponent },
      { path: 'question/:id', component: QuestionComponent },
      { path: 'questioncanvas', component: QuestionCanvasComponent },
      { path: 'questioncanvas/:id', component: QuestionCanvasComponent },
      { path: 'question', component: QuestionComponent},
      { path: 'imageselect', component: ImageSelectorComponent},
      { path: 'tests', component: WorksheetListComponent , resolve: { worksheets : worksheetListResolver } },
      { path: 'questions/view', component: QuestionViewComponent },
      { path: 'testsolve/:testInstanceId', component: TestSolveCanvasComponent },  // 🆕 Test çözme sayfası
      { path: 'test/:testId', component: WorksheetDetailComponent },  // 🆕 Test çözme sayfası
      { path: 'student-profile', component: StudentProfileComponent },  // 🆕 Test çözme sayfası
      { path: 'exam', component: TestCreateComponent},  // 🆕 Test çözme sayfası
      { path: 'exam/:id', component: TestCreateComponent },  // 🆕 Test çözme sayfası      
    ],
  },
  { path: '', 
    component: PublicLayoutComponent,
    children: [
      { path: 'register', component: RegisterComponent },
      { path: 'login', component: LoginComponent }      
    ],
  },
  { path: '**', redirectTo: 'tests' }
  
];
