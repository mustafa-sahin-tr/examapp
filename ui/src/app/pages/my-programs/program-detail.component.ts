import { Component } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { CommonModule } from '@angular/common';
import { NgxChartsModule, Color, ScaleType } from '@swimlane/ngx-charts';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatFormFieldModule } from '@angular/material/form-field';
import { FormsModule } from '@angular/forms';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';

interface ProgramDayDetail {
  date: string;
  done: boolean;
  solved: number;
  minutes: number;
  subjects: { name: string; solved: number; minutes: number }[];
  notes?: string;
  mood?: string;
}

@Component({
  selector: 'app-program-detail',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatIconModule,
    NgxChartsModule,
    MatProgressBarModule,
    MatFormFieldModule,
    MatInputModule,
    FormsModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
  ],
  templateUrl: './program-detail.component.html',
  styleUrls: ['./program-detail.component.scss'],
})
export class ProgramDetailComponent {
  programId: string | null = null;

  // Gelişmiş mock veri
  program = {
    programName: 'TYT Deneme Programı',
    startDate: '2025-05-01',
    endDate: '2025-05-30',
    studyType: 'question',
    studyDuration: '25-5',
    questionsPerDay: 12,
    subjectsPerDay: 2,
    days: [
      {
        date: '2025-05-01',
        done: true,
        solved: 12,
        minutes: 60,
        subjects: [
          { name: 'Matematik', solved: 6, minutes: 30 },
          { name: 'Fizik', solved: 6, minutes: 30 },
        ],
        notes: 'Harika başladım!',
        mood: '😃',
      },
      {
        date: '2025-05-02',
        done: false,
        solved: 8,
        minutes: 35,
        subjects: [
          { name: 'Matematik', solved: 4, minutes: 20 },
          { name: 'Fizik', solved: 4, minutes: 15 },
        ],
        notes: 'Biraz yorgundum.',
        mood: '😐',
      },
      {
        date: '2025-05-03',
        done: true,
        solved: 15,
        minutes: 70,
        subjects: [
          { name: 'Matematik', solved: 7, minutes: 35 },
          { name: 'Fizik', solved: 8, minutes: 35 },
        ],
        notes: 'Rekor kırdım!',
        mood: '🔥',
      },
      { date: '2025-05-04', done: false, solved: 0, minutes: 0, subjects: [], notes: 'Çalışmadım.', mood: '😞' },
      {
        date: '2025-05-05',
        done: true,
        solved: 12,
        minutes: 60,
        subjects: [
          { name: 'Matematik', solved: 6, minutes: 30 },
          { name: 'Fizik', solved: 6, minutes: 30 },
        ],
        notes: '',
        mood: '🙂',
      },
      {
        date: '2025-05-06',
        done: true,
        solved: 14,
        minutes: 65,
        subjects: [
          { name: 'Matematik', solved: 7, minutes: 35 },
          { name: 'Fizik', solved: 7, minutes: 30 },
        ],
        notes: 'İyi bir gündü.',
        mood: '😃',
      },
      {
        date: '2025-05-07',
        done: true,
        solved: 10,
        minutes: 50,
        subjects: [
          { name: 'Matematik', solved: 5, minutes: 25 },
          { name: 'Fizik', solved: 5, minutes: 25 },
        ],
        notes: '',
        mood: '🙂',
      },
      {
        date: '2025-05-08',
        done: true,
        solved: 16,
        minutes: 80,
        subjects: [
          { name: 'Matematik', solved: 8, minutes: 40 },
          { name: 'Fizik', solved: 8, minutes: 40 },
        ],
        notes: 'Çok verimli!',
        mood: '😃',
      },
      {
        date: '2025-05-09',
        done: false,
        solved: 6,
        minutes: 20,
        subjects: [
          { name: 'Matematik', solved: 3, minutes: 10 },
          { name: 'Fizik', solved: 3, minutes: 10 },
        ],
        notes: 'Düşüşteyim.',
        mood: '😞',
      },
      {
        date: '2025-05-10',
        done: true,
        solved: 13,
        minutes: 65,
        subjects: [
          { name: 'Matematik', solved: 7, minutes: 35 },
          { name: 'Fizik', solved: 6, minutes: 30 },
        ],
        notes: '',
        mood: '🙂',
      },
      // ...daha fazla gün eklenebilir
    ] as ProgramDayDetail[],
  };

