import { Component, DestroyRef, inject, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { combineLatest, take } from 'rxjs';
import { StudentProfile } from '../../models/student-profile';
import { StudentService } from '../../services/student.service';
import { ActivatedRoute, Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatSelectModule } from '@angular/material/select';
import { Grade } from '../../models/student';
import { LeaderboardComponent } from '../../shared/components/leaderboard/leaderboard.component';
import { SectionHeaderComponent } from '../../shared/components/section-header/section-header.component';
import { MatTabsModule } from '@angular/material/tabs';
import { FormsModule } from '@angular/forms';
import { BadgeThropyComponent } from '../../shared/components/badge-thropy/badge-thropy.component';
import { UserThemeSwitcherComponent } from '../../components/user-theme-switcher/user-theme-switcher.component';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { currentUserId } from '../../shared/utils/current-user-id.util';
import { AccountSecurityCardComponent } from '../../shared/components/account-security-card/account-security-card.component';
import { StudentParentsCardComponent } from '../../shared/components/student-parents-card/student-parents-card.component';
import { WeakTopicsCardComponent } from '../../shared/components/weak-topics-card/weak-topics-card.component';

/** Sayfanin Transloco scope'u: `public/i18n/student-profile/<lang>.json` (issue #183). */
const SCOPE = 'student-profile';

@Component({
  selector: 'app-student-profile',
  templateUrl: './student-profile.component.html',
  standalone: true,
  styleUrls: ['./student-profile.component.scss'],
  imports: [
    CommonModule,
    MatCardModule,
    MatIconModule,
    MatListModule,
    MatSnackBarModule,
    MatSelectModule,
    FormsModule,
    LeaderboardComponent,
    SectionHeaderComponent,
    MatTabsModule,
    BadgeThropyComponent,
    UserThemeSwitcherComponent,
    TranslocoDirective,
    AccountSecurityCardComponent,
    StudentParentsCardComponent,
    WeakTopicsCardComponent,
  ],
  changeDetection: ChangeDetectionStrategy.Eager,
  providers: [provideTranslocoScope(SCOPE)],
})
export class StudentProfileComponent implements OnInit {
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);

  student: StudentProfile | null = null;
  /**
   * BadgeService rapor uclari (`reports/users/{userId}/...`) icin AUTH kullanici id'si (localStorage `user.id`).
   * StudentProfileDto.Id ogrenci kaydinin id'sidir, kullanici id'si degildir; buradan turetilmez (aksi 403).
   */
  reportUserId: number | null = null;
  grades: Grade[] = [];
  activeTab = 0; // Varsayılan olarak ilk sekme açık
  constructor(
    private studentService: StudentService,
    private route: ActivatedRoute,
    private router: Router,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit() {
    this.reportUserId = currentUserId();

    this.studentService.loadGrades().subscribe((response) => {
      this.grades = response;
    });

    this.studentService.getProfile().subscribe((response) => {
      this.student = response;
    });

  }

  changeGrade(): void {
    if (this.student) {
      this.studentService.updateGrade(this.student.gradeId).subscribe(() => {
        this.notify('profile.gradeUpdated');
      });
    }
  }

  onAvatarChange(event: any): void {
    const file = event.target.files[0];
    if (file) {
      this.studentService.updateAvatar(file).subscribe((response) => {
        this.student = response;
        this.notify('profile.avatarUpdated');
      });
    }
  }

  /**
   * Snackbar metni sablon disinda oldugu icin scope once `selectTranslate` ile yuklenir; sozluk
   * hazir oldugunda aksiyon etiketi senkron okunabilir.
   */
  private notify(messageKey: string): void {
    combineLatest([
      this.transloco.selectTranslate<string>(messageKey, {}, SCOPE),
      this.transloco.selectTranslate<string>('actions.ok', {}, SCOPE),
    ])
      .pipe(take(1), takeUntilDestroyed(this.destroyRef))
      .subscribe(([message, action]) => {
        this.snackBar.open(message, action, { duration: 3000 });
      });
  }
}
