import { Routes } from '@angular/router';
import { RegisterComponent } from './pages/register/register.component';
import { LoginComponent } from './pages/login/login.component';
import { PublicLayoutComponent } from './pages/public/public-layout/public-layout.component';
import { CallbackComponent } from './pages/callback/callback.component';
import { LogoutComponent } from './pages/logout/logout.component';

export const routes: Routes = [  
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
