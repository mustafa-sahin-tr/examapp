import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Paged } from '../models/test-instance';

/** Issue #1282: ödev hedefi (backend `SchoolHomeworkTarget`). */
export type HomeworkTarget = 'Grade' | 'Section' | 'Students';

/** Issue #1282: öğretmen kontrolü (backend `SchoolHomeworkCheckStatus`). Puan yok. */
export type HomeworkCheckStatus = 'Done' | 'Partial' | 'NotDone';

export interface HomeworkCreateRequest {
  target: HomeworkTarget;
  gradeId?: number;
  sectionId?: number;
  studentIds?: number[];
  subjectId: number;
  bookName?: string | null;
  pages?: string | null;
  note?: string | null;
  /** Yerel takvim günü, `yyyy-MM-dd`. */
  dueDate: string;
}

/** Issue #1282: öğretmenin ödev satırı; sayılar öğretmenin güncel okulundaki öğrenciler. */
export interface TeacherHomework {
  id: number;
  target: HomeworkTarget;
  targetLabel: string | null;
  subjectId: number;
  subjectName: string;
  bookName: string | null;
  pages: string | null;
  note: string | null;
  dueDate: string;
  createdAt: string;
  isPastDue: boolean;
  studentCount: number;
  studentDoneCount: number;
  doneCount: number;
  partialCount: number;
  notDoneCount: number;
  overdueCount: number;
}

export interface HomeworkStudentStatus {
  studentId: number;
  fullName: string;
  studentNumber: string;
  studentDoneAt: string | null;
  teacherStatus: HomeworkCheckStatus | null;
  teacherNote: string | null;
  checkedAt: string | null;
  isOverdue: boolean;
}

export interface HomeworkDetail {
  homework: TeacherHomework;
  students: HomeworkStudentStatus[];
}

/** Issue #1282: öğrencinin ödev satırı. */
export interface StudentHomework {
  id: number;
  subjectName: string;
  bookName: string | null;
  pages: string | null;
  note: string | null;
  dueDate: string;
  createdAt: string;
  teacherName: string;
  studentDoneAt: string | null;
  teacherStatus: HomeworkCheckStatus | null;
  teacherNote: string | null;
  isOverdue: boolean;
}

/** Issue #1283: ödev olarak verilen test / etkinlik ataması (öğretmen). Sayılar atama ilerleme ekranıyla aynı. */
export interface TeacherAssignmentHomework {
  assignmentId: number;
  worksheetId: number;
  worksheetName: string;
  worksheetKind: 'Test' | 'Activity';
  targetType: 'Grade' | 'Student';
  targetName: string;
  startAt: string;
  dueAt: string;
  isPastDue: boolean;
  studentCount: number;
  completedCount: number;
  inProgressCount: number;
  notStartedCount: number;
  overdueCount: number;
}

/** Issue #1283: öğrencinin test / etkinlik ödevi; durum oturumdan otomatik (veli paneliyle aynı kova). */
export interface StudentAssignmentHomework {
  assignmentId: number;
  worksheetId: number;
  worksheetName: string;
  worksheetKind: 'Test' | 'Activity';
  subjectName: string | null;
  teacherName: string;
  dueAt: string;
  status: 'Completed' | 'Pending' | 'Overdue';
  inProgress: boolean;
  completedAt: string | null;
}

export const HOMEWORK_BOOK_MAX = 120;
export const HOMEWORK_PAGES_MAX = 60;
export const HOMEWORK_NOTE_MAX = 500;
export const HOMEWORK_TEACHER_NOTE_MAX = 300;
export const HOMEWORK_MAX_DUE_DAYS = 180;

/**
 * Issue #1282 (#1071 S7): düz ödev. Öğretmen okulunun sınıfına / şubesine / seçtiği öğrencilere ödev verir, öğrenci "yaptım" der,
 * öğretmen Yapıldı / Kısmen / Yapılmadı ile kontrol eder. Okul, kapsam ve gecikme sunucuda çözülür.
 */
@Injectable({ providedIn: 'root' })
export class HomeworkService {
  private readonly http = inject(HttpClient);
  private readonly url = '/api/exam/homework';
  private readonly studentUrl = '/api/exam/student/homework';

  create(request: HomeworkCreateRequest): Observable<TeacherHomework> {
    return this.http.post<TeacherHomework>(this.url, request);
  }

  list(page: number, pageSize: number): Observable<Paged<TeacherHomework>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<Paged<TeacherHomework>>(this.url, { params });
  }

  detail(id: number): Observable<HomeworkDetail> {
    return this.http.get<HomeworkDetail>(`${this.url}/${id}`);
  }

  check(id: number, studentId: number, status: HomeworkCheckStatus | null, note: string | null): Observable<HomeworkStudentStatus> {
    return this.http.put<HomeworkStudentStatus>(`${this.url}/${id}/students/${studentId}`, { status, note });
  }

  remove(id: number): Observable<void> {
    return this.http.delete<void>(`${this.url}/${id}`);
  }

  /** Issue #1283: ödev olarak verdiğim test / etkinlik atamaları. */
  assignments(): Observable<TeacherAssignmentHomework[]> {
    return this.http.get<TeacherAssignmentHomework[]>(`${this.url}/assignments`);
  }

  /** Issue #1283: öğrencinin test / etkinlik ödevleri. */
  myAssignments(): Observable<StudentAssignmentHomework[]> {
    return this.http.get<StudentAssignmentHomework[]>(`${this.studentUrl}/assignments`);
  }

  mine(): Observable<StudentHomework[]> {
    return this.http.get<StudentHomework[]>(this.studentUrl);
  }

  markDone(id: number, done: boolean): Observable<StudentHomework> {
    return this.http.put<StudentHomework>(`${this.studentUrl}/${id}/done`, { done });
  }
}
