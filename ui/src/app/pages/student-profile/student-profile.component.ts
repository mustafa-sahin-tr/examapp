import { Component, inject, OnInit } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { StudentProfile } from '../../models/student-profile';
import { StudentService } from '../../services/student.service';
import { ActivatedRoute, Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatSelectModule } from '@angular/material/select';
import { Grade } from '../../models/student';
import { PointCardComponent } from '../../shared/components/point-card/point-card.component';
import { BadgeBoxComponent } from '../../shared/components/badge-box/badge-box.component';
import { LeaderboardComponent } from '../../shared/components/leaderboard/leaderboard.component';
import { SectionHeaderComponent } from '../../shared/components/section-header/section-header.component';
import { MatTabsModule } from '@angular/material/tabs';
import { NgxChartsModule, Color, ScaleType, LegendPosition } from '@swimlane/ngx-charts';
import { StudentTimeChartComponent } from '../../shared/components/student-time-chart/student-time-chart.component';
import { FormsModule, NgModel } from '@angular/forms';
import { BadgeThropyComponent } from '../../shared/components/badge-thropy/badge-thropy.component';
import { TestService } from '../../services/test.service';

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
    MatTooltipModule,
    MatSnackBarModule,
    MatSelectModule,
    PointCardComponent,
    BadgeBoxComponent,
    FormsModule,
    LeaderboardComponent,
    SectionHeaderComponent,
    MatTabsModule,
    NgxChartsModule,
    StudentTimeChartComponent,
    BadgeThropyComponent,
  ],
})
export class StudentProfileComponent implements OnInit {
  testService = inject(TestService);
  industry: { name: string; value: number } = { name: 'Software Development', value: 100 };
  yearsOfExperience: number = 0;
  avatarUrl: string = 'http://localhost/minio-api/avatars/avatar.png';
  industries = [
    { name: 'Software Development', value: 100 },
    { name: 'Healthcare', value: 75 },
    { name: 'Finance', value: 60 },
    { name: 'Education', value: 40 },
    { name: 'Retail', value: 30 },
  ];

  student: StudentProfile | null = null;
  studentId: number = 0;
  grades: Grade[] = [];
  activeTab = 0; // Varsayılan olarak ilk sekme açık
  activeTab2 = 1;
  badges = [
    { title: 'Activity', level: 1, icon: 'http://localhost/minio-api/avatars/activity-badge.png' },
    { title: 'Assignments', level: 2, icon: 'http://localhost/minio-api/avatars/activity-badge.png' },
    { title: 'Grades', level: 4, icon: 'http://localhost/minio-api/avatars/activity-badge.png' },
    { title: 'Attendance', level: 6, icon: 'http://localhost/minio-api/avatars/activity-badge.png' },
    { title: 'Discipline', level: 8, icon: 'http://localhost/minio-api/avatars/activity-badge.png' },
    { title: 'Communication', level: 7, icon: 'http://localhost/minio-api/avatars/activity-badge.png' },
  ];

  classLeaderboard = [
    { name: 'Safi Abu-Rashed', score: 1024, icon: 'assets/icons/gold.png' },
    { name: 'Ahmad Abed Al Rahman', score: 950, icon: 'assets/icons/silver.png' },
    { name: 'Hanan Saad', score: 912, icon: 'assets/icons/bronze.png' },
    { name: 'Omar Saleem', score: 880, icon: '' },
  ];

  groupLeaderboard = [
    { name: 'Safi Abu-Rashed', score: 740, grade: 'Grade 1 - Blue', icon: 'assets/icons/gold.png' },
    { name: 'Ahmad Abed Al Rahman', score: 695, grade: 'Grade 1 - Blue', icon: 'assets/icons/silver.png' },
    { name: 'Hanan Saad', score: 690, grade: 'Grade 1 - Blue', icon: 'assets/icons/bronze.png' },
  ];

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

  /**  */

  bubbleData: any[] = [
    {
      name: 'Germany',
      series: [
        {
          name: '2010',
          x: '2010',
          y: 80.3,
          r: 80.4,
        },
        {
          name: '2000',
          x: '2000',
          y: 80.3,
          r: 78,
        },
        {
          name: '1990',
          x: '1990',
          y: 75.4,
          r: 79,
        },
      ],
    },
    {
      name: 'United States',
      series: [
        {
          name: '2010',
          x: '2010',
          y: 78.8,
          r: 310,
        },
        {
          name: '2000',
          x: '2000',
          y: 76.9,
          r: 283,
        },
        {
          name: '1990',
          x: '1990',
          y: 75.4,
          r: 253,
        },
      ],
    },
    {
      name: 'France',
      series: [
        {
          name: '2010',
          x: '2010',
          y: 81.4,
          r: 63,
        },
        {
          name: '2000',
          x: '2000',
          y: 79.1,
          r: 59.4,
        },
        {
          name: '1990',
          x: '1990',
          y: 77.2,
          r: 56.9,
        },
      ],
    },
    {
      name: 'United Kingdom',
      series: [
        {
          name: '2010',
          x: '2010',
          y: 80.2,
          r: 62.7,
        },
        {
          name: '2000',
          x: '2000',
          y: 77.8,
          r: 58.9,
        },
        {
          name: '1990',
          x: '1990',
          y: 75.7,
          r: 57.1,
        },
      ],
    },
  ];

