import { Routes } from '@angular/router';
import { HomeComponent } from './home/home.component';
import { RegisterComponent } from './register/register.component';
import { LoginComponent } from './login/login.component';
import { StudentRegisterComponent } from './student-register/student-register.component';
import { AuthGuard } from './guards/auth.guard';
import { QuestionComponent } from './question/question.component';
import { QuestionViewComponent } from './question-view/question-view.component';
import { TestSolveComponent } from './test-solve/test-solve.component';
import { StudentProfileComponent } from './student-profile/student-profile.component';
import { TestListComponent } from './test-list/test-list.component';

export const routes: Routes = [
  { path: 'register', component: RegisterComponent },
  { path: 'login', component: LoginComponent },
  { path: 'home', component: HomeComponent , canActivate: [AuthGuard]},
  { path: 'student-register', component: StudentRegisterComponent, canActivate: [AuthGuard] },
  { path: 'questions/create', component: QuestionComponent, canActivate: [AuthGuard] },
  { path: 'tests', component: TestListComponent, canActivate: [AuthGuard] },
  { path: 'questions/view', component: QuestionViewComponent, canActivate: [AuthGuard] },
  { path: 'test/:testInstanceId', component: TestSolveComponent, canActivate: [AuthGuard] },  // 🆕 Test çözme sayfası
  { path: 'student-profile', component: StudentProfileComponent, canActivate: [AuthGuard] },  // 🆕 Test çözme sayfası
  { path: '**', redirectTo: 'login' }
];
