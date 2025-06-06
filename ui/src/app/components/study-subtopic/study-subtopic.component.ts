import { Component, OnInit, Input, Output, EventEmitter, inject, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatRippleModule } from '@angular/material/core';
import { MatTabsModule } from '@angular/material/tabs';
import { MatDividerModule } from '@angular/material/divider';
import { MatChipsModule } from '@angular/material/chips';
import { StudyService } from '../../services/study.service';
import { SubTopic } from '../../models/subtopic';
import { Topic } from '../../models/topic';
import { StudyContent } from '../../models/study-content';

@Component({
  selector: 'app-study-subtopic',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatRippleModule,
    MatTabsModule,
    MatDividerModule,
    MatChipsModule,
  ],
  templateUrl: './study-subtopic.component.html',
  styleUrls: ['./study-subtopic.component.scss'],
})
export class StudySubtopicComponent implements OnInit, OnChanges {
  studyService = inject(StudyService);

  @Input() topicId!: number;
  @Output() contentSelected = new EventEmitter<{ subtopicId: number; contentId: number }>();

  topic: Topic | null = null;
  subtopics: SubTopic[] = [];
  contentsMap: Map<number, StudyContent[]> = new Map(); // Map subtopicId to contents
  isLoading = true;

  ngOnInit() {
    if (this.topicId) {
      this.loadSubtopics();
    }
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['topicId'] && !changes['topicId'].firstChange) {
      this.loadSubtopics();
    }
  }

  loadSubtopics() {
    this.isLoading = true;
    this.contentsMap.clear();

    this.studyService.getSubtopicsByTopic(this.topicId).subscribe({
      next: (subtopics) => {
        this.subtopics = subtopics;

        // Load content for each subtopic
        subtopics.forEach((subtopic) => {
          this.loadContents(subtopic.id);
        });

        // Get the topic details
        this.studyService.getTopicsBySubject(subtopics[0]?.topicId || 0).subscribe((topics) => {
          this.topic = topics.find((t) => t.id === this.topicId) || null;
        });

        this.isLoading = false;
      },
      error: (error) => {
        console.error('Error loading subtopics:', error);
        this.isLoading = false;
      },
    });
  }

  loadContents(subtopicId: number) {
    this.studyService.getContentBySubtopic(subtopicId).subscribe({
      next: (contents) => {
        this.contentsMap.set(subtopicId, contents);
      },
      error: (error) => {
        console.error(`Error loading contents for subtopic ${subtopicId}:`, error);
        this.contentsMap.set(subtopicId, []);
      },
    });
  }

  onSelectContent(subtopicId: number, contentId: number) {
    this.contentSelected.emit({ subtopicId, contentId });
  }

  getContentIcon(type: string): string {
    return type === 'video' ? 'videocam' : 'article';
  }

  getContentBySubtopic(subtopicId: number): StudyContent[] {
    return this.contentsMap.get(subtopicId) || [];
  }

  getVideoCount(subtopicId: number): number {
    const contents = this.getContentBySubtopic(subtopicId);
    return contents.filter((c) => c.type === 'video').length;
  }

  getDocumentCount(subtopicId: number): number {
    const contents = this.getContentBySubtopic(subtopicId);
    return contents.filter((c) => c.type === 'document').length;
  }
}