  view: any[] = [700, 400];

  // options
  showXAxis: boolean = true;
  showYAxis: boolean = true;
  gradient: boolean = false;
  showLegend: boolean = true;
  showXAxisLabel: boolean = true;
  yAxisLabel: string = 'Population';
  showYAxisLabel: boolean = true;
  xAxisLabel: string = 'Years';
  maxRadius: number = 20;
  minRadius: number = 5;
  yScaleMin: number = 70;
  yScaleMax: number = 85;

  // Günlük veriyi haftalara bölüp heatmap formatına uygun hale getiriyoruz
  activityData = this.generateHeatmapData3();

  activityData2 = this.generateHeatmapData();

  /**
   * Statik “MUSTAFA” ısı haritası verisi üreten fonksiyon.
   * Harf pikselleri value=10 (koyu), diğerleri value=0 (açık).
   */
  generateHeatmapData3(): Array<{ name: string; series: Array<{ name: string; value: number }> }> {
    const daysOfWeek = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
    const totalWeeks = 53;

    const letters: Record<string, number[][]> = {
      Q: [
        [0, 0, 0, 0, 0],
        [0, 1, 0, 1, 0],
        [1, 0, 1, 0, 1],
        [1, 0, 0, 0, 1],
        [0, 1, 0, 1, 0],
        [0, 0, 1, 0, 0],
        [0, 0, 0, 0, 0],
      ],
      M: [
        [0, 0, 0, 0, 0], // Row 0
        [1, 1, 0, 1, 1], // Row 1
        [1, 0, 1, 0, 1], // Row 2
        [1, 0, 0, 0, 1], // Row 3
        [1, 0, 0, 0, 1], // Row 4
        [1, 0, 0, 0, 1], // Row 5
        [0, 0, 0, 0, 0], // Row 6
      ],
      U: [
        [0, 0, 0, 0, 0],
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
        [1, 1, 1, 1, 1],
        [0, 0, 0, 0, 0],
      ],
      S: [
        [0, 0, 0, 0, 0],
        [0, 1, 1, 1, 1],
        [1, 0, 0, 0, 0],
        [0, 1, 1, 1, 0],
        [0, 0, 0, 0, 1],
        [1, 1, 1, 1, 0],
        [0, 0, 0, 0, 0],
      ],
      T: [
        [0, 0, 0, 0, 0],
        [1, 1, 1, 1, 1],
        [0, 0, 1, 0, 0],
        [0, 0, 1, 0, 0],
        [0, 0, 1, 0, 0],
        [0, 0, 1, 0, 0],
        [0, 0, 0, 0, 0],
      ],
      A: [
        [0, 0, 0, 0, 0],
        [0, 1, 1, 1, 0],
        [1, 0, 0, 0, 1],
        [1, 1, 1, 1, 1],
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
        [0, 0, 0, 0, 0],
      ],
      F: [
        [0, 0, 0, 0, 0],
        [1, 1, 1, 1, 1],
        [1, 0, 0, 0, 0],
        [1, 1, 1, 1, 0],
        [1, 0, 0, 0, 0],
        [1, 0, 0, 0, 0],
        [0, 0, 0, 0, 0],
      ],
    };

    // 5×7 piksel font desenleri
    const lettersBig: Record<string, number[][]> = {
      M: [
        [1, 0, 0, 0, 1],
        [1, 1, 0, 1, 1],
        [1, 0, 1, 0, 1],
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
      ],
      U: [
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
        [0, 1, 1, 1, 0],
      ],
      S: [
        [0, 1, 1, 1, 1],
        [1, 0, 0, 0, 0],
        [1, 0, 0, 0, 0],
        [0, 1, 1, 1, 0],
        [0, 0, 0, 0, 1],
        [0, 0, 0, 0, 1],
        [1, 1, 1, 1, 0],
      ],
      T: [
        [1, 1, 1, 1, 1],
        [0, 0, 1, 0, 0],
        [0, 0, 1, 0, 0],
        [0, 0, 1, 0, 0],
        [0, 0, 1, 0, 0],
        [0, 0, 1, 0, 0],
        [0, 0, 1, 0, 0],
      ],
      A: [
        [0, 1, 1, 1, 0],
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
        [1, 1, 1, 1, 1],
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
      ],
      F: [
        [1, 1, 1, 1, 1],
        [1, 0, 0, 0, 0],
        [1, 0, 0, 0, 0],
        [1, 1, 1, 1, 0],
        [1, 0, 0, 0, 0],
        [1, 0, 0, 0, 0],
        [1, 0, 0, 0, 0],
      ],
    };

    // Yazdırmak istediğimiz kelime
    const word = ['Q', 'M', 'U', 'S', 'T', 'A', 'F', 'A', 'Q'];

    // 7 satır için başlangıç boş matris
    const gridRows: number[][] = Array.from({ length: 7 }, () => []);

    // Harf desenlerini yan yana ekle, harfler arası 1 sütun boşluk
    word.forEach((ch, idx) => {
      const pattern = letters[ch];
      for (let r = 0; r < 7; r++) {
        gridRows[r].push(...pattern[r]);
      }
      if (idx < word.length - 1) {
        for (let r = 0; r < 7; r++) {
          gridRows[r].push(0);
        }
      }
    });

    // Toplam 52 haftaya ortalamak için sola/sağa dolgu
    const cols = gridRows[0].length; // 41 olması beklenir
    const padLeft = Math.floor((totalWeeks - cols) / 2);
    const padRight = totalWeeks - cols - padLeft;
    for (let r = 0; r < 7; r++) {
      gridRows[r] = [...Array(padLeft).fill(0), ...gridRows[r], ...Array(padRight).fill(0)];
    }

    // ngx‑charts ters çizdiği için satırları dikeyde çevir
    const flippedRows = gridRows.slice().reverse();

    // Son olarak heatmap verisini oluştur
    const heatmapData: Array<{ name: string; series: Array<{ name: string; value: number }> }> = [];
    for (let w = 0; w < totalWeeks; w++) {
      heatmapData.push({
        name: `Week ${w + 1}`,
        series: daysOfWeek.map((day, rowIdx) => ({
          name: day,
          value: flippedRows[rowIdx][w] ? 10 : Math.floor(Math.random() * 3) + 1, // boşluklara 1–3 arası rastgele
        })),
      });
    }
    console.log('heatmapData:', JSON.stringify(heatmapData));
    return heatmapData;
  }

