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
@Component({
  selector: 'app-student-profile',
  templateUrl: './student-profile.component.html',
  styleUrls: ['./student-profile.component.scss'],
  imports: [CommonModule, MatCardModule, MatIconModule, MatListModule, MatTooltipModule]
})
export class StudentProfileComponent implements OnInit {
  student: StudentProfile | null = null;
  studentId: number = 0;
  constructor(private studentService: StudentService, private route: ActivatedRoute,      
      private router: Router ) {}

  ngOnInit() {
    this.studentService.getProfile().subscribe(response => {
      this.student = response;
    });
  }
}
