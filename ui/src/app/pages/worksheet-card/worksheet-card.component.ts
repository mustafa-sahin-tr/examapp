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
import { TestService } from '../../services/test.service';

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
  private readonly testService = inject(TestService);
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
    // Test null veya undefined ise ancak assignment'ın içinde worksheetId dolu ise test'i load et
    if ((!this.test || this.test === null || this.test === undefined) && this.assignment?.worksheetId) {
      this.loadTestFromAssignment();
    } else if (this.test) {
      this.initializeComponent();
    }
  }

  private loadTestFromAssignment(): void {
    if (!this.assignment?.instanceId) return;

    this.testService
      .get(this.assignment.instanceId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (test: Test) => {
          this.test = test;
          this.initializeComponent();
        },
        error: (error) => {
          console.error('Test yüklenirken hata oluştu:', error);
          // Hata durumunda varsayılan değerlerle devam et
          this.initializeComponent();
        },
      });
  }

  private initializeComponent(): void {
    if (this.test) {
      this.styleConfig = CARDSTYLES[this.getTestColor(this.test)];
    }
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

  // Progress Related Methods
  getAnsweredQuestions(): number {
    if (!this.test.instance) return 0;
    return this.test.instance.correctAnswers + this.test.instance.wrongAnswers;
  }

  getQuestionProgressPercentage(): number {
    const totalQuestions = this.test.questionCount || 0;
    const answeredQuestions = this.getAnsweredQuestions();
    if (totalQuestions === 0) return 0;
    return Math.round((answeredQuestions / totalQuestions) * 100);
  }

  getQuestionProgressBackground(): string {
    const percentage = this.getQuestionProgressPercentage();
    const answeredQuestions = this.getAnsweredQuestions();
    const totalQuestions = this.test.questionCount || 0;

    // Hiç başlamamışsa gri
    if (answeredQuestions === 0) {
      return `conic-gradient(#e0e0e0 0deg, #e0e0e0 360deg)`;
    }

    // Tamamlanmışsa yeşil
    if (answeredQuestions === totalQuestions) {
      return `conic-gradient(#4CAF50 0deg, #4CAF50 360deg)`;
    }

    // Devam ediyorsa mavi-gri karışım
    const degrees = (percentage / 100) * 360;
    return `conic-gradient(#2196F3 0deg, #2196F3 ${degrees}deg, #e0e0e0 ${degrees}deg, #e0e0e0 360deg)`;
  }

  getTimeProgress(): string {
    if (!this.test.instance) return '0/0dk';

    const spentMinutes = this.test.instance.durationMinutes;
    const totalMinutes = Math.round(this.test.maxDurationSeconds / 60);

    return `${spentMinutes}/${totalMinutes}dk`;
  }

  getTimeProgressPercentage(): number {
    if (!this.test.instance) return 0;

    const spentMinutes = this.test.instance.durationMinutes;
    const totalMinutes = Math.round(this.test.maxDurationSeconds / 60);

    if (totalMinutes === 0) return 0;
    return Math.min(Math.round((spentMinutes / totalMinutes) * 100), 100);
  }

  getTimeProgressBackground(): string {
    const percentage = this.getTimeProgressPercentage();

    // Hiç başlamamışsa gri
    if (percentage === 0) {
      return `conic-gradient(#e0e0e0 0deg, #e0e0e0 360deg)`;
    }

    // Süre dolmuşsa kırmızı
    if (percentage >= 100) {
      return `conic-gradient(#f44336 0deg, #f44336 360deg)`;
    }

    // %80'den fazlaysa turuncu-kırmızı
    if (percentage >= 80) {
      const degrees = (percentage / 100) * 360;
      return `conic-gradient(#FF9800 0deg, #FF9800 ${degrees}deg, #e0e0e0 ${degrees}deg, #e0e0e0 360deg)`;
    }

    // Normal ilerlemede yeşil
    const degrees = (percentage / 100) * 360;
    return `conic-gradient(#4CAF50 0deg, #4CAF50 ${degrees}deg, #e0e0e0 ${degrees}deg, #e0e0e0 360deg)`;
  }

  getMaxDurationText(): string {
    const totalMinutes = Math.round(this.test.maxDurationSeconds / 60);
    if (totalMinutes < 60) {
      return `${totalMinutes}dk`;
    } else {
      const hours = Math.floor(totalMinutes / 60);
      const minutes = totalMinutes % 60;
      return minutes > 0 ? `${hours}s ${minutes}dk` : `${hours}s`;
    }
  }

  // Gauge Progress Methods
  getGaugeCircumference(): number {
    const radius = 40; // SVG'deki circle radius
    return 2 * Math.PI * radius;
  }

  getGaugeOffset(): number {
    if (!this.test.instance) return this.getGaugeCircumference(); // Hiç başlamamışsa tam kapalı

    const totalQuestions = this.test.questionCount || 0;
    const answeredQuestions = this.getAnsweredQuestions();

    if (totalQuestions === 0) return this.getGaugeCircumference();

    const progressPercentage = (answeredQuestions / totalQuestions) * 100;
    const circumference = this.getGaugeCircumference();

    // Progress'i tersine çevir (dashoffset azaldıkça çizgi artar)
    return circumference - (progressPercentage / 100) * circumference;
  }
}
