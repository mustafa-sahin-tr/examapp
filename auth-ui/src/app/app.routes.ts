import { Routes } from '@angular/router';
import { RegisterComponent } from './pages/register/register.component';
import { LoginComponent } from './pages/login/login.component';
import { PublicLayoutComponent } from './pages/public/public-layout/public-layout.component';
import { CallbackComponent } from './pages/callback/callback.component';
import { LogoutComponent } from './pages/logout/logout.component';
import { CompleteProfileComponent } from './pages/complete-profile/complete-profile.component';

export const routes: Routes = [
  {
    path: '',
    component: PublicLayoutComponent,
    children: [
      { path: 'callback', component: CallbackComponent },
      { path: 'logout', component: LogoutComponent },
      { path: 'register', component: RegisterComponent },
      { path: 'login', component: LoginComponent },
      { path: 'complete-profile', component: CompleteProfileComponent },
      // Issue #440: velinin e-postasındaki aktivasyon bağlantısı (anonim; `/app/parent-activate?token=...`).
      {
        path: 'parent-activate',
        loadComponent: () =>
          import('./pages/parent-activate/parent-activate.component').then((m) => m.ParentActivateComponent),
      },
    ],
  },
  { path: '**', redirectTo: 'tests' },
];