  generateHeatmapData2() {
    const daysOfWeek = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
    const totalWeeks = 52;

    // 5×7 piksel font desenleri:
    const letters: { [char: string]: number[][] } = {
      M: [
        [1, 0, 0, 0, 1],
        [1, 1, 0, 1, 1],
        [1, 0, 1, 0, 1],
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
      ],
      U: [
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
        [0, 1, 1, 1, 0],
      ],
      S: [
        [0, 1, 1, 1, 1],
        [1, 0, 0, 0, 0],
        [1, 0, 0, 0, 0],
        [0, 1, 1, 1, 0],
        [0, 0, 0, 0, 1],
        [0, 0, 0, 0, 1],
        [1, 1, 1, 1, 0],
      ],
      T: [
        [1, 1, 1, 1, 1],
        [0, 0, 1, 0, 0],
        [0, 0, 1, 0, 0],
        [0, 0, 1, 0, 0],
        [0, 0, 1, 0, 0],
        [0, 0, 1, 0, 0],
        [0, 0, 1, 0, 0],
      ],
      A: [
        [0, 1, 1, 1, 0],
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
        [1, 1, 1, 1, 1],
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
        [1, 0, 0, 0, 1],
      ],
      F: [
        [1, 1, 1, 1, 1],
        [1, 0, 0, 0, 0],
        [1, 0, 0, 0, 0],
        [1, 1, 1, 1, 0],
        [1, 0, 0, 0, 0],
        [1, 0, 0, 0, 0],
        [1, 0, 0, 0, 0],
      ],
    };

    // Kelime ve harf arası 1 boşluk:
    const word = ['M', 'U', 'S', 'T', 'A', 'F', 'A'];
    // Önce 7×N’lik (N = 7×5 + 6×1 = 41) bir grid oluştur
    const gridRows: number[][] = Array.from({ length: 7 }, () => []);

    word.forEach((ch, idx) => {
      const pat = letters[ch];
      for (let r = 0; r < 7; r++) {
        gridRows[r].push(...pat[r]);
      }
      // son harften sonra boşluk koyma
      if (idx < word.length - 1) {
        for (let r = 0; r < 7; r++) gridRows[r].push(0);
      }
    });

    // 41 sütunu 52’ye tam ortala:
    const currentCols = gridRows[0].length; // 41
    const padLeft = Math.floor((totalWeeks - currentCols) / 2); // 5
    const padRight = totalWeeks - currentCols - padLeft; // 6

    for (let r = 0; r < 7; r++) {
      gridRows[r] = [...Array(padLeft).fill(0), ...gridRows[r], ...Array(padRight).fill(0)];
    }

    // Artık gridRows[r][c] = 1 ise piksel koyu, 0 ise açık
    const heatmapData: any[] = [];
    for (let c = 0; c < totalWeeks; c++) {
      const weekData = {
        name: `Week ${c + 1}`,
        series: daysOfWeek.map((d, r) => ({
          name: d,
          // 1 → koyu, 0 → açık
          value: gridRows[r][c] ? 10 : 0,
        })),
      };
      heatmapData.push(weekData);
    }

    return heatmapData;
  }

