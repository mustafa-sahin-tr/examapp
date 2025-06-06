import { Component, OnInit, Output, EventEmitter, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatRippleModule } from '@angular/material/core';
import { StudyService } from '../../services/study.service';
import { Subject } from '../../models/subject';

@Component({
  selector: 'app-study-catalog',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatButtonModule, MatIconModule, MatRippleModule],
  templateUrl: './study-catalog.component.html',
  styleUrls: ['./study-catalog.component.scss'],
})
export class StudyCatalogComponent implements OnInit {
  studyService = inject(StudyService);

  subjects: Subject[] = [];
  isLoading = true;

  @Output() subjectSelected = new EventEmitter<number>();

  ngOnInit() {
    this.loadSubjects();
  }

  loadSubjects() {
    this.isLoading = true;
    this.studyService.getSubjects().subscribe({
      next: (subjects) => {
        this.subjects = subjects;
        this.isLoading = false;
      },
      error: (error) => {
        console.error('Error loading subjects:', error);
        this.isLoading = false;
      },
    });
  }

  onSelectSubject(subjectId: number) {
    this.subjectSelected.emit(subjectId);
  }
}
