import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { Subject } from '../models/subject';
import { Topic } from '../models/topic';
import { SubTopic } from '../models/subtopic';
import { StudyContent } from '../models/study-content';
import { Note } from '../models/note';
import { Comment } from '../models/comment';

@Injectable({
  providedIn: 'root',
})
export class StudyService {
  private baseUrl = '/api/exam/study';

  constructor(private http: HttpClient) {}

  // Get all subjects (for catalog)
  getSubjects(): Observable<Subject[]> {
    // For mock data until backend is implemented
    return of([
      { id: 1, name: 'Matematik' },
      { id: 2, name: 'Türkçe' },
      { id: 3, name: 'Fen Bilimleri' },
      { id: 4, name: 'Sosyal Bilgiler' },
    ]);
  }

  // Get topics by subject id
  getTopicsBySubject(subjectId: number): Observable<Topic[]> {
    // For mock data until backend is implemented
    return of([
      { id: 1, name: 'Doğal Sayılar', subjectId: 1, gradeId: 1 },
      { id: 2, name: 'Kesirler', subjectId: 1, gradeId: 1 },
      { id: 3, name: 'Geometri', subjectId: 1, gradeId: 1 },
    ]);
  }

  // Get subtopics by topic id
  getSubtopicsByTopic(topicId: number): Observable<SubTopic[]> {
    // For mock data until backend is implemented
    return of([
      { id: 1, name: 'Sayı Kavramı', topicId: 1 },
      { id: 2, name: 'Ritmik Sayma', topicId: 1 },
      { id: 3, name: 'Toplama İşlemi', topicId: 1 },
    ]);
  }

  // Get study content by subtopic id
  getContentBySubtopic(subtopicId: number): Observable<StudyContent[]> {
    // For mock data until backend is implemented
    return of([
      {
        id: 1,
        title: 'Doğal Sayılar Tanıtımı',
        type: 'video',
        url: 'https://www.youtube.com/embed/dQw4w9WgXcQ',
        subtopicId: 1,
        description: 'Bu video doğal sayılar konusunun temel kavramlarını açıklar.',
      },
      {
        id: 2,
        title: 'Doğal Sayılar Çalışma Dökümanı',
        type: 'document',
        url: '/assets/docs/dogal-sayilar.pdf',
        subtopicId: 1,
        description: 'Bu döküman doğal sayılar konusuyla ilgili alıştırmaları içerir.',
      },
    ]);
  }

  // Get comments for content
  getCommentsByContent(contentId: number): Observable<Comment[]> {
    // For mock data until backend is implemented
    return of([
      {
        id: 1,
        text: 'Bu video çok yararlı oldu, teşekkürler!',
        userId: 101,
        username: 'Ayşe Yılmaz',
        contentId: 1,
        createdAt: new Date(Date.now() - 86400000),
      },
      {
        id: 2,
        text: 'Acaba ekstra kaynak önerebilir misiniz?',
        userId: 102,
        username: 'Mehmet Demir',
        contentId: 1,
        createdAt: new Date(Date.now() - 43200000),
      },
    ]);
  }

  // Add a new comment
  addComment(comment: { text: string; contentId: number }): Observable<Comment> {
    // For mock data until backend is implemented
    const newComment: Comment = {
      id: Math.floor(Math.random() * 1000),
      text: comment.text,
      userId: 103,
      username: 'Mevcut Kullanıcı',
      contentId: comment.contentId,
      createdAt: new Date(),
    };
    return of(newComment);
  }

  // Add content to favorites
  addToFavorites(contentId: number): Observable<boolean> {
    // For mock data until backend is implemented
    return of(true);
  }

  // Remove content from favorites
  removeFromFavorites(contentId: number): Observable<boolean> {
    // For mock data until backend is implemented
    return of(true);
  }

  // Get user's favorite contents
  getFavorites(): Observable<StudyContent[]> {
    // For mock data until backend is implemented
    return of([
      {
        id: 1,
        title: 'Doğal Sayılar Tanıtımı',
        type: 'video',
        url: 'https://www.youtube.com/embed/dQw4w9WgXcQ',
        subtopicId: 1,
        description: 'Bu video doğal sayılar konusunun temel kavramlarını açıklar.',
      },
    ]);
  }

  // Save a note
  saveNote(note: Note): Observable<Note> {
    // For mock data until backend is implemented
    const newNote: Note = {
      ...note,
      id: note.id || Math.floor(Math.random() * 1000),
      createdAt: note.createdAt || new Date(),
      updatedAt: new Date(),
    };
    return of(newNote);
  }

  // Get notes by content
  getNotesByContent(contentId: number): Observable<Note[]> {
    // For mock data until backend is implemented
    return of([
      {
        id: 1,
        text: 'Bu konuyla ilgili dikkat edilmesi gereken noktalar...',
        contentId: 1,
        userId: 103,
        createdAt: new Date(Date.now() - 259200000),
        updatedAt: new Date(Date.now() - 172800000),
      },
    ]);
  }

  // Mark content as completed
  markAsCompleted(contentId: number): Observable<boolean> {
    // For mock data until backend is implemented
    return of(true);
  }

  // Get user's progress
  getUserProgress(): Observable<
    { subjectId: number; topicId: number; subtopicId: number; contentId: number; completed: boolean }[]
  > {
    // For mock data until backend is implemented
    return of([
      { subjectId: 1, topicId: 1, subtopicId: 1, contentId: 1, completed: true },
      { subjectId: 1, topicId: 1, subtopicId: 1, contentId: 2, completed: false },
    ]);
  }

  // Search for content by keyword
  searchContent(keyword: string): Observable<StudyContent[]> {
    // For mock data until backend is implemented
    return of([
      {
        id: 1,
        title: 'Doğal Sayılar Tanıtımı',
        type: 'video',
        url: 'https://www.youtube.com/embed/dQw4w9WgXcQ',
        subtopicId: 1,
        description: 'Bu video doğal sayılar konusunun temel kavramlarını açıklar.',
      },
    ]);
  }
}
