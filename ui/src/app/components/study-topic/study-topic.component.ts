import { Component, OnInit, Input, Output, EventEmitter, inject, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatRippleModule } from '@angular/material/core';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { StudyService } from '../../services/study.service';
import { Topic } from '../../models/topic';
import { Subject } from '../../models/subject';

@Component({
  selector: 'app-study-topic',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatButtonModule, MatIconModule, MatRippleModule, MatProgressBarModule],
  templateUrl: './study-topic.component.html',
  styleUrls: ['./study-topic.component.scss'],
})
export class StudyTopicComponent implements OnInit, OnChanges {
  studyService = inject(StudyService);

  @Input() subjectId!: number;
  @Output() topicSelected = new EventEmitter<number>();

  subject: Subject | null = null;
  topics: Topic[] = [];
  isLoading = true;

  ngOnInit() {
    if (this.subjectId) {
      this.loadTopics();

      // Get the subject details
      this.studyService.getSubjects().subscribe((subjects) => {
        this.subject = subjects.find((s) => s.id === this.subjectId) || null;
      });
    }
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['subjectId'] && !changes['subjectId'].firstChange) {
      this.loadTopics();
    }
  }

  loadTopics() {
    this.isLoading = true;
    this.studyService.getTopicsBySubject(this.subjectId).subscribe({
      next: (topics) => {
        this.topics = topics;
        this.isLoading = false;
      },
      error: (error) => {
        console.error('Error loading topics:', error);
        this.isLoading = false;
      },
    });
  }

  onSelectTopic(topicId: number) {
    this.topicSelected.emit(topicId);
  }
}
