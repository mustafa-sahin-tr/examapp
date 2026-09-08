import {
  Component,
  ElementRef,
  HostListener,
  Injector,
  OnInit,
  QueryList,
  ViewChildren,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Color, HeatMapModule, ScaleType } from '@swimlane/ngx-charts';
import { finalize } from 'rxjs';
import { AdminService } from '../../../services/admin.service';
import {
  AdminDashboardSummary,
  AdminDashboardTrendPoint,
  AdminDashboardTrends,
} from '../../../models/admin-dashboard.model';

type SummaryCardKey = 'teachers' | 'students' | 'worksheets' | 'questions';

interface SummaryCardViewModel {
  key: SummaryCardKey;
  label: string;
  value: number;
  icon: string;
}

/** Tooltip için hücreye iliştirilen veri (ngx-charts `extra` alanı). */
interface HeatmapCellExtra {
  /** `yyyy-MM-dd` */
  date: string;
  /** "10 Ağu 2026" */
  label: string;
  count: number;
  /** "oluşturuldu" / "çözüldü" — tooltip cümlesi için. */
  verb: string;
}

/**
 * Tek gün hücresi (y ekseni satırı); `name` = gün adı, `value` = yoğunluk SEVİYESİ (0..HEAT_LEVELS tam sayı),
 * gerçek sayı DEĞİL. Gerçek sayı `extra.count`'ta; tooltip ve özet oradan okur.
 */
interface HeatmapDay {
  name: string;
  value: number;
  extra: HeatmapCellExtra;
}

/**
 * ngx-charts heat-map `results` girdisi; dashboard.component `transformActivityToHeatmap` çıktısı ile aynı
 * oryantasyon: dış dizi = haftalar (x ekseni sütunları, `name` = hafta başlangıcı), `series` = 7 gün.
 */
interface HeatmapWeek {
  name: string;
  series: HeatmapDay[];
}

/** ngx-charts `tooltipText` callback'ine gelen nesnenin bizim kullandığımız kısmı. */
interface HeatmapTooltipPayload {
  cell?: { value?: number; extra?: HeatmapCellExtra };
  data?: number;
}

type TrendKey = 'created' | 'solved';

/** Bir trend kartının şablon için hazır hâli; her ikisi de aynı kart bileşenini kullanır. */
interface TrendCardViewModel {
  key: TrendKey;
  title: string;
  icon: string;
  /** Heatmap yoğunluk şeması: seviye 0..HEAT_LEVELS için token'ın açık tonundan tam rengine kademeli geçiş. */
  scheme: Color;
  results: HeatmapWeek[];
  /** x ekseni tick'i: aynı ay tekrar etmez (kart başına ayrı durum). */
  xAxisTickFormatting: (label: string) => string;
  total: number;
  /** Tüm günler 0 ise "yeterli veri yok" gösterilir. */
  isEmpty: boolean;
  /** Ekran okuyucu için kısa özet cümle (aria-describedby hedefi). */
  summaryText: string;
}

/** Skeleton'da çizilecek kart sayısı: 4 sayaç + 1 AI kartı. */
const SKELETON_CARD_COUNT = 5;

const TOTAL_WEEKS = 52;
const DAYS_PER_WEEK = 7;
const MS_PER_DAY = 86_400_000;

/** Trend penceresi (gün): 52 tam hafta. Backend `days` 1..365 kabul eder. */
const TREND_DAYS = TOTAL_WEEKS * DAYS_PER_WEEK;

/** Mobilde gösterilen son hafta sayısı (dashboard.component `mobileHeatmapWeeks` ile aynı). */
const MOBILE_HEATMAP_WEEKS = 17;

/** Sıfır dışı günler için yoğunluk seviyesi sayısı; hücre `value` 0..HEAT_LEVELS (0 = o gün etkinlik yok). */
const HEAT_LEVELS = 4;

/** Seviye sınırlarının dağılım içindeki konumu: 1/4, 2/4, 3/4 (HEAT_LEVELS-1 adet eşik). */
const HEAT_LEVEL_FRACTIONS = Array.from({ length: HEAT_LEVELS - 1 }, (_, i) => (i + 1) / HEAT_LEVELS);

/** Sıralı dizide doğrusal enterpolasyonlu yüzdelik (q: 0..1). */
function quantile(sorted: number[], q: number): number {
  const pos = (sorted.length - 1) * q;
  const lo = Math.floor(pos);
  const hi = Math.ceil(pos);
  return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
}

