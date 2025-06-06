import { Component, OnInit, Input, Output, EventEmitter, inject, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTabsModule } from '@angular/material/tabs';
import { MatDividerModule } from '@angular/material/divider';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { FormsModule, ReactiveFormsModule, FormControl } from '@angular/forms';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatMenuModule } from '@angular/material/menu';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { StudyService } from '../../services/study.service';
import { StudyContent } from '../../models/study-content';
import { Comment } from '../../models/comment';
import { Note } from '../../models/note';

@Component({
  selector: 'app-study-content-viewer',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatTabsModule,
    MatDividerModule,
    MatFormFieldModule,
    MatInputModule,
    FormsModule,
    ReactiveFormsModule,
    MatExpansionModule,
    MatMenuModule,
  ],
  templateUrl: './study-content-viewer.component.html',
  styleUrls: ['./study-content-viewer.component.scss'],
})
export class StudyContentViewerComponent implements OnInit, OnChanges {
  studyService = inject(StudyService);
  sanitizer = inject(DomSanitizer);

  @Input() contentId!: number;
  @Input() isCompleted = false;
  @Output() markCompleted = new EventEmitter<number>();

  content: StudyContent | null = null;
  comments: Comment[] = [];
  notes: Note[] = [];
  isLoading = true;
  isFavorite = false;

  commentControl = new FormControl('');
  noteControl = new FormControl('');
  currentNote: Note | null = null;

  // For safe iframe URL
  safeUrl: SafeResourceUrl | null = null;

  ngOnInit() {
    if (this.contentId) {
      this.loadContent();
    }
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['contentId'] && !changes['contentId'].firstChange) {
      this.loadContent();
    }
  }

  loadContent() {
    this.isLoading = true;

    // For demo, we'll use the mock data from service
    this.studyService.getContentBySubtopic(1).subscribe((contents) => {
      this.content = contents.find((c) => c.id === this.contentId) || null;

      if (this.content) {
        // Update safe URL for iframe
        if (this.content.type === 'video' && this.content.url) {
          this.safeUrl = this.sanitizer.bypassSecurityTrustResourceUrl(this.content.url);
        }

        // Load comments
        this.loadComments();

        // Load notes
        this.loadNotes();
      }

      this.isLoading = false;
    });
  }

  loadComments() {
    if (!this.contentId) return;

    this.studyService.getCommentsByContent(this.contentId).subscribe({
      next: (comments) => {
        this.comments = comments;
      },
      error: (error) => {
        console.error('Error loading comments:', error);
        this.comments = [];
      },
    });
  }

  loadNotes() {
    if (!this.contentId) return;

    this.studyService.getNotesByContent(this.contentId).subscribe({
      next: (notes) => {
        this.notes = notes;

        // Set current note if exists
        if (notes.length > 0) {
          this.currentNote = notes[0];
          this.noteControl.setValue(notes[0].text);
        }
      },
      error: (error) => {
        console.error('Error loading notes:', error);
        this.notes = [];
      },
    });
  }

  addComment() {
    const commentText = this.commentControl.value?.trim();
    if (!commentText || !this.contentId) return;

    this.studyService
      .addComment({
        text: commentText,
        contentId: this.contentId,
      })
      .subscribe({
        next: (newComment) => {
          this.comments = [newComment, ...this.comments];
          this.commentControl.reset();
        },
        error: (error) => {
          console.error('Error adding comment:', error);
        },
      });
  }

  saveNote() {
    const noteText = this.noteControl.value?.trim();
    if (!noteText || !this.contentId) return;

    const note: Note = {
      id: this.currentNote?.id,
      text: noteText,
      contentId: this.contentId,
      userId: 103,
    };

    this.studyService.saveNote(note).subscribe({
      next: (savedNote) => {
        if (this.currentNote) {
          // Update existing note
          this.notes = this.notes.map((n) => (n.id === savedNote.id ? savedNote : n));
        } else {
          // Add new note
          this.notes = [savedNote, ...this.notes];
        }
        this.currentNote = savedNote;
      },
      error: (error) => {
        console.error('Error saving note:', error);
      },
    });
  }

  toggleFavorite() {
    this.isFavorite = !this.isFavorite;

    if (this.isFavorite) {
      this.studyService.addToFavorites(this.contentId).subscribe();
    } else {
      this.studyService.removeFromFavorites(this.contentId).subscribe();
    }
  }

  onMarkCompleted() {
    if (this.contentId) {
      this.markCompleted.emit(this.contentId);
      this.isCompleted = true;
    }
  }

  shareContent(platform: string) {
    let shareUrl = window.location.href;
    let text = `${this.content?.title || 'Ders içeriği'} - ExamApp`;

    switch (platform) {
      case 'twitter':
        window.open(
          `https://twitter.com/intent/tweet?text=${encodeURIComponent(text)}&url=${encodeURIComponent(shareUrl)}`,
          '_blank'
        );
        break;
      case 'facebook':
        window.open(`https://www.facebook.com/sharer/sharer.php?u=${encodeURIComponent(shareUrl)}`, '_blank');
        break;
      case 'whatsapp':
        window.open(`https://wa.me/?text=${encodeURIComponent(text + ' ' + shareUrl)}`, '_blank');
        break;
      case 'copy':
        navigator.clipboard.writeText(shareUrl).then(() => {
          alert('Link kopyalandı!');
        });
        break;
    }
  }
}
