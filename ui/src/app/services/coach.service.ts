import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import {
  CoachGoal,
  CoachNote,
  CoachOverview,
  CoachStudent,
  ParentCoachSummary,
  SaveCoachGoal,
  StudentCoachGoal,
} from '../models/coach.model';
import { decodeFlashcardText as decodeText, encodeFlashcardText as encodeText } from './flashcard.service';

/** İş kuralı hatası (`{ errorCode }`) bilgi kartlarıyla aynı biçimde gelir. */
export { flashcardErrorCode as coachErrorCode } from './flashcard.service';

function decodeGoal<T extends { title: string }>(goal: T): T {
  return { ...goal, title: decodeText(goal.title) };
}

function decodeNote(note: CoachNote): CoachNote {
  return { ...note, body: decodeText(note.body) };
}

/**
 * Issue #700: koç paneli. Hedef ve not sunucuda HTML temizleyiciden geçer; bilgi kartları (#699) gibi gidişte kodlanır,
 * gelişte çözülür. Kapsam dışı öğrenci 404 döner.
 */
@Injectable({ providedIn: 'root' })
export class CoachService {
  private readonly http = inject(HttpClient);
  private readonly teacherUrl = '/api/exam/teacher/coach/students';

  listStudents(): Observable<CoachStudent[]> {
    return this.http.get<CoachStudent[]>(this.teacherUrl);
  }

  getOverview(studentId: number): Observable<CoachOverview> {
    return this.http
      .get<CoachOverview>(`${this.teacherUrl}/${studentId}/overview`)
      .pipe(map((overview) => ({ ...overview, goals: overview.goals.map(decodeGoal) })));
  }

  listGoals(studentId: number, weekStart?: string): Observable<CoachGoal[]> {
    const params: Record<string, string> = weekStart ? { weekStart } : {};
    return this.http
      .get<CoachGoal[]>(`${this.teacherUrl}/${studentId}/goals`, { params })
      .pipe(map((goals) => goals.map(decodeGoal)));
  }

  addGoal(studentId: number, goal: SaveCoachGoal): Observable<CoachGoal> {
    return this.http
      .post<CoachGoal>(`${this.teacherUrl}/${studentId}/goals`, { ...goal, title: encodeText(goal.title) })
      .pipe(map(decodeGoal));
  }

  updateGoal(studentId: number, id: number, title: string): Observable<CoachGoal> {
    return this.http
      .put<CoachGoal>(`${this.teacherUrl}/${studentId}/goals/${id}`, { title: encodeText(title) })
      .pipe(map(decodeGoal));
  }

  deleteGoal(studentId: number, id: number): Observable<void> {
    return this.http.delete<void>(`${this.teacherUrl}/${studentId}/goals/${id}`);
  }

  listNotes(studentId: number): Observable<CoachNote[]> {
    return this.http.get<CoachNote[]>(`${this.teacherUrl}/${studentId}/notes`).pipe(map((notes) => notes.map(decodeNote)));
  }

  addNote(studentId: number, body: string): Observable<CoachNote> {
    return this.http
      .post<CoachNote>(`${this.teacherUrl}/${studentId}/notes`, { body: encodeText(body) })
      .pipe(map(decodeNote));
  }

  updateNote(studentId: number, id: number, body: string): Observable<CoachNote> {
    return this.http
      .put<CoachNote>(`${this.teacherUrl}/${studentId}/notes/${id}`, { body: encodeText(body) })
      .pipe(map(decodeNote));
  }

  deleteNote(studentId: number, id: number): Observable<void> {
    return this.http.delete<void>(`${this.teacherUrl}/${studentId}/notes/${id}`);
  }

  /** Öğrencinin bu haftaki hedefleri, bütün koçlarından. */
  getMyGoals(): Observable<StudentCoachGoal[]> {
    return this.http.get<StudentCoachGoal[]>('/api/exam/student/me/coach-goals').pipe(map((goals) => goals.map(decodeGoal)));
  }

  /** Issue #893: öğrenci bu haftaki hedefini işaretler ya da işareti kaldırır. */
  setMyGoalDone(id: number, done: boolean): Observable<StudentCoachGoal> {
    return this.http.put<StudentCoachGoal>(`/api/exam/student/me/coach-goals/${id}/done`, { done }).pipe(map(decodeGoal));
  }

  getChildSummary(studentId: number): Observable<ParentCoachSummary> {
    return this.http
      .get<ParentCoachSummary>(`/api/exam/parent/children/${studentId}/coach-summary`)
      .pipe(map((summary) => ({ ...summary, goals: summary.goals.map(decodeGoal) })));
  }
}
