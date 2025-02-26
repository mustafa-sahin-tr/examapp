import { Component, OnInit } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { StudentProfile } from '../../models/student-profile';
import { StudentService } from '../../services/student.service';
import { ActivatedRoute, Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import {MatListModule} from '@angular/material/list';
import {MatTooltipModule} from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatSelectModule } from '@angular/material/select';
import { Grade } from '../../models/student';
import { RewardPathComponent } from '../reward-path/reward-path.component';
import { PointCardComponent } from '../../shared/components/point-card/point-card.component';
import { BadgeBoxComponent } from '../../shared/components/badge-box/badge-box.component';
import { LeaderboardComponent } from '../../shared/components/leaderboard/leaderboard.component';
import { SectionHeaderComponent } from '../../shared/components/section-header/section-header.component';
import { MatTabsModule } from '@angular/material/tabs';
import { NgxChartsModule, Color, ScaleType } from '@swimlane/ngx-charts';

@Component({
  selector: 'app-student-profile',
  templateUrl: './student-profile.component.html',
  standalone: true,
  styleUrls: ['./student-profile.component.scss'],
  imports: [CommonModule, MatCardModule, MatIconModule, MatListModule, MatTooltipModule,
    MatSnackBarModule, MatSelectModule, PointCardComponent, BadgeBoxComponent, 
    LeaderboardComponent, SectionHeaderComponent, MatTabsModule, NgxChartsModule
  ]
})
export class StudentProfileComponent implements OnInit {
  student: StudentProfile | null = null;
  studentId: number = 0;
  grades: Grade[] = [];
  activeTab = 0;  // Varsayılan olarak ilk sekme açık
  badges = [
    { title: "Activity", level: 1, icon: "http://localhost/minio-api/avatars/activity-badge.png" },
    { title: "Assignments", level: 2, icon: "http://localhost/minio-api/avatars/activity-badge.png" },
    { title: "Grades", level: 4, icon: "http://localhost/minio-api/avatars/activity-badge.png" },
    { title: "Attendance", level: 6, icon: "http://localhost/minio-api/avatars/activity-badge.png" },
    { title: "Discipline", level: 8, icon: "http://localhost/minio-api/avatars/activity-badge.png" },
    { title: "Communication", level: 7, icon: "http://localhost/minio-api/avatars/activity-badge.png" }
  ];

  classLeaderboard = [
    { name: "Safi Abu-Rashed", score: 1024, icon: "assets/icons/gold.png" },
    { name: "Ahmad Abed Al Rahman", score: 950, icon: "assets/icons/silver.png" },
    { name: "Hanan Saad", score: 912, icon: "assets/icons/bronze.png" },
    { name: "Omar Saleem", score: 880, icon: "" }
  ];
  
  groupLeaderboard = [
    { name: "Safi Abu-Rashed", score: 740, grade: "Grade 1 - Blue", icon: "assets/icons/gold.png" },
    { name: "Ahmad Abed Al Rahman", score: 695, grade: "Grade 1 - Blue", icon: "assets/icons/silver.png" },
    { name: "Hanan Saad", score: 690, grade: "Grade 1 - Blue", icon: "assets/icons/bronze.png" }
  ];

  colorScheme: Color = {
    name: 'heatmapScheme',
    selectable: false,
    group: ScaleType.Quantile,
    domain: ['#f5f7f5', '#d3f5d3', '#a6f7a6', '#74fc74', '#4bfa4b', '#26fc26', '#03fc03']
  };

  // Günlük veriyi haftalara bölüp heatmap formatına uygun hale getiriyoruz
  activityData = this.generateHeatmapData();

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
        series: [] as { name: string; value: number }[]
      };
  
      daysOfWeek.forEach((day, index) => {
        let date = new Date(startDate);
        date.setDate(startDate.getDate() + week * 7 + index);
  
        weekData.series.push({
          name: day, // Y ekseni (Pazartesi - Pazar)
          value: Math.floor(Math.random() * 10) // 0-10 arasında rastgele aktivite
        });
      });
  
      heatmapData.push(weekData);
    }
  
    return heatmapData;
  }
  

  lastMonthDisplayed: string = ''; // En son gösterilen ayı takip etmek için

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
  }
  
  
  
  

  constructor(private studentService: StudentService, private route: ActivatedRoute,
      private router: Router, private snackBar: MatSnackBar ) {}

  ngOnInit() {
    this.studentService.loadGrades().subscribe(response => {
        this.grades = response;
    });

    this.studentService.getProfile().subscribe(response => {
      this.student = response;
    });
  }

  changeGrade(): void {
    if(this.student) {
      this.studentService.updateGrade(this.student.gradeId).subscribe(() => {
        this.snackBar.open('Grade güncellendi!', 'Tamam', { duration: 3000 });
      });
    }
  }

  onAvatarChange(event: any): void {
    const file = event.target.files[0];
    if (file) {
      this.studentService.updateAvatar(file).subscribe(response => {
        this.student = response;
        this.snackBar.open('Avatar güncellendi!', 'Tamam', { duration: 3000 });
      });
    }
  }
}