/**
 * Bir serinin gün sayılarından heatmap seviye eşiklerini (artan, HEAT_LEVELS-1 adet) türetir.
 *
 * Neden ham sayı değil, seviye: ngx-charts hücre rengini her zaman [0,max] üzerinden DOĞRUSAL normalize eder.
 * Günlük etkinlik verisi uzun kuyrukludur (çoğu gün 1-5, tek bir gün 79 gibi); doğrusal ölçekte tepe dışındaki
 * her gün skalanın en alt %10-20'sine sıkışıp boş günden ayırt edilemez oluyordu. GitHub katkı grafiği de bu
 * yüzden sürekli renk yerine verinin kendi dağılımından türetilmiş birkaç ayrık kova kullanır; burada aynı fikir.
 *
 * Eşikler sıfır dışı FARKLI değerlerin çeyrekliklerinden alınır. "Farklı" olması bilinçli: tekrar eden değerler
 * (yüzlerce "1" günü) dahil edilseydi çeyreklikler birbirine eşit çıkıp (1,1,1) 2-3 gibi sıradan günleri tepe ile
 * aynı kovaya iter, ya da tam tersi her "1" gününü koyu gösterirdi. Farklı değerler üzerinden alınınca en yüksek
 * gün her zaman en üst seviyeye düşer, ara değerler kademeli dağılır. Tek farklı değer varsa (her etkin gün aynı
 * sayı) çeyreklik tanımsız olur; [0,max] eşit bölünür ve o günler en üst seviye alır — hepsi "tepe"dir.
 */
function buildHeatThresholds(counts: number[]): number[] {
  const distinct = Array.from(new Set(counts.filter((c) => c > 0))).sort((a, b) => a - b);
  if (distinct.length === 0) {
    return [];
  }
  if (distinct.length === 1) {
    const max = distinct[0];
    return HEAT_LEVEL_FRACTIONS.map((f) => max * f);
  }
  return HEAT_LEVEL_FRACTIONS.map((f) => quantile(distinct, f));
}

/** Gerçek gün sayısı → seviye: 0 = etkinlik yok; aksi hâlde 1 + aşılan eşik sayısı (en fazla HEAT_LEVELS). */
function toHeatLevel(count: number, thresholds: number[]): number {
  if (count <= 0) {
    return 0;
  }
  return 1 + thresholds.filter((t) => count > t).length;
}

/** `--primaryColor` gibi bir token'ı runtime'da çözer; token bulunamazsa SVG için güvenli `currentColor`. */
function readCssToken(name: string): string {
  if (typeof getComputedStyle === 'undefined') {
    return 'currentColor';
  }
  const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  return value || 'currentColor';
}

/**
 * `#rgb` / `#rrggbb` rengi `rgba(r,g,b,alpha)`'ya çevirir. ngx-charts (d3) `color-mix()` çözemediği için
 * açık tonlar alfa ile üretilir; böylece hücre kart yüzeyinin üstünde temadan bağımsız doğru görünür.
 * Hex olmayan girdi (örn. `currentColor`) olduğu gibi döner.
 */
function withAlpha(color: string, alpha: number): string {
  const hex = color.startsWith('#') ? color.slice(1) : '';
  const full = hex.length === 3 ? hex.split('').map((c) => c + c).join('') : hex;
  if (!/^[0-9a-f]{6}$/i.test(full)) {
    return color;
  }
  const r = parseInt(full.slice(0, 2), 16);
  const g = parseInt(full.slice(2, 4), 16);
  const b = parseInt(full.slice(4, 6), 16);
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}

/**
 * Seviye 0..2 için hücre alfa değerleri (0 = etkinlik yok: soluk ama görünür). Seviye 3 durağı ve seviye 4'ün
 * rengi bunlardan türetilir, bkz. `buildHeatScheme`.
 */
const HEAT_LEVEL_ALPHAS = [0.1, 0.3, 0.55] as const;

/**
 * Heatmap renk şeması. Hücre `value` artık 0..HEAT_LEVELS seviyesidir (bkz. `buildHeatThresholds`), yani
 * ngx-charts'ın [0,max] doğrusal normalizasyonu daima seviye/HEAT_LEVELS = 0, .25, .5, .75, 1 üretir.
 * `ColorHelper` N durağı `0, 1/N … (N-1)/N` noktalarına koyar; N = HEAT_LEVELS seçilince seviye 0..3 tam olarak
 * bir durağa denk gelir, ara renk üretilmez. En üst seviye (1.0) her N için son durağın ötesindedir ve d3
 * ekstrapole eder: renk = 2·son − sondan_önceki. Son durak, bu ekstrapolasyon tam renk (alfa 1) versin diye
 * `(1 + önceki_alfa) / 2` olarak seçilir; böylece 5 seviye 5 belirgin ton alır ve tepe tam renktir.
 */
