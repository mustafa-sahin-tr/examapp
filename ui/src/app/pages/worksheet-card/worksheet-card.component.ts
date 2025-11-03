import { CommonModule } from '@angular/common';
import { AfterViewInit, Component, EventEmitter, inject, Input, OnDestroy, OnInit, Output } from '@angular/core';
import { Subject, takeUntil } from 'rxjs';
import { Test } from '../../models/test-instance';
import { HasRoleDirective } from '../../shared/directives/has-role.directive';
import { IsStudentDirective } from '../../shared/directives/is-student.directive';
import { Router } from '@angular/router';
import { CARDSTYLES, StyleConfig } from './worksheet-card-styles';
import { MatCardModule } from '@angular/material/card';
import { SafeHtmlPipe } from '../../services/safehtml';
import { AssignedWorksheet } from '../../models/assignment';
import { MatIconModule } from '@angular/material/icon';
import { ThemeConfigService, WorksheetCardThemeConfig } from '../../services/theme-config.service';

@Component({
  selector: 'app-worksheet-card',
  templateUrl: './worksheet-card.component.html',
  styleUrls: ['./worksheet-card.component.scss'],
  standalone: true,
  imports: [CommonModule, IsStudentDirective, MatCardModule, MatIconModule],
})
export class WorksheetCardComponent implements OnInit, OnDestroy {
  @Input() test!: Test;
  @Input() assignment?: AssignedWorksheet; // Assignment durumu için
  @Output() cardClick = new EventEmitter<number>();
  @Input() size: string = '225px'; // Default size
  @Input() color: string = 'purple'; // Default color
  @Input() transform: string = 'scale(1)'; // Default transform
  @Input() centerLabel: string = 'center'; // Default center label
  @Input() styleConfig: StyleConfig = CARDSTYLES['default'];

  @Input() type: string = 'pscard';

  private readonly router = inject(Router);
  private readonly themeService = inject(ThemeConfigService);
  private readonly destroy$ = new Subject<void>();

  themeConfig!: WorksheetCardThemeConfig;

  images = ['honey-back.png', 'rect-back.png', 'triangle-back.png', 'diamond-back.png'];
  public getBackgroundImage() {
    const randomIndex = (this.test.id || 0) % this.images.length;
    return this.images[randomIndex];
  }

  onClick() {
    this.cardClick.emit(this.test.id || 0);
  }

  public getTestColor(test: Test): string {
    if (test.name.toLocaleLowerCase().includes('matema')) {
      return 'blue';
    } else if (test.name.toLocaleLowerCase().includes('fen')) {
      return 'green';
    } else if (test.name.toLocaleLowerCase().includes('hayat')) {
      return 'orange';
    } else if (test.name.toLocaleLowerCase().includes('türkçe')) {
      return 'red';
    }
    return 'default';
  }