  selectedDay: ProgramDayDetail | null = null;

  carouselIndex = 0;
  daysPerView = 7; // Kaç gün bir arada görünsün (responsive için ayarlanabilir)
  get maxCarouselIndex() {
    return Math.max(0, this.program.days.length - this.daysPerView);
  }

  constructor(private route: ActivatedRoute) {
    this.programId = this.route.snapshot.paramMap.get('id');
  }

  get totalDays() {
    return this.program.days.length;
  }
  get completedDays() {
    return this.program.days.filter((d) => d.done).length;
  }
  get missedDays() {
    return this.program.days.filter((d) => !d.done).length;
  }

  get visibleDays() {
    return this.program.days.slice(this.carouselIndex, this.carouselIndex + this.daysPerView);
  }

  selectDay(day: ProgramDayDetail) {
    this.selectedDay = day;
  }
  closeDayDetail() {
    this.selectedDay = null;
  }

  scrollDays(direction: number) {
    this.carouselIndex = Math.min(Math.max(0, this.carouselIndex + direction), this.maxCarouselIndex);
    // Scroll işlemi için DOM'a erişim gerekirse ViewChild ile eklenebilir
  }

  getRealIndex(i: number) {
    return this.carouselIndex + i;
  }

  // Modern istatistikler için ek mock veriler ve hesaplamalar
  get totalSolved() {
    return this.program.days.reduce((sum, d) => sum + d.solved, 0);
  }
  get totalMinutes() {
    return this.program.days.reduce((sum, d) => sum + d.minutes, 0);
  }
  get successRate() {
    return this.program.days.length > 0 ? Math.round((this.completedDays / this.program.days.length) * 100) : 0;
  }

  // ngx-charts heatmap için örnek veri ve renk şeması
  heatmapData = [
    {
      name: 'Hafta 1',
      series: [
        { name: 'Pzt', value: 12 },
        { name: 'Sal', value: 8 },
        { name: 'Çar', value: 12 },
        { name: 'Per', value: 0 },
        { name: 'Cum', value: 12 },
        { name: 'Cmt', value: 10 },
        { name: 'Paz', value: 0 },
      ],
    },
    {
      name: 'Hafta 2',
      series: [
        { name: 'Pzt', value: 10 },
        { name: 'Sal', value: 12 },
        { name: 'Çar', value: 12 },
        { name: 'Per', value: 12 },
        { name: 'Cum', value: 8 },
        { name: 'Cmt', value: 12 },
        { name: 'Paz', value: 0 },
      ],
    },
    // ...devamı
  ];
  heatmapScheme: Color = {
    name: 'heatmapScheme',
    selectable: false,
    group: ScaleType.Quantile,
    domain: ['#e0f7fa', '#80deea', '#26c6da', '#00acc1', '#00838f', '#005662'],
  };

  // --- EN İYİ GÜN, EN KÖTÜ GÜN, STREAK ---
  get bestDay() {
    if (this.program.days.length === 0) return null;
    return this.program.days.reduce(
      (best: any, d: any) => (!best || d.solved > best.solved ? d : best),
      this.program.days[0]
    );
  }
  get worstDay() {
    const filtered = this.program.days.filter((d: any) => d.solved > 0);
    if (filtered.length === 0) return null;
    return filtered.reduce((worst: any, d: any) => (d.solved < worst.solved ? d : worst), filtered[0]);
  }
  get currentStreak() {
    let streak = 0;
    for (let i = this.program.days.length - 1; i >= 0; i--) {
      if (this.program.days[i].done) streak++;
      else break;
    }
    return streak;
  }
  get maxStreak() {
    let max = 0,
      cur = 0;
    for (const d of this.program.days) {
      if (d.done) cur++;
      else {
        max = Math.max(max, cur);
        cur = 0;
      }
    }
    return Math.max(max, cur);
  }