  generateHeatmapData() {
    const daysOfWeek = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
    let startDate = new Date();
    startDate.setFullYear(startDate.getFullYear() - 1); // 1 yıl geriye git

    let heatmapData = [];

    for (let week = 0; week < 52; week++) {
      let currentDate = new Date(startDate);
      currentDate.setDate(startDate.getDate() + week * 7);

      let year = currentDate.getFullYear();
      let month = currentDate.toLocaleString('en-US', { month: 'short' }); // "Jan", "Feb" gibi

      let weekData = {
        name: `${year} - ${month} - Week ${week + 1}`,
        series: [] as { name: string; value: number }[],
      };

      daysOfWeek.forEach((day, index) => {
        let date = new Date(startDate);
        date.setDate(startDate.getDate() + week * 7 + index);

        weekData.series.push({
          name: day, // Y ekseni (Pazartesi - Pazar)
          value: Math.floor(Math.random() * 10), // 0-10 arasında rastgele aktivite
        });
      });

      heatmapData.push(weekData);
    }

    return heatmapData;
  }

  lastMonthDisplayed: string = ''; // En son gösterilen ayı takip etmek için

  colorScheme2: Color = {
    name: 'cool',
    selectable: false,
    group: ScaleType.Linear,
    domain: ['#5AA454', '#E44D25', '#CFC0BB', '#7aa3e5', '#a8385d', '#aae3f5'],
  };
  cardColor: string = '#232837';

  single: any[] = [];

  xAxisTickFormatting(val: string): string {
    // "2024 - Feb - Week 1" formatındaki değerden ayı al
    const monthMatch = val.match(/- (\w{3}) -/);
    if (!monthMatch) return ''; // Eğer ay bilgisi bulunamazsa boş döndür

    const monthName = monthMatch[1]; // "Jan", "Feb", "Mar" gibi

    // Eğer bu ay daha önce gösterildiyse tekrar gösterme
    if (this.lastMonthDisplayed === monthName) {
      return ''; // Aynı ay içindeki diğer haftalar boş olsun
    }

    this.lastMonthDisplayed = monthName; // Yeni ayı güncelle
    return monthName; // Sadece ilk hafta için ay ismini göster
    // return `<span style="fill: red;">${monthName}</span>`; // Kırmızı renkte gösterir
  }

  constructor(
    private studentService: StudentService,
    private route: ActivatedRoute,
    private router: Router,
    private snackBar: MatSnackBar
  ) {}

  onSelect(event: any) {
    console.log(event);
  }

  public chartClicked(e: any): void {
    console.log(e);
  }

  ngOnInit() {
    this.studentService.loadGrades().subscribe((response) => {
      this.grades = response;
    });

    this.studentService.getProfile().subscribe((response) => {
      this.student = response;
    });

    this.testService.studentStatistics().subscribe((response) => {
      this.single = [];
      this.single = this.single.concat([
        {
          name: 'Çözülen Test',
          value: response.total.completedTests,
        },
        {
          name: 'Çalışma Süresi (dk)',
          value: response.total.totalTimeSpentMinutes,
        },
        {
          name: 'Çözülen Soru',
          value: response.total.totalCorrectAnswers + response.total.totalWrongAnswers,
        },
        {
          name: 'Doğru Cevap',
          value: response.total.totalCorrectAnswers,
        },
      ]);

      console.log('Student Statistics:', this.single);
    });
  }

  changeGrade(): void {
    if (this.student) {
      this.studentService.updateGrade(this.student.gradeId).subscribe(() => {
        this.snackBar.open('Grade güncellendi!', 'Tamam', { duration: 3000 });
      });
    }
  }

  onAvatarChange(event: any): void {
    const file = event.target.files[0];
    if (file) {
      this.studentService.updateAvatar(file).subscribe((response) => {
        this.student = response;
        this.snackBar.open('Avatar güncellendi!', 'Tamam', { duration: 3000 });
      });
    }
  }
}
