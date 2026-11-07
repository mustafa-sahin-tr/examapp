import {
  Component,
  HostListener,
  OnInit,
  computed,
  inject,
  signal,
  ChangeDetectionStrategy
} from '@angular/core';
import { DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { BarChartModule, Color, ScaleType } from '@swimlane/ngx-charts';
import { finalize, take } from 'rxjs';
import { TranslocoDirective, TranslocoPipe, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { LocaleService } from '../../../services/locale.service';
import { AdminService } from '../../../services/admin.service';
import { AdminPendingService } from '../../../services/admin-pending.service';
import { AdminPendingSummary } from '../../../models/admin-pending.model';
import {
  AdminDashboardSummary,
  AdminDashboardTrendPoint,
  AdminDashboardTrends,
} from '../../../models/admin-dashboard.model';
import { dashboardIsoDate } from '../../../shared/utils/dashboard-time-zone.util';
import { parseCssColor, readCssToken, withAlpha } from '../../../shared/utils/css-color.util';

type SummaryCardKey = 'teachers' | 'students' | 'worksheets' | 'questions';

interface SummaryCardViewModel {
  key: SummaryCardKey;
  /** Scope'a göreli çeviri anahtarı; şablonda `t()` ile çözülür. */
  labelKey: string;
  value: number;
  icon: string;
  /** Filtresiz liste ekranı (issue #154); yoksa kart tıklanamaz, düz `div` olarak kalır. */
  route?: string;
}

/** #887: "Bekleyenler" satırı — kuyruk, sayı alanı ve kuyruk sayfası (menüdeki #861 sekmeleriyle aynı rotalar). */
export const ADMIN_PENDING_QUEUES: readonly { key: keyof Omit<AdminPendingSummary, 'total'>; icon: string; route: string }[] = [
  { key: 'teacherApplications', icon: 'how_to_reg', route: '/admin/teacher-approvals' },
  { key: 'schoolApplications', icon: 'school', route: '/admin/student-school-requests' },
  { key: 'questionReports', icon: 'flag', route: '/question-reports' },
  { key: 'commentReports', icon: 'comment', route: '/admin/comment-reports' },
  { key: 'openTextAnswers', icon: 'rate_review', route: '/open-text-reviews' },
];

/** Yönetim ekranlarının ortak Transloco scope'u: `public/i18n/admin/<lang>.json` (issue #183). */
const ADMIN_SCOPE = 'admin';

type TrendKey = 'created' | 'solved';

/** Haftalık çubuk ve giriş grafiği kartlarının ortak başlık/özet alanları. */
interface TrendCardBase {
  title: string;
  icon: string;
  /** Kart başlığının yanında görünen dönem etiketi: "Son 1 yıl" / "Son 30 gün". */
  period: string;
  total: number;
  /** Tüm günler 0 ise "yeterli veri yok" gösterilir. */
  isEmpty: boolean;
  /** Ekran okuyucu için kısa özet cümle (aria-describedby hedefi). */
  summaryText: string;
}

/** Bir heatmap trend kartının şablon için hazır hâli; her ikisi de aynı kart bileşenini kullanır. */
interface TrendCardViewModel extends TrendCardBase {
  key: TrendKey;
  /** #887: son 8 haftanın haftalık toplamları (eskiden yeniye). */
  weeks: TrendWeekBar[];
}

/** #887: haftalık çubuk; `percent` kartın en yüksek haftasına göre (0–100). */
interface TrendWeekBar {
  label: string;
  count: number;
  percent: number;
}

/** Tooltip için çubuğa iliştirilen veri (ngx-charts `extra`, `tooltipTemplate` `model.extra` olarak gelir). */
interface LoginBarExtra {
  /** "10 Ağu 2026" */
  label: string;
}

/**
 * ngx-charts bar-vertical `results` girdisi. `name` x ekseni kategorisi; tekil olması gerektiği için
 * `yyyy-MM-dd` tutulur ve görünen etiket `xAxisTickFormatting` ile üretilir. `value` gerçek giriş sayısıdır
 * (heatmap'ten farklı olarak seviye değil; bar yüksekliği doğrusal ölçekte anlamlı).
 */
interface LoginBarPoint {
  name: string;
  value: number;
  extra: LoginBarExtra;
}

/**
 * Issue #89 — öğrenci login bar chart kartı. Heatmap kartından ayrı şekil: 30 günlük tek seri,
 * sabit `[view]` yok (kap genişliğine uyar), tick'ler seyreltilmiş açık tarih listesi.
 */
interface LoginTrendViewModel extends TrendCardBase {
  /** Tek renkli ordinal şema (`--ms-info-text-medium`). */
  scheme: Color;
  results: LoginBarPoint[];
  /** Gösterilecek tick'lerin `name` değerleri (her N günde bir, bugün her zaman dahil). */
  xAxisTicks: string[];
}

/** Skeleton'da çizilecek kart sayısı: 4 sayaç + 1 AI kartı. */
const SKELETON_CARD_COUNT = 5;

/** Trend bölümü skeleton'u: 2 heatmap + 1 bar chart. */
const TREND_SKELETON_CARD_COUNT = 3;

/** #887: soru oluşturma / çözme trendi son 8 hafta (yıllık ısı haritasının yerine). */
const TREND_WEEKS = 8;
const DAYS_PER_WEEK = 7;
const MS_PER_DAY = 86_400_000;

/** Trend penceresi (gün): 8 hafta; giriş grafiğinin 30 günü de bu serinin içinde. */
const TREND_DAYS = TREND_WEEKS * DAYS_PER_WEEK;

/**
 * Login bar chart penceresi (gün). Backend'e ayrı istek atılmaz; 364 günlük `studentLogin` serisinin
 * son 30 günü dilimlenir (kullanıcı kararı: login trendi 1 aylık bar chart, heatmap değil).
 */
const LOGIN_TREND_DAYS = 30;

/** Bar chart x ekseni tick aralığı (gün): 30 çubukta her gün etiketlenirse "10 Ağu" etiketleri üst üste biner. */
const LOGIN_TICK_STEP_DESKTOP = 3;
const LOGIN_TICK_STEP_MOBILE = 6;

/**
 * Bar chart renk şeması: tek renkli ordinal domain. ngx-charts bar-vertical her çubuğu `name`'e göre
 * ordinal skaladan boyar; domain tek elemanlı olunca tüm çubuklar aynı token rengini alır.
 */
function buildBarScheme(name: string, tokenName: string): Color {
  return {
    name,
    selectable: false,
    group: ScaleType.Ordinal,
    domain: [readCssToken(tokenName)],
  };
}

/*
 * Takvim günü temsili (issue #265): backend gün kovaları Türkiye yerel günüdür (`DASHBOARD_TIME_ZONE`).
 * Bileşen içinde bir takvim günü, o günün **UTC gece yarısı** `Date`'i ile temsil edilir; tüm aritmetik
 * `*UTC*` metotlarıyla, tüm biçimleme `timeZone: 'UTC'` ile yapılır. Böylece sonuç tarayıcının saat
 * diliminden (ve DST'den) bağımsızdır. "Bugün" yalnızca `todayCalendarDate()` ile, dashboard saat
 * diliminden türetilir.
 */

/** `yyyy-MM-dd` → o takvim gününün UTC gece yarısı temsili. */
function parseIsoDate(isoDate: string): Date {
  const [y, m, d] = isoDate.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d));
}

