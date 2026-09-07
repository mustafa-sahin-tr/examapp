import { Component, inject } from '@angular/core';
import { AuthService } from '../../services/auth.service';
import { DashboardComponent } from './dashboard.component';
import { TeacherDashboardComponent } from '../teacher-dashboard/teacher-dashboard.component';

/**
 * Issue #53 — `/dashboard` için rol ayrımı.
 * Teacher realm rolü varsa öğretmen görünümü, aksi halde mevcut öğrenci dashboard'u.
 * Öğrenci komponentine dokunulmaz; öğretmen için öğrenci API çağrıları hiç tetiklenmez.
 */
@Component({
  selector: 'app-dashboard-switch',
  standalone: true,
  imports: [DashboardComponent, TeacherDashboardComponent],
  template: `
    @if (isTeacher) {
      <app-teacher-dashboard />
    } @else {
      <app-dashboard />
    }
  `,
})
export class DashboardSwitchComponent {
  private readonly auth = inject(AuthService);
  readonly isTeacher = this.auth.hasRealmRole('Teacher');
}
