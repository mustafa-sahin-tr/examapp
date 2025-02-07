import { Component, OnInit } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { StudentProfile } from '../models/student-profile';
import { StudentService } from '../services/student.service';
import { ActivatedRoute, Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import {MatListModule} from '@angular/material/list';
import {MatTooltipModule} from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatSelectModule } from '@angular/material/select';
import { Grade } from '../models/student';
@Component({
  selector: 'app-student-profile',
  templateUrl: './student-profile.component.html',
  standalone: true,
  styleUrls: ['./student-profile.component.scss'],
  imports: [CommonModule, MatCardModule, MatIconModule, MatListModule, MatTooltipModule,
    MatSnackBarModule, MatSelectModule
  ]
})
export class StudentProfileComponent implements OnInit {
  student: StudentProfile | null = null;
  studentId: number = 0;
  grades: Grade[] = [];

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
