import { Component, DestroyRef, inject, OnInit } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { combineLatest, finalize, map, switchMap, take } from 'rxjs';
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
import { NgxChartsModule, Color, ScaleType } from '@swimlane/ngx-charts';
import { FormsModule } from '@angular/forms';
import { BadgeThropyComponent } from '../../shared/components/badge-thropy/badge-thropy.component';
import { TestService } from '../../services/test.service';
import { UserThemeSwitcherComponent } from '../../components/user-theme-switcher/user-theme-switcher.component';
import { BadgeService, UserActivityResponse } from '../../services/badge.service';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { LocaleService } from '../../services/locale.service';
import { isAppLocale, localeDefinitionOf } from '../../models/locale';
import { currentUserId } from '../../shared/utils/current-user-id.util';
import { AccountSecurityCardComponent } from '../../shared/components/account-security-card/account-security-card.component';

/** Sayfanin Transloco scope'u: `public/i18n/student-profile/<lang>.json` (issue #183). */
const SCOPE = 'student-profile';

/** "Ödevler ve Sınavlar" özet kutularının scope'a göreli başlık anahtarları (sıra kutu sırasıdır). */
export const STAT_LABEL_KEYS = [
  'stats.completedTests',
  'stats.studyMinutes',
  'stats.solvedQuestions',
  'stats.correctAnswers',
];

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
    NgxChartsModule,
    BadgeThropyComponent,
    UserThemeSwitcherComponent,
    TranslocoDirective,
    AccountSecurityCardComponent,
  ],
  providers: [provideTranslocoScope(SCOPE)],
})
export class StudentProfileComponent implements OnInit {
  testService = inject(TestService);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);

  /** "Haftalik Hedef" sekmesindeki ay basligi; bicimlendirme `date` pipe'i ile dile baglidir. */
  readonly goalMonth = new Date();
  student: StudentProfile | null = null;
  /**
   * BadgeService rapor uclari (`reports/users/{userId}/...`) icin AUTH kullanici id'si (localStorage `user.id`).
   * StudentProfileDto.Id ogrenci kaydinin id'sidir, kullanici id'si degildir; buradan turetilmez (aksi 403).
   */
  reportUserId: number | null = null;
  grades: Grade[] = [];
  activeTab = 0; // Varsayılan olarak ilk sekme açık
  activeTab2 = 1;

  colorScheme: Color = {
    name: 'heatmapScheme',
    selectable: false,
    group: ScaleType.Quantile,
    domain: ['#eff2f5', '#aceebb', '#4ac26b', '#2da44e', '#116329'],
  };

  colorScheme3 = {
    name: 'viridis',
    selectable: false,
    group: ScaleType.Linear,
    domain: [
      '#440154', // çok düşük
      '#3B528B',
      '#21908C',
      '#5DC863',
      '#FDE725', // çok yüksek
    ],
  };

  plasmaSchema = {
    name: 'plasma',
    selectable: false,
    group: ScaleType.Quantile,
    domain: ['#0D0887', '#6A00A8', '#B12A90', '#E16462', '#FCA636'],
  };

  neonSchema = {
    name: 'neon',
    selectable: false,
    group: ScaleType.Linear,
    domain: [
      '#00FFC8', // çok düşük
      '#00E0B2',
      '#00B38A',
      '#007B56',
      '#003F2D', // çok yüksek
    ],
  };

  infernoScheme = {
    name: 'inferno',
    selectable: false,
    group: ScaleType.Linear,
    domain: [
      '#000004', // en düşük
      '#420A68',
      '#932667',
      '#DD513A',
      '#FE9F6D',
      '#FDE725', // en yüksek
    ],
  };

  cividisScheme = {
    name: 'cividis',
    selectable: false,
    group: ScaleType.Linear,
    domain: ['#00204C', '#134E8C', '#006F7D', '#44AA6C', '#FDAE50'],
  };

  sunsetScheme = {
    name: 'sunset',
    selectable: false,
    group: ScaleType.Linear,
    domain: [
      '#FFE5B4', // soluk sarı
      '#FFB370',
      '#FF7F3F',
      '#E84F3B',
      '#A6282E',
    ],
  };

  oceanScheme = {
    name: 'ocean',
    selectable: false,
    group: ScaleType.Linear,
    domain: ['#A6FFEA', '#4DFFDB', '#00E3B7', '#009780', '#00554A'],
  };

  /*
  domain: [
    'var(--contribution-default-bgColor-0)', 
    'var(--contribution-default-bgColor-0)',
    'var(--contribution-default-bgColor-1)',
    'var(--contribution-default-bgColor-1)',
    'var(--contribution-default-bgColor-2)',
    'var(--contribution-default-bgColor-3)',
    'var(--contribution-default-bgColor-4)']
  */
  //

  /**
   * Issue #381: ısı haritalarının hafta adı → x ekseni tik metni. Hafta adı aktif dilde haftanın başladığı gündür
   * ("06 Eki"); tik yalnız ayın ilk haftasında ay adını gösterir. Eşleme veri üretilirken doldurulur, böylece
   * biçimlendirici durumsuzdur (ngx-charts tikleri her çizimde yeniden ister).
   */
  private readonly weekTickLabels = new Map<string, string>();

  activityDataFromApi: Array<{ name: string; series: Array<{ name: string; value: number; extra?: any }> }> = [];
  activityApiLoading = false;
  activityApiError = false;

  colorScheme2: Color = {
    name: 'cool',
    selectable: false,
    group: ScaleType.Linear,
    domain: ['#5AA454', '#E44D25', '#CFC0BB', '#7aa3e5', '#a8385d', '#aae3f5'],
  };
  cardColor: string = '#232837';

  single: Array<{ name: string; value: number }> = [];

  /** Hafta adından x ekseni tikini üretir (ayın ilk haftasında ay adı, diğerlerinde boş) — bkz. `weekTickLabels`. */
  readonly xAxisTickFormatting = (val: string): string => this.weekTickLabels.get(val) ?? '';

  /** Haftanın adı (aktif dilde başlangıç günü) ve x ekseni tik metni kaydı. */
  private weekLabel(weekStart: Date, locale: string): string {
    const label = weekStart.toLocaleDateString(locale, { day: '2-digit', month: 'short' });
    const tick = weekStart.getDate() <= 7 ? weekStart.toLocaleDateString(locale, { month: 'short' }) : '';
    this.weekTickLabels.set(label, tick);
    return label;
  }

  constructor(
    private studentService: StudentService,
    private route: ActivatedRoute,
    private router: Router,
    private snackBar: MatSnackBar,
    private badgeService: BadgeService
  ) {}

  onSelect(event: any) {
    console.log(event);
  }

  public chartClicked(e: any): void {
    console.log(e);
  }

  ngOnInit() {
    this.reportUserId = currentUserId();

    this.studentService.loadGrades().subscribe((response) => {
      this.grades = response;
    });

    this.studentService.getProfile().subscribe((response) => {
      this.student = response;
    });

    // Issue #381: kutu başlıkları scope yüklendikten SONRA çözülür (senkron translate scope hazır değilken ham anahtar
    // döndürüyordu) ve dil değişince yeniden üretilir.
    combineLatest([
      this.testService.studentStatistics(),
      this.transloco.selectTranslate<string[]>(STAT_LABEL_KEYS, {}, SCOPE),
    ])
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(([response, labels]) => {
        const total = response.total;
        const values = [
          total.completedTests,
          total.totalTimeSpentMinutes,
          total.totalCorrectAnswers + total.totalWrongAnswers,
          total.totalCorrectAnswers,
        ];
        this.single = values.map((value, index) => ({ name: labels[index], value }));
      });

    if (this.reportUserId) {
      this.loadUserActivityHeatmap(this.reportUserId);
    }
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

  activityHeatmapTooltip = (tooltip: any): string => {
    if (!tooltip) {
      return '';
    }

    const cell = tooltip.cell ?? tooltip.data ?? tooltip;
    const extra = cell?.extra ?? {};
    const date = extra.date ? new Date(extra.date) : new Date();
    const dateLabel = extra.label ?? date.toLocaleDateString(this.intlLocale, { day: '2-digit', month: 'short' });
    const duration = this.formatDuration(extra.totalTimeSeconds ?? 0);

    return [
      dateLabel,
      `${this.text('heatmap.tooltip.questions')}: ${extra.questionCount ?? 0}`,
      `${this.text('heatmap.tooltip.correct')}: ${extra.correctCount ?? 0}`,
      `${this.text('heatmap.tooltip.duration')}: ${duration}`,
      `${this.text('heatmap.tooltip.activityScore')}: ${cell?.value ?? 0}`,
    ].join('\n');
  };

  private loadUserActivityHeatmap(userId: number): void {
    if (!userId || userId <= 0) {
      return;
    }

    this.activityApiLoading = true;
    this.activityApiError = false;

    // Issue #381: gün/hafta etiketleri aktif dilde; dil değişince (istatistik kutuları gibi) yeniden üretilir.
    this.badgeService
      .getUserActivity(userId)
      .pipe(
        finalize(() => (this.activityApiLoading = false)),
        switchMap((response) => this.transloco.langChanges$.pipe(map((lang) => ({ response, lang })))),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: ({ response, lang }) => {
          this.weekTickLabels.clear();
          this.activityDataFromApi = this.transformActivityToHeatmap(response, this.intlLocaleFor(lang));
        },
        error: (error) => {
          console.error('Öğrenci aktivite verisi alınamadı', error);
          this.activityApiError = true;
          this.activityDataFromApi = [];
        },
      });
  }

  private transformActivityToHeatmap(
    response: UserActivityResponse,
    locale: string
  ): Array<{ name: string; series: any[] }> {
    const totalWeeks = 52;
    const daysPerWeek = 7;

    const rawEndDate = response?.endDateUtc ? this.normalizeDate(response.endDateUtc) : this.normalizeDate(new Date());
    const alignedEndDate = this.endOfWeek(rawEndDate);
    const alignedStartDate = this.addDays(alignedEndDate, -(totalWeeks * daysPerWeek - 1));

    const activityMap = new Map<string, (typeof response.days)[number]>();
    (response?.days ?? []).forEach((day) => {
      activityMap.set(this.toIsoDateKey(this.normalizeDate(day.dateUtc)), day);
    });

    const heatmap: Array<{ name: string; series: any[] }> = [];

    for (let weekIndex = 0; weekIndex < totalWeeks; weekIndex++) {
      const weekStart = this.addDays(alignedStartDate, weekIndex * daysPerWeek);
      const weekSeries = Array.from({ length: daysPerWeek }, (_, dayOffset) => {
        const currentDate = this.addDays(weekStart, dayOffset);
        const key = this.toIsoDateKey(currentDate);
        const dayActivity = activityMap.get(key);
        const activityScore = dayActivity?.activityScore ?? 0;

        if (currentDate > rawEndDate) {
          return null;
        }

        return {
          name: this.formatDayLabel(currentDate, locale),
          value: activityScore,
          extra: {
            date: currentDate.toISOString(),
            label: currentDate.toLocaleDateString(locale, { day: '2-digit', month: 'short' }),
            questionCount: dayActivity?.questionCount ?? 0,
            correctCount: dayActivity?.correctCount ?? 0,
            totalTimeSeconds: dayActivity?.totalTimeSeconds ?? 0,
            totalPoints: dayActivity?.totalPoints ?? 0,
          },
        };
      }).filter((entry): entry is { name: string; value: number; extra: any } => entry !== null);

      heatmap.push({
        name: this.weekLabel(weekStart, locale),
        series: weekSeries.slice().reverse(), // reverse so Monday renders on top
      });
    }

    return heatmap;
  }

  private addDays(date: Date, days: number): Date {
    const result = new Date(date);
    result.setDate(result.getDate() + days);
    result.setHours(0, 0, 0, 0);
    return result;
  }

  private normalizeDate(date: string | Date): Date {
    const normalized = new Date(date);
    normalized.setHours(0, 0, 0, 0);
    return normalized;
  }

  private toIsoDateKey(date: Date): string {
    const utc = Date.UTC(date.getFullYear(), date.getMonth(), date.getDate());
    return new Date(utc).toISOString().split('T')[0];
  }

  private formatDayLabel(date: Date, locale: string): string {
    return date.toLocaleDateString(locale, { weekday: 'short' });
  }

  /** Intl cagrilarinda kullanilacak aktif dil (issue #183). */
  private get intlLocale(): string {
    return this.localeService.localeDefinition().angularLocale;
  }

  /** Transloco dil kodunun Intl karşılığı; tanınmayan kodda LocaleService'in aktif dili. */
  private intlLocaleFor(lang: string): string {
    return isAppLocale(lang) ? localeDefinitionOf(lang).angularLocale : this.intlLocale;
  }

  /** Sozlukten senkron metin; sayfa sablonu render oldugunda scope yuklu olur. */
  private text(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${SCOPE}.${key}`, params) ?? '';
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

  private formatDuration(totalSeconds: number): string {
    if (!totalSeconds) {
      return this.text('duration.seconds', { seconds: 0 });
    }

    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;

    if (minutes && seconds) {
      return this.text('duration.minutesAndSeconds', { minutes, seconds });
    }

    if (minutes) {
      return this.text('duration.minutes', { minutes });
    }

    return this.text('duration.seconds', { seconds });
  }

  private endOfWeek(date: Date): Date {
    const target = new Date(date);
    const distanceToSunday = (7 - target.getDay()) % 7;
    return this.addDays(target, distanceToSunday);
  }
}
