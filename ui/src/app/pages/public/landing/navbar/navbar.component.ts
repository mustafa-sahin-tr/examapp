import { ChangeDetectionStrategy, Component, HostListener, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { TranslocoDirective, TranslocoPipe } from '@jsverse/transloco';

import { ColorSchemeService } from '../../../../services/color-scheme.service';
import { LanguageSwitcherComponent } from '../../../../shared/components/language-switcher/language-switcher.component';
import { LandingAnchorDirective } from '../landing-anchor.directive';
import {
  LANDING_LOGIN_URL,
  LANDING_NAV_LINKS,
  LANDING_REGISTER_STUDENT_URL,
  LANDING_REGISTER_TEACHER_URL,
} from '../landing-content';

@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    TranslocoDirective,
    TranslocoPipe,
    LanguageSwitcherComponent,
    LandingAnchorDirective,
  ],
  templateUrl: './navbar.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NavbarComponent {
  readonly colorScheme = inject(ColorSchemeService);

  readonly links = LANDING_NAV_LINKS;
  readonly loginUrl = LANDING_LOGIN_URL;
  readonly registerStudentUrl = LANDING_REGISTER_STUDENT_URL;
  readonly registerTeacherUrl = LANDING_REGISTER_TEACHER_URL;
  readonly menuOpen = signal(false);

  toggleMenu(): void {
    this.menuOpen.update((open) => !open);
  }

  closeMenu(): void {
    this.menuOpen.set(false);
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    this.closeMenu();
  }
}