function buildHeatScheme(name: string, tokenName: string): Color {
  const color = readCssToken(tokenName);
  const [zero, low, mid] = HEAT_LEVEL_ALPHAS;
  const high = (1 + mid) / 2;
  return {
    name,
    selectable: false,
    group: ScaleType.Linear,
    domain: [withAlpha(color, zero), withAlpha(color, low), withAlpha(color, mid), withAlpha(color, high)],
  };
}

/** `yyyy-MM-dd` → yerel gece yarısı Date (saat dilimi kayması olmadan takvim günü). */
function parseIsoDate(isoDate: string): Date {
  const [y, m, d] = isoDate.split('-').map(Number);
  return new Date(y, m - 1, d);
}

function normalizeDate(date: Date): Date {
  const normalized = new Date(date);
  normalized.setHours(0, 0, 0, 0);
  return normalized;
}

function toIsoDateKey(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

function addDays(date: Date, days: number): Date {
  const result = new Date(date);
  result.setDate(result.getDate() + days);
  result.setHours(0, 0, 0, 0);
  return result;
}

/** x ekseni etiketi: "10 Ağu" */
function formatWeekLabel(weekStart: Date): string {
  return weekStart.toLocaleDateString('tr-TR', { day: 'numeric', month: 'short' });
}

/** y ekseni etiketi: "Pzt" */
function formatDayLabel(date: Date): string {
  return date.toLocaleDateString('tr-TR', { weekday: 'short' });
}

/** Tooltip tarihi: "10 Ağu 2026" */
function formatTooltipDate(date: Date): string {
  return date.toLocaleDateString('tr-TR', { day: 'numeric', month: 'short', year: 'numeric' });
}

/**
 * dashboard.component `activityXAxisTickFormatting` ile aynı davranış: hafta etiketi "10 Ağu" → ay adı,
 * aynı ay art arda gelince boş. Durum kart başına tutulur ve ilk hafta etiketinde sıfırlanır; böylece
 * iki heatmap ve yeniden render'lar birbirini etkilemez.
 */
function createMonthTickFormatter(firstWeekLabel: string): (label: string) => string {
  let lastMonth = '';
  return (label: string): string => {
    if (label === firstWeekLabel) {
      lastMonth = '';
    }
    const parts = label.trim().split(' ');
    if (parts.length < 2) {
      return label;
    }
    const month = parts[1];
    if (lastMonth === month) {
      return '';
    }
    lastMonth = month;
    return month;
  };
}

/**
 * `yyyy-MM-dd` → bugünden kaç gün önce (0 = bugün). Yerel takvim günü ile karşılaştırılır — `parseIsoDate`
 * ve heatmap ızgarasıyla aynı gün tanımı; UTC kullanılsaydı yerel gece yarısından sonraki ilk saatlerde
 * "bugün"/"dün" kayardı.
 */
function daysAgo(isoDate: string): number {
  const then = parseIsoDate(isoDate).getTime();
  const today = normalizeDate(new Date()).getTime();
  return Math.max(0, Math.round((today - then) / MS_PER_DAY));
}

/**
 * Issue #86 / #88 — Admin dashboard.
 * Phase 1: sayaç kartları (summary). Phase 2: son 1 yılın (52 hafta) soru oluşturma / çözme takvim
 * heatmap'leri — öğrenci dashboard'undaki aktivite heatmap'i ile aynı ngx-charts deseni ve boyut.
 * İki bölümün loading/error/data durumları birbirinden bağımsızdır; trend endpoint'i düşerse
 * yalnızca trend bölümü hata gösterir, sayaç kartları etkilenmez.
 */
@Component({
  selector: 'app-admin-dashboard',
  standalone: true,
  imports: [DecimalPipe, MatButtonModule, MatIconModule, MatProgressSpinnerModule, HeatMapModule],
  templateUrl: './admin-dashboard.component.html',
  styleUrls: ['./admin-dashboard.component.scss'],
})
export class AdminDashboardComponent implements OnInit {
  private readonly adminService = inject(AdminService);
  private readonly injector = inject(Injector);

  /** Heatmap kaydırma kapları (kart başına bir tane); bkz. `scrollHeatmapsToLatestWeek`. */
  @ViewChildren('heatmapScroll') private readonly heatmapScrolls?: QueryList<ElementRef<HTMLElement>>;

  // ── Phase 1: sayaçlar ──────────────────────────────────────────────────────
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly summary = signal<AdminDashboardSummary | null>(null);

  readonly skeletonCards = Array.from({ length: SKELETON_CARD_COUNT }, (_, i) => i);

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
      { key: 'teachers', label: 'Öğretmen', value: s.teacherCount, icon: 'school' },
      { key: 'students', label: 'Öğrenci', value: s.studentCount, icon: 'groups' },
      { key: 'worksheets', label: 'Test', value: s.worksheetCount, icon: 'assignment' },
      { key: 'questions', label: 'Soru', value: s.questionCount, icon: 'quiz' },
    ];
  });

  /** Hiç soru yoksa AI kartı oran yerine "Henüz soru yok" gösterir. */
  readonly hasQuestions = computed(() => (this.summary()?.questionCount ?? 0) > 0);

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

  /** Masaüstünde 52 hafta, mobilde son 17 hafta (yatay kaydırma ile). */
  private readonly visibleWeekCount = computed(() => (this.isMobileViewport() ? MOBILE_HEATMAP_WEEKS : TOTAL_WEEKS));

  readonly heatmapView = computed<[number, number]>(() => {
    const weeks = Math.max(this.visibleWeekCount(), this.isMobileViewport() ? 14 : 24);
    if (this.isMobileViewport()) {
      return [Math.max(weeks * 20, Math.max(this.viewportWidth() - 24, 320)), 190];
    }
    return [Math.max(weeks * 22, 1200), 210];
  });

  /**
   * Renkler CSS token'dan bir kez okunur: ngx-charts `scheme.domain` `var(--x)` kabul etmez,
   * çözümlenmiş değere ihtiyaç duyar. Tema değişimi bu sayfada beklenmediği için cache yeterli.
   */
  private readonly createdScheme = buildHeatScheme('trend-created', '--primaryColor');
  private readonly solvedScheme = buildHeatScheme('trend-solved', '--ms-success-text-medium');

  readonly trendCards = computed<TrendCardViewModel[]>(() => {
    const t = this.trends();
    if (!t) {
      return [];
    }
    const visibleWeeks = this.visibleWeekCount();
    return [
      this.buildTrendCard('created', 'Soru Oluşturma', 'add_circle_outline', 'oluşturuldu', t.questionCreated, this.createdScheme, visibleWeeks),
      this.buildTrendCard('solved', 'Soru Çözme', 'task_alt', 'çözüldü', t.questionSolved, this.solvedScheme, visibleWeeks),
    ];
  });

  /**
   * Heatmap içeriği her değiştiğinde (veri geldi, ya da mobil↔masaüstü eşiği geçilip 17/52 hafta yeniden
   * çizildi) kapları en güncel haftaya kaydırır. `trendCards` yalnızca `trends` veya hafta sayısı değişince
   * yeniden hesaplanır; ilgisiz change-detection turları (aynı kırılım içinde resize, tooltip vb.) effect'i
   * tetiklemez, dolayısıyla yöneticinin elle geri kaydırdığı konumla oynanmaz.
   */
  private readonly scrollToLatestWeekOnTrendChange = effect(() => {
    if (this.trendCards().length === 0) {
      return;
    }
    this.scrollHeatmapsToLatestWeek();
  });

  ngOnInit(): void {
    this.updateViewportWidth();
    // İki istek paralel; biri diğerini bloklamaz ve kendi durum setini yönetir.
    this.loadSummary();
    this.loadTrends();
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
          this.error.set('Özet bilgileri alınırken bir sorun oluştu.');
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
          this.trendError.set('Trend verileri alınırken bir sorun oluştu.');
        },
      });
  }

  /**
   * Bir sonraki render'ın ardından her heatmap kabını en sağa (en güncel hafta) kaydırır. `afterNextRender`
   * DOM'da yeni `@for` öğeleri ve ngx-charts SVG'si (sabit `[view]` genişliği) yerleştikten sonra çalışır;
   * iki kart bağımsız ölçülür. 52 hafta kartın genişliğinden taştığı için tarayıcı varsayılanı
   * (`scrollLeft = 0`) en ESKİ haftaları gösteriyordu, güncel ay görünmek için elle kaydırma gerekiyordu.
   */
  private scrollHeatmapsToLatestWeek(): void {
    afterNextRender(
      () => {
        this.heatmapScrolls?.forEach(({ nativeElement }) => {
          nativeElement.scrollLeft = nativeElement.scrollWidth;
        });
      },
      { injector: this.injector },
    );
  }

  /** ngx-charts `tooltipText` girdisi. Örn: "10 Ağu 2026: 5 soru oluşturuldu". */
  readonly formatHeatmapTooltip = (payload: HeatmapTooltipPayload | null | undefined): string => {
    const extra = payload?.cell?.extra;
    if (!extra) {
      return String(payload?.data ?? '');
    }
    return `${extra.label}: ${extra.count} soru ${extra.verb}`;
  };

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
    scheme: Color,
    visibleWeeks: number,
  ): TrendCardViewModel {
    const total = points.reduce((acc, p) => acc + p.count, 0);
    const isEmpty = points.length === 0 || total === 0;
    const results = this.transformToHeatmap(points, verb).slice(-visibleWeeks);

    return {
      key,
      title,
      icon,
      scheme,
      results,
      xAxisTickFormatting: createMonthTickFormatter(results[0]?.name ?? ''),
      total,
      isEmpty,
      summaryText: this.buildTrendSummary(verb, points, total, isEmpty),
    };
  }

  /**
   * dashboard.component `transformActivityToHeatmap`'in `{date, count}` serisine uyarlanmış hâli.
   * Backend tam olarak 52×7 gün döner (bugün dahil, geriye doğru); ızgara serinin ilk gününden başlar ve
   * son gününde biter, günler tarih anahtarıyla eşlenir. Haftanın gününe (Pazar) hizalama YAPILMAZ: ızgara
   * bütçesi sabit 52×7 olduğundan bitişi ileri bir Pazar'a kaydırmak serinin en eski günlerini ızgaranın
   * dışına düşürüyor, kart toplamı ile görünen hücreler tutarsızlaşıyordu. Bu yüzden ilk sütunun ilk satırı
   * Pazartesi olmak zorunda değil; y ekseni etiketleri gerçek gün adını taşır. Seri 52×7'den kısaysa
   * ızgaranın veri öncesi kısmı 0 olur; bitişten sonraki gün üretilmez. Her haftanın günleri ters sırada
   * verilir çünkü heat-map y ekseninde ilk öğeyi en alta koyar.
   *
   * Hücre `value` gerçek sayı değil, serinin kendi dağılımından türetilen yoğunluk seviyesidir
   * (`buildHeatThresholds`); gerçek sayı tooltip/özet için `extra.count`'a yazılır.
   */
  private transformToHeatmap(points: AdminDashboardTrendPoint[], verb: string): HeatmapWeek[] {
    const endDate = points.length > 0 ? parseIsoDate(points[points.length - 1].date) : normalizeDate(new Date());
    const startDate = addDays(endDate, -(TOTAL_WEEKS * DAYS_PER_WEEK - 1));

    const countByDate = new Map(points.map((p) => [p.date, p.count]));
    const thresholds = buildHeatThresholds(points.map((p) => p.count));

    const heatmap: HeatmapWeek[] = [];
    for (let weekIndex = 0; weekIndex < TOTAL_WEEKS; weekIndex++) {
      const weekStart = addDays(startDate, weekIndex * DAYS_PER_WEEK);
      const weekSeries = Array.from({ length: DAYS_PER_WEEK }, (_, dayOffset) => {
        const currentDate = addDays(weekStart, dayOffset);
        if (currentDate > endDate) {
          return null;
        }
        const key = toIsoDateKey(currentDate);
        const count = countByDate.get(key) ?? 0;
        return {
          name: formatDayLabel(currentDate),
          value: toHeatLevel(count, thresholds),
          extra: { date: key, label: formatTooltipDate(currentDate), count, verb },
        } satisfies HeatmapDay;
      }).filter((entry): entry is HeatmapDay => entry !== null);

      heatmap.push({ name: formatWeekLabel(weekStart), series: weekSeries.slice().reverse() });
    }

    return heatmap;
  }

  /**
   * Erişilebilirlik özeti. Örn:
   * "Son 1 yıl: toplam 42 soru oluşturuldu. En yüksek gün 3 gün önce, 9 soru."
   */
  private buildTrendSummary(verb: string, points: AdminDashboardTrendPoint[], total: number, isEmpty: boolean): string {
    if (isEmpty) {
      return 'Son 1 yıl: henüz yeterli veri yok.';
    }
    // Eşitlikte en güncel günü seç (tarihler artan sıralı → sondan tarama).
    let peak = points[points.length - 1];
    for (let i = points.length - 1; i >= 0; i--) {
      if (points[i].count > peak.count) {
        peak = points[i];
      }
    }
    const ago = daysAgo(peak.date);
    const when = ago === 0 ? 'bugün' : ago === 1 ? 'dün' : `${ago} gün önce`;
    return `Son 1 yıl: toplam ${total} soru ${verb}. En yüksek gün ${when}, ${peak.count} soru.`;
  }
}