  ngOnInit(): void {
    this.styleConfig = CARDSTYLES[this.getTestColor(this.test)];
    this.themeConfig = this.themeService.getCurrentTheme();

    // Theme değişikliklerini dinle
    this.themeService.currentTheme$.pipe(takeUntil(this.destroy$)).subscribe((newTheme: WorksheetCardThemeConfig) => {
      this.themeConfig = newTheme;
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  edit() {
    this.router.navigate(['/exam', this.test.id]);
  }

  getStatusBorderClass(): string {
    if (!this.themeConfig.borders || !this.assignment) {
      return 'border-default';
    }

    const isPersonalAssignment = !this.assignment.isGradeAssignment;
    const prefix = isPersonalAssignment ? 'personal' : 'grade';

    // Bitiş süresine 24 saatten az kalmışsa kırmızı göster (tamamlanmamışsa)
    if (!this.assignment.isCompleted && this.assignment.endAt) {
      const now = new Date();
      const endDate = new Date(this.assignment.endAt);
      const timeDifference = endDate.getTime() - now.getTime();
      const hoursRemaining = timeDifference / (1000 * 60 * 60);

      if (hoursRemaining <= 24 && hoursRemaining > 0) {
        return `border-urgent-${prefix}`;
      }
    }

    if (this.assignment.isCompleted) {
      return `border-completed-${prefix}`;
    }

    if (this.assignment.hasStarted && !this.assignment.isCompleted) {
      return `border-in-progress-${prefix}`;
    }

    return `border-not-started-${prefix}`;
  }

  // 1. Background Gradient Classes
  getGradientClass(): string {
    if (!this.themeConfig.gradient || !this.assignment) return '';

    const isPersonalAssignment = !this.assignment.isGradeAssignment;
    const prefix = isPersonalAssignment ? 'personal' : 'grade';

    if (this.isUrgent()) return `gradient-urgent-${prefix}`;
    if (this.assignment.isCompleted) return `gradient-completed-${prefix}`;
    if (this.assignment.hasStarted && !this.assignment.isCompleted) return `gradient-in-progress-${prefix}`;
    return `gradient-not-started-${prefix}`;
  }

  // 2. Icon Badge
  getAssignmentIcon(): string {
    if (!this.assignment) return '';
    const iconName = this.assignment.isGradeAssignment ? 'group' : 'person';
    return `${iconName}`;
  }

  getStatusIcon(): string {
    if (!this.assignment) return '';

    if (this.isUrgent()) return 'warning';
    if (this.assignment.isCompleted) return 'check_circle';
    if (this.assignment.hasStarted && !this.assignment.isCompleted) return 'schedule';
    return 'pause_circle';
  }

  // 3. Ribbon/Banner Class
  getRibbonClass(): string {
    if (!this.themeConfig.ribbons || !this.assignment) return '';

    const isPersonalAssignment = !this.assignment.isGradeAssignment;
    const prefix = isPersonalAssignment ? 'personal' : 'grade';

    if (this.isUrgent()) return `ribbon-urgent-${prefix}`;
    if (this.assignment.isCompleted) return `ribbon-completed-${prefix}`;
    if (this.assignment.hasStarted && !this.assignment.isCompleted) return `ribbon-in-progress-${prefix}`;
    return `ribbon-not-started-${prefix}`;
  }

  getRibbonText(): string {
    if (!this.assignment) return '';
    return this.assignment.isGradeAssignment ? 'SINIF' : 'KİŞİSEL';
  }

  // 4. Glow/Shadow Class
  getGlowClass(): string {
    if (!this.themeConfig.glowEffects || !this.assignment) return '';

    const isPersonalAssignment = !this.assignment.isGradeAssignment;
    const prefix = isPersonalAssignment ? 'personal' : 'grade';

    if (this.isUrgent()) return `glow-urgent-${prefix}`;
    if (this.assignment.isCompleted) return `glow-completed-${prefix}`;
    if (this.assignment.hasStarted && !this.assignment.isCompleted) return `glow-in-progress-${prefix}`;
    return `glow-not-started-${prefix}`;
  }

  // 5. Transform Class
  getTransformClass(): string {
    if (!this.themeConfig.transformEffects || !this.assignment) return '';

    const isPersonalAssignment = !this.assignment.isGradeAssignment;
    const prefix = isPersonalAssignment ? 'personal' : 'grade';

    if (this.isUrgent()) return `transform-urgent-${prefix}`;
    if (this.assignment.isCompleted) return `transform-completed-${prefix}`;
    if (this.assignment.hasStarted && !this.assignment.isCompleted) return `transform-in-progress-${prefix}`;
    return `transform-not-started-${prefix}`;
  }

  // 6. Progress Bar
  getProgressPercentage(): number {
    if (!this.assignment || !this.assignment.endAt) return 0;

    const start = new Date(this.assignment.startAt);
    const end = new Date(this.assignment.endAt);
    const now = new Date();

    if (now < start) return 0;
    if (now > end || this.assignment.isCompleted) return 100;

    const totalTime = end.getTime() - start.getTime();
    const elapsedTime = now.getTime() - start.getTime();

    return Math.min(100, Math.max(0, (elapsedTime / totalTime) * 100));
  }

  getProgressClass(): string {
    if (!this.themeConfig.progressBar || !this.assignment) return '';

    const isPersonalAssignment = !this.assignment.isGradeAssignment;
    const prefix = isPersonalAssignment ? 'personal' : 'grade';

    if (this.isUrgent()) return `progress-urgent-${prefix}`;
    if (this.assignment.isCompleted) return `progress-completed-${prefix}`;
    if (this.assignment.hasStarted && !this.assignment.isCompleted) return `progress-in-progress-${prefix}`;
    return `progress-not-started-${prefix}`;
  }

  // 7. Typography Class
  getTypographyClass(): string {
    if (!this.themeConfig.typographyEffects || !this.assignment) return '';

    const isPersonalAssignment = !this.assignment.isGradeAssignment;
    const prefix = isPersonalAssignment ? 'personal' : 'grade';

    if (this.isUrgent()) return `typography-urgent-${prefix}`;
    if (this.assignment.isCompleted) return `typography-completed-${prefix}`;
    if (this.assignment.hasStarted && !this.assignment.isCompleted) return `typography-in-progress-${prefix}`;
    return `typography-not-started-${prefix}`;
  }

  // Helper method
  private isUrgent(): boolean {
    if (!this.assignment || this.assignment.isCompleted || !this.assignment.endAt) return false;

    const now = new Date();
    const endDate = new Date(this.assignment.endAt);
    const timeDifference = endDate.getTime() - now.getTime();
    const hoursRemaining = timeDifference / (1000 * 60 * 60);

    return hoursRemaining <= 24 && hoursRemaining > 0;
  }
}
