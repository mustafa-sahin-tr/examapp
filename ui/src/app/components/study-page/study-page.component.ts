import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatTabsModule } from '@angular/material/tabs';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatBadgeModule } from '@angular/material/badge';
import { MatDividerModule } from '@angular/material/divider';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { StudyService } from '../../services/study.service';
import { Subject } from '../../models/subject';
import { StudyCatalogComponent } from '../study-catalog/study-catalog.component';
import { StudyTopicComponent } from '../study-topic/study-topic.component';
import { StudySubtopicComponent } from '../study-subtopic/study-subtopic.component';
import { StudyContentViewerComponent } from '../study-content-viewer/study-content-viewer.component';
import { SectionHeaderComponent } from '../../shared/components/section-header/section-header.component';

@Component({
  selector: 'app-study-page',
  standalone: true,
  imports: [
    CommonModule,
    MatTabsModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatProgressBarModule,
    MatInputModule,
    MatFormFieldModule,
    MatBadgeModule,
    MatDividerModule,
    FormsModule,
    ReactiveFormsModule,
    StudyCatalogComponent,
    StudyTopicComponent,
    StudySubtopicComponent,
    StudyContentViewerComponent,
    SectionHeaderComponent,
  ],
  templateUrl: './study-page.component.html',
  styleUrls: ['./study-page.component.scss'],
})
export class StudyPageComponent implements OnInit {
  studyService = inject(StudyService);

  // Reactive signals for state management
  currentView = signal<'catalog' | 'topic' | 'subtopic' | 'content'>('catalog');
  selectedSubjectId = signal<number | null>(null);
  selectedTopicId = signal<number | null>(null);
  selectedSubtopicId = signal<number | null>(null);
  selectedContentId = signal<number | null>(null);
  searchQuery = signal<string>('');

  // Progress tracking
  userProgress = signal<
    { subjectId: number; topicId: number; subtopicId: number; contentId: number; completed: boolean }[]
  >([]);

  // Computed value for overall progress percentage
  overallProgress = computed(() => {
    const progress = this.userProgress();
    if (progress.length === 0) return 0;

    const completedCount = progress.filter((p) => p.completed).length;
    return Math.round((completedCount / progress.length) * 100);
  });

  constructor(private router: Router) {}

  ngOnInit() {
    // Load user's progress
    this.studyService.getUserProgress().subscribe((progress) => {
      this.userProgress.set(progress);
    });
  }

  // Navigation methods
  navigateToCatalog() {
    this.currentView.set('catalog');
    this.selectedSubjectId.set(null);
    this.selectedTopicId.set(null);
    this.selectedSubtopicId.set(null);
    this.selectedContentId.set(null);
  }

  navigateToTopic(subjectId: number) {
    this.selectedSubjectId.set(subjectId);
    this.currentView.set('topic');
    this.selectedTopicId.set(null);
    this.selectedSubtopicId.set(null);
    this.selectedContentId.set(null);
  }

  navigateToSubtopic(topicId: number) {
    this.selectedTopicId.set(topicId);
    this.currentView.set('subtopic');
    this.selectedSubtopicId.set(null);
    this.selectedContentId.set(null);
  }

  navigateToContent(subtopicId: number, contentId: number) {
    this.selectedSubtopicId.set(subtopicId);
    this.selectedContentId.set(contentId);
    this.currentView.set('content');
  }

  // Search function
  onSearch(query: string) {
    this.searchQuery.set(query);
    if (query) {
      this.studyService.searchContent(query).subscribe((results) => {
        // Handle search results
        if (results.length > 0) {
          this.selectedContentId.set(results[0].id);
          this.currentView.set('content');
        }
      });
    }
  }

  // Track content completion
  markContentAsCompleted(contentId: number) {
    this.studyService.markAsCompleted(contentId).subscribe((success) => {
      if (success) {
        const progress = [...this.userProgress()];
        const contentIndex = progress.findIndex((p) => p.contentId === contentId);

        if (contentIndex !== -1) {
          progress[contentIndex].completed = true;
          this.userProgress.set(progress);
        } else {
          // Add new completed item
          const newItem = {
            subjectId: this.selectedSubjectId() || 0,
            topicId: this.selectedTopicId() || 0,
            subtopicId: this.selectedSubtopicId() || 0,
            contentId: contentId,
            completed: true,
          };
          this.userProgress.set([...progress, newItem]);
        }
      }
    });
  }

  // Check if content is completed
  isContentCompleted(contentId: number): boolean {
    return this.userProgress().some((p) => p.contentId === contentId && p.completed);
  }
}