  // --- DERS BAZINDA İSTATİSTİKLER ---
  get subjectStats() {
    const stats: { [subject: string]: { solved: number; minutes: number } } = {};
    for (const d of this.program.days) {
      for (const s of d.subjects) {
        if (!stats[s.name]) stats[s.name] = { solved: 0, minutes: 0 };
        stats[s.name].solved += s.solved;
        stats[s.name].minutes += s.minutes;
      }
    }
    return Object.entries(stats).map(([name, v]) => ({ name, ...v }));
  }

  // --- PIE CHART DATA ---
  get subjectPieData() {
    return this.subjectStats.map((s) => ({ name: s.name, value: s.solved }));
  }
  subjectPieScheme: Color = {
    name: 'pieScheme',
    selectable: true,
    group: ScaleType.Ordinal,
    domain: ['#1976d2', '#43a047', '#ffb300', '#e53935', '#8e24aa'],
  };

  // --- GÜNLÜK FEEDBACK MESAJI ---
  getMotivation(day: any) {
    if (day.solved === 0) return 'Bugün çalışmadın, yarın telafi edebilirsin!';
    if (day.solved >= this.program.questionsPerDay * 1.2) return 'Mükemmel performans!';
    if (day.solved >= this.program.questionsPerDay) return 'Hedefini tutturdun, harikasın!';
    if (day.solved >= this.program.questionsPerDay * 0.7) return 'Fena değil, biraz daha gayret!';
    return 'Bugün hedefin altında kaldın.';
  }

  // Bar chart için gün bazında hedef ve gerçekleşen soru sayısı
  get barChartData() {
    return this.program.days.map((d, i) => ({
      name: `${i + 1}.Gün`,
      value: d.solved,
    }));
  }
  // En uzun süre günü
  get maxMinutesDay() {
    if (!this.program.days.length) return null;
    return this.program.days.reduce((max, d) => (d.minutes > (max?.minutes ?? 0) ? d : max), this.program.days[0]);
  }
  // En yüksek uyum oranı günü
  get maxRateDay() {
    if (!this.program.days.length) return null;
    return this.program.days.reduce(
      (max, d) => {
        const rate = d.solved / this.program.questionsPerDay;
        const maxRate = max.solved / this.program.questionsPerDay;
        return rate > maxRate ? { ...d, rate: Math.round(rate * 100) } : { ...max, rate: Math.round(maxRate * 100) };
      },
      { ...this.program.days[0], rate: Math.round((this.program.days[0].solved / this.program.questionsPerDay) * 100) }
    );
  }
  // Haftalık toplam soru ve süre (son 7 gün)
  get weeklyTotalSolved() {
    return this.program.days.slice(-7).reduce((sum, d) => sum + d.solved, 0);
  }
  get weeklyTotalMinutes() {
    return this.program.days.slice(-7).reduce((sum, d) => sum + d.minutes, 0);
  }

  // Radar chart için örnek veri
  get radarChartData() {
    // ngx-charts-polar-chart expects an array of objects with a 'name' and a 'series' array
    // Example:
    // [
    //   { name: 'Başarı', series: [
    //     { name: 'Matematik', value: 85 },
    //     { name: 'Türkçe', value: 60 }, ... ] }
    // ]
    return [
      {
        name: 'Başarı',
        series: [
          { name: 'Matematik', value: 85 },
          { name: 'Türkçe', value: 60 },
          { name: 'Fen', value: 40 },
          { name: 'Sosyal Bil.', value: 75 },
          { name: 'İngilizce', value: 90 },
        ],
      },
    ];
  }
}