/** Dashboard saat dilimine (backend `Dashboard:TimeZone`) göre bugünün takvim günü. */
function todayCalendarDate(): Date {
  return parseIsoDate(dashboardIsoDate(new Date()));
}

function toIsoDateKey(date: Date): string {
  const y = date.getUTCFullYear();
  const m = String(date.getUTCMonth() + 1).padStart(2, '0');
  const d = String(date.getUTCDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

function addDays(date: Date, days: number): Date {
  const result = new Date(date);
  result.setUTCDate(result.getUTCDate() + days);
  return result;
}

/** x ekseni etiketi: "10 Ağu" (aktif dile göre) */
function formatWeekLabel(weekStart: Date, locale: string): string {
  return weekStart.toLocaleDateString(locale, { day: 'numeric', month: 'short', timeZone: 'UTC' });
}

/** Tooltip tarihi: "10 Ağu 2026" (aktif dile göre) */
function formatTooltipDate(date: Date, locale: string): string {
  return date.toLocaleDateString(locale, { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' });
}

/** Bar chart x ekseni etiketi: `yyyy-MM-dd` → "10 Ağu". Tanınmayan girdi olduğu gibi döner. */
function formatIsoDayTick(isoDate: string, locale: string): string {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(isoDate)) {
    return isoDate;
  }
  return formatWeekLabel(parseIsoDate(isoDate), locale);
}

/**
 * Bar chart y ekseni: giriş sayısı tam sayıdır; d3 küçük aralıkta 0.5, 1.5 gibi ara tick üretebilir,
 * onlar boş döner (`roundDomains` üst sınırı yuvarlar ama ara tick'leri garanti etmez).
 */
function formatIntegerTick(value: number): string {
  return Number.isInteger(value) ? String(value) : '';
}

/**
 * Serinin sondan başlayarak her `step` günde bir `name`'ini seçer; bugün (son eleman) her zaman dahil.
 * Baştan sayılsaydı en güncel gün etiketsiz kalabilirdi.
 */
function pickTickNames(points: LoginBarPoint[], step: number): string[] {
  const ticks: string[] = [];
  for (let i = points.length - 1; i >= 0; i -= step) {
    ticks.unshift(points[i].name);
  }
  return ticks;
}

/**
 * `yyyy-MM-dd` → bugünden kaç gün önce (0 = bugün). "Bugün" backend gün kovalarıyla aynı tanımdır: dashboard
 * saat dilimindeki (Türkiye, #265) takvim günü — tarayıcının saat dilimi değil. Aksi hâlde TR gece yarısı
 * civarında ya da farklı dilimdeki bir tarayıcıda "bugün"/"dün" backend'e göre kayardı.
 */
function daysAgo(isoDate: string): number {
  const then = parseIsoDate(isoDate).getTime();
  const today = todayCalendarDate().getTime();
  return Math.max(0, Math.round((today - then) / MS_PER_DAY));
}

/** "bugün" / "dün" / "N gün önce" — çeviri anahtarı ve parametresi. */
function describeDaysAgoKey(isoDate: string): { key: string; count: number } {
  const ago = daysAgo(isoDate);
  if (ago === 0) return { key: 'trends.today', count: 0 };
  if (ago === 1) return { key: 'trends.yesterday', count: 1 };
  return { key: 'trends.daysAgo', count: ago };
}

/** Eşitlikte en güncel günü seç (tarihler artan sıralı → sondan tarama). Boş dizi için `undefined`. */
function findPeak(points: AdminDashboardTrendPoint[]): AdminDashboardTrendPoint | undefined {
  let peak = points[points.length - 1];
  for (let i = points.length - 1; i >= 0; i--) {
    if (points[i].count > peak.count) {
      peak = points[i];
    }
  }
  return peak;
}

/**
 * Issue #86 / #88 / #89 — Admin dashboard.
 * Phase 1: sayaç kartları (summary). Phase 2: son 8 haftanın soru oluşturma / çözme haftalık çubukları
 * (#887; eskiden 52 haftalık heatmap). #887: üstte "Bekleyenler" satırı ve sistem durumu.
 * Phase 3 (#89): son 30 günün öğrenci login bar chart'ı; aynı trends isteğinden beslenir.
 * İki bölümün loading/error/data durumları birbirinden bağımsızdır; trend endpoint'i düşerse
 * yalnızca trend bölümü hata gösterir, sayaç kartları etkilenmez.
 */
@Component({
  selector: 'app-admin-dashboard',
  standalone: true,
  imports: [
    DecimalPipe,
    NgTemplateOutlet,
    RouterLink,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    BarChartModule,
    TranslocoDirective,
    TranslocoPipe,
  ],
  providers: [provideTranslocoScope(ADMIN_SCOPE)],
  templateUrl: './admin-dashboard.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./admin-dashboard.component.scss'],
})
export class AdminDashboardComponent implements OnInit {
  private readonly adminService = inject(AdminService);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);

  /** Intl çağrıları için aktif dilin locale kodu ('tr' | 'en-US'). */
  private get intlLocale(): string {
    return this.localeService.localeDefinition().angularLocale;
  }

  /** Heatmap kaydırma kapları (kart başına bir tane); bkz. `scrollHeatmapsToLatestWeek`. */

  // ── Phase 1: sayaçlar ──────────────────────────────────────────────────────
  private readonly pendingService = inject(AdminPendingService);
  /** #887: bekleyen kuyruk sayıları (menü rozetiyle aynı kaynak); yüklenemezse satır gizli. */
  readonly pending = this.pendingService.summary;
  readonly pendingQueues = ADMIN_PENDING_QUEUES;

  /** #887: sistem durumu — son sınıflandırma ("12 dk önce") ve yayımlanamayan olay uyarısı. */
  readonly lastClassification = computed(() => {
    const at = this.summary()?.lastAiClassificationAt;
    if (!at) return null;
    const minutes = Math.max(0, Math.round((Date.now() - new Date(at).getTime()) / 60_000));
    const rtf = new Intl.RelativeTimeFormat(this.intlLocale, { numeric: 'auto' });
    if (minutes < 60) return rtf.format(-minutes, 'minute');
    if (minutes < 60 * 48) return rtf.format(-Math.round(minutes / 60), 'hour');
    return rtf.format(-Math.round(minutes / 1440), 'day');
  });

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly summary = signal<AdminDashboardSummary | null>(null);

  readonly skeletonCards = Array.from({ length: SKELETON_CARD_COUNT }, (_, i) => i);
  readonly trendSkeletonCards = Array.from({ length: TREND_SKELETON_CARD_COUNT }, (_, i) => i);

  /** Platformda hiç veri yoksa: kartlar 0 gösterir, ayırt edici boş-durum metni çıkar. */
  readonly isEmpty = computed(() => {
    const s = this.summary();
    return (
      !!s &&
      s.teacherCount === 0 &&
      s.studentCount === 0 &&
      s.worksheetCount === 0 &&
      s.questionCount === 0
    );
  });

  /** Dört düz sayaç kartı; AI kartı ayrı işlenir (oran + ilerleme çubuğu). */
  readonly cards = computed<SummaryCardViewModel[]>(() => {
    const s = this.summary();
    if (!s) {
      return [];
    }
    return [
      { key: 'teachers', labelKey: 'cards.teachers', value: s.teacherCount, icon: 'school', route: '/admin/teachers' },
      { key: 'students', labelKey: 'cards.students', value: s.studentCount, icon: 'groups', route: '/admin/students' },
      { key: 'worksheets', labelKey: 'cards.worksheets', value: s.worksheetCount, icon: 'assignment' },
      { key: 'questions', labelKey: 'cards.questions', value: s.questionCount, icon: 'quiz' },
    ];
  });

  /** Hiç soru yoksa AI kartı oran yerine "Henüz soru yok" gösterir. */
  readonly hasQuestions = computed(() => (this.summary()?.questionCount ?? 0) > 0);

  /**
   * Issue #440/#441: "Veli durumu" kartının sayaçları (hedef küme, hoşgörüde, süresi dolmuş, aktivasyon bekleyen). Sunucu alanları
   * göndermiyorsa (eski API) kart gösterilmez.
   */
  readonly parentStatusRows = computed<{ key: string; labelKey: string; value: number }[] | null>(() => {
    const s = this.summary();
    if (!s || typeof s.studentsWithoutParent !== 'number') {
      return null;
    }
    return [
      { key: 'withoutParent', labelKey: 'parentStatus.withoutParent', value: s.studentsWithoutParent },
      { key: 'inGrace', labelKey: 'parentStatus.inGrace', value: s.studentsInParentGrace ?? 0 },
      { key: 'graceExpired', labelKey: 'parentStatus.graceExpired', value: s.studentsParentGraceExpired ?? 0 },
      {
        key: 'awaitingActivation',
        labelKey: 'parentStatus.awaitingActivation',
        value: s.studentsAwaitingParentActivation ?? 0,
      },
    ];
  });

  /** 0..100 arası, ilerleme çubuğu genişliği ve rozet için. Backend 0..1 döner. */
  readonly aiPercent = computed(() => {
    const ratio = this.summary()?.aiClassifiedRatio ?? 0;
    return Math.min(100, Math.max(0, ratio * 100));
  });

  // ── Phase 2: trendler (summary'den bağımsız durum seti) ────────────────────
  readonly trendLoading = signal(true);
  readonly trendError = signal<string | null>(null);
  readonly trends = signal<AdminDashboardTrends | null>(null);

  // Viewport takibi ve heatmap boyutu: dashboard.component `activityHeatmapView` ile aynı ölçekleme.
  readonly viewportWidth = signal(typeof window !== 'undefined' ? window.innerWidth : 1280);
  readonly isMobileViewport = computed(() => this.viewportWidth() < 768);

  /**
   * Renkler CSS token'dan bir kez okunur: ngx-charts `scheme.domain` `var(--x)` kabul etmez,
   * çözümlenmiş değere ihtiyaç duyar. Tema değişimi bu sayfada beklenmediği için cache yeterli.
   */
  private readonly loginScheme = buildBarScheme('trend-login', '--ms-info-text-medium');

  readonly trendCards = computed<TrendCardViewModel[]>(() => {
    const t = this.trends();
    if (!t) {
      return [];
    }
    return [
      this.buildTrendCard(
        'created',
        this.text('trends.createdTitle'),
        'add_circle_outline',
        this.text('trends.verbCreated'),
        t.questionCreated,
      ),
      this.buildTrendCard(
        'solved',
        this.text('trends.solvedTitle'),
        'task_alt',
        this.text('trends.verbSolved'),
        t.questionSolved,
      ),
    ];
  });

  /**
   * Issue #89 — öğrenci login bar chart kartı. `trendCards`'tan ayrı tutulur (günlük çubuk, haftalık değil).
   * Aynı trends yanıtının `studentLogin` serisinden son 30 gün dilimlenir; tick seyreltmesi viewport'a bağlı.
   */
  readonly loginTrendCard = computed<LoginTrendViewModel | null>(() => {
    const t = this.trends();
    if (!t) {
      return null;
    }
    const points = t.studentLogin.slice(-LOGIN_TREND_DAYS);
    const total = points.reduce((acc, p) => acc + p.count, 0);
    const isEmpty = points.length === 0 || total === 0;
    const results = points.map((p) => ({
      name: p.date,
      value: p.count,
      extra: { label: formatTooltipDate(parseIsoDate(p.date), this.intlLocale) },
    }) satisfies LoginBarPoint);
    const tickStep = this.isMobileViewport() ? LOGIN_TICK_STEP_MOBILE : LOGIN_TICK_STEP_DESKTOP;

    return {
      title: this.text('trends.loginTitle'),
      icon: 'login',
      period: this.text('trends.periodDays', { days: LOGIN_TREND_DAYS }),
      scheme: this.loginScheme,
      results,
      xAxisTicks: pickTickNames(results, tickStep),
      total,
      isEmpty,
      summaryText: this.buildLoginSummary(points, total, isEmpty),
    };
  });

  constructor() {
    this.preloadScope();
  }

  ngOnInit(): void {
    this.updateViewportWidth();
    // İki istek paralel; biri diğerini bloklamaz ve kendi durum setini yönetir.
    this.loadSummary();
    this.loadTrends();
    this.pendingService.refresh().pipe(take(1)).subscribe({ error: () => undefined });
  }

  @HostListener('window:resize')
  onWindowResize(): void {
    this.updateViewportWidth();
  }

  loadSummary(): void {
    this.loading.set(true);
    this.error.set(null);

    this.adminService
      .getDashboardSummary()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (summary) => this.summary.set(summary),
        error: () => {
          this.summary.set(null);
          this.error.set(this.text('error'));
        },
      });
  }

  loadTrends(): void {
    this.trendLoading.set(true);
    this.trendError.set(null);

    this.adminService
      .getDashboardTrends(TREND_DAYS)
      .pipe(finalize(() => this.trendLoading.set(false)))
      .subscribe({
        next: (trends) => this.trends.set(trends),
        error: () => {
          this.trends.set(null);
          this.trendError.set(this.text('trends.error'));
        },
      });
  }

  readonly formatLoginXAxisTick = (isoDate: string): string => formatIsoDayTick(isoDate, this.intlLocale);
  readonly formatLoginYAxisTick = formatIntegerTick;

  /** Scope'a göreli anahtarı senkron çözer; sözlük şablon render edilirken yüklenmiş olur. */
  private text(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${ADMIN_SCOPE}.dashboard.${key}`, params) ?? '';
  }

  /** "bugün" / "dün" / "3 gün önce" — aktif dilde. */
  private describeDaysAgo(isoDate: string): string {
    const { key, count } = describeDaysAgoKey(isoDate);
    return this.text(key, { count });
  }

  private updateViewportWidth(): void {
    if (typeof window === 'undefined') {
      return;
    }
    this.viewportWidth.set(window.innerWidth);
  }

  private buildTrendCard(
    key: TrendKey,
    title: string,
    icon: string,
    verb: string,
    points: AdminDashboardTrendPoint[],
  ): TrendCardViewModel {
    const total = points.reduce((acc, p) => acc + p.count, 0);
    const isEmpty = points.length === 0 || total === 0;

    return {
      key,
      title,
      icon,
      period: this.text('trends.periodWeeks', { weeks: TREND_WEEKS }),
      weeks: this.toWeekBars(points),
      total,
      isEmpty,
      summaryText: this.buildTrendSummary(verb, points, total, isEmpty),
    };
  }

  /**
   * #887: günlük seriyi son günle biten 7'şer günlük kovalara toplar (son kova bugünü içerir). Seri kısaysa eksik günler 0;
   * etiket kovanın ilk günü ("2 Kas").
   */
  private toWeekBars(points: AdminDashboardTrendPoint[]): TrendWeekBar[] {
    const endDate = points.length > 0 ? parseIsoDate(points[points.length - 1].date) : todayCalendarDate();
    const countByDate = new Map(points.map((p) => [p.date, p.count]));
    const weeks = Array.from({ length: TREND_WEEKS }, (_, i) => {
      const start = addDays(endDate, -((TREND_WEEKS - i) * DAYS_PER_WEEK - 1));
      let count = 0;
      for (let d = 0; d < DAYS_PER_WEEK; d++) {
        count += countByDate.get(toIsoDateKey(addDays(start, d))) ?? 0;
      }
      return { label: formatWeekLabel(start, this.intlLocale), count, percent: 0 };
    });
    const max = Math.max(...weeks.map((w) => w.count));
    return weeks.map((w) => ({ ...w, percent: max > 0 ? Math.round((w.count * 100) / max) : 0 }));
  }

  /**
   * Erişilebilirlik özeti. Örn:
   * "Son 1 yıl: toplam 42 soru oluşturuldu. En yüksek gün 3 gün önce, 9 soru."
   */
  private buildTrendSummary(verb: string, points: AdminDashboardTrendPoint[], total: number, isEmpty: boolean): string {
    if (isEmpty) {
      return this.text('trends.summaryEmpty');
    }
    const peak = findPeak(points);
    if (!peak) {
      return this.text('trends.summaryEmpty');
    }
    return this.text('trends.summary', {
      total,
      verb,
      peak: this.describeDaysAgo(peak.date),
      count: peak.count,
    });
  }

  /**
   * Login kartı erişilebilirlik özeti. Örn:
   * "Son 30 gün: toplam 120 öğrenci girişi. En yüksek gün dün, 14 giriş."
   */
  private buildLoginSummary(points: AdminDashboardTrendPoint[], total: number, isEmpty: boolean): string {
    const peak = isEmpty ? undefined : findPeak(points);
    if (!peak) {
      return this.text('trends.loginSummaryEmpty', { days: LOGIN_TREND_DAYS });
    }
    return this.text('trends.loginSummary', {
      days: LOGIN_TREND_DAYS,
      total,
      peak: this.describeDaysAgo(peak.date),
      count: peak.count,
    });
  }

  /**
   * Şablon dışı metinler (snackbar, dialog, hata mesajı) senkron `translate()` ile okunur;
   * sözlük şablon render edilmeden de hazır olsun diye scope burada yüklenir.
   */
  private preloadScope(): void {
    this.transloco
      .load(`${ADMIN_SCOPE}/${this.transloco.getActiveLang()}`)
      .pipe(take(1))
      .subscribe();
  }

}
