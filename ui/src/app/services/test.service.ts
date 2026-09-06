import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { WorksheetListFilter } from '../models/worksheet-list-filter';
import { BehaviorSubject, Observable, of, switchMap, tap, map } from 'rxjs';
import { CalendarEvent, StudentCalendarResponse } from '../models/calendar-event';
import { CheckStudentResponse } from '../models/check-student-response';
import { Router } from '@angular/router';
import { Subject } from '../models/subject';
import { Topic } from '../models/topic';
import { SubTopic } from '../models/subtopic';
import {
  InstanceSummary,
  Exam,
  Paged,
  Test,
  TestInstance,
  WorksheetStudentVisibility,
  WorksheetTeacherSharing,
} from '../models/test-instance';
import {
  ApiResponse,
  AssignedWorksheet,
  TeacherWorksheetAssignments,
  WorksheetAssignmentRequest,
} from '../models/assignment';
import { StudentAnswer } from '../models/student-answer';
import { AnswerChoice, QuestionRegion } from '../models/draws';
import { Answer } from '../models/answer';
import { StudentStatisticsResponse } from '../models/statistics';
import { Question } from '../models/question';
import {
  CopyWorksheetResult,
  WorksheetDetail,
  WorksheetFromMistakesResult,
  WorksheetReminder,
  WorksheetReminderRequest,
} from '../models/worksheet-detail';

@Injectable({
  providedIn: 'root',
})
export class TestService {
  private baseUrl = '/api/exam/worksheet';

  constructor(
    private http: HttpClient,
    private router: Router
  ) {}

  getWorksheetDetail(worksheetId: number): Observable<WorksheetDetail> {
    return this.http.get<WorksheetDetail>(`${this.baseUrl}/${worksheetId}/detail`);
  }

  createWorksheetFromMistakes(instanceId: number): Observable<WorksheetFromMistakesResult> {
    return this.http.post<WorksheetFromMistakesResult>(`${this.baseUrl}/from-mistakes/${instanceId}`, null);
  }

  /**
   * Başkasının (public) sınavını kendi hesabına kopyalar (issue #16).
   * Kopya `Private` bir worksheet olarak açılır; kopyalayan sahibidir.
   * 401 / 403 / 404 hata döner.
   */
  copyWorksheet(worksheetId: number): Observable<CopyWorksheetResult> {
    return this.http.post<CopyWorksheetResult>(`${this.baseUrl}/${worksheetId}/copy`, null);
  }

  getWorksheetReminder(worksheetId: number): Observable<WorksheetReminder | null> {
    return this.http.get<WorksheetReminder | null>(`${this.baseUrl}/${worksheetId}/reminder`);
  }

  putWorksheetReminder(worksheetId: number, body: WorksheetReminderRequest): Observable<WorksheetReminder> {
    return this.http.put<WorksheetReminder>(`${this.baseUrl}/${worksheetId}/reminder`, body);
  }

  deleteWorksheetReminder(worksheetId: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${worksheetId}/reminder`);
  }

  /**
   * Öğrencinin `[from, to)` aralığındaki takvim etkinlikleri (planlanmış hatırlatmalar
   * + atama teslim tarihleri). `to` hariç (exclusive). from/to `.toISOString()` ile
   * her zaman 'Z' içeren UTC olarak gönderilir; aralık en fazla 180 gün.
   */
  getMyCalendar(from: Date, to: Date): Observable<CalendarEvent[]> {
    const params = new HttpParams().set('from', from.toISOString()).set('to', to.toISOString());
    return this.http
      .get<StudentCalendarResponse>('/api/exam/calendar/me', { params })
      .pipe(map((r) => r.events));
  }

  getTestWithAnswers(testInstanceId: number): Observable<TestInstance> {
    return this.http.get<TestInstance>(`${this.baseUrl}/test-instance/${testInstanceId}`);
  }

  getCanvasTestWithAnswers(testInstanceId: number): Observable<TestInstance> {
    return this.http.get<TestInstance>(`${this.baseUrl}/test-canvas-instance/${testInstanceId}`);
  }

  getCanvasTestResults(testInstanceId: number): Observable<TestInstance> {
    return this.http.get<TestInstance>(`${this.baseUrl}/test-canvas-instance-result/${testInstanceId}`);
  }

  saveAnswer(studentAnswer: StudentAnswer): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/save-answer`, studentAnswer);
  }

  completeTest(testInstanceId: number): Observable<any> {
    return this.http.put<any>(`${this.baseUrl}/end-test/${testInstanceId}`, null);
  }

  startTest(testInstanceId: number): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/start-test/${testInstanceId}`, null);
  }

  loadTest() {
    return this.http.get<Exam[]>(`${this.baseUrl}`);
  }

  search(
    query: string | undefined,
    subjectIds: number[],
    gradeIds: number[],
    pageNumber: number = 1,
    pageSize = 10,
    booktestId = 0
  ): Observable<Paged<Test>> {
    const subjectIdsParam = subjectIds && subjectIds.length > 0 ? '&subjectIds=' + subjectIds.join(`&subjectIds=`) : '';
    const gradeIdParam = gradeIds && gradeIds.length > 0 ? '&gradeIds=' + gradeIds.join(`&gradeIds=`) : '';
    const bookTestIdParam = booktestId ? `&bookTestId=${booktestId}` : '';
    var reqUrl = `${this.baseUrl}/list?search=${query}&pageNumber=${pageNumber}&pageSize=${pageSize}${subjectIdsParam}${gradeIdParam}${bookTestIdParam}`;
    console.log(reqUrl);
    return this.http.get<Paged<Test>>(reqUrl);
  }

  /**
   * Typed `/api/exam/list` çağrısı. Yeni sıralama / durum / aralık parametrelerini destekler.
   * Öğrenci rolünde `statuses` sunucu tarafında uygulanır; öğretmen rolünde yok sayılır.
   */
  listWorksheets(filter: WorksheetListFilter): Observable<Paged<Test>> {
    let params = new HttpParams()
      .set('pageNumber', String(filter.pageNumber ?? 1))
      .set('pageSize', String(filter.pageSize ?? 12));

    if (filter.search) {
      params = params.set('search', filter.search);
    }
    for (const id of filter.subjectIds ?? []) {
      params = params.append('subjectIds', String(id));
    }
    for (const id of filter.gradeIds ?? []) {
      params = params.append('gradeIds', String(id));
    }
    for (const status of filter.statuses ?? []) {
      params = params.append('statuses', String(status));
    }
    for (const id of filter.bookIds ?? []) {
      params = params.append('bookIds', String(id));
    }
    if (filter.bookTestId) {
      params = params.set('bookTestId', String(filter.bookTestId));
    }
    if (filter.minQuestionCount != null) {
      params = params.set('minQuestionCount', String(filter.minQuestionCount));
    }
    if (filter.maxQuestionCount != null) {
      params = params.set('maxQuestionCount', String(filter.maxQuestionCount));
    }
    if (filter.minDurationSeconds != null) {
      params = params.set('minDurationSeconds', String(filter.minDurationSeconds));
    }
    if (filter.maxDurationSeconds != null) {
      params = params.set('maxDurationSeconds', String(filter.maxDurationSeconds));
    }
    if (filter.isPracticeTest != null) {
      params = params.set('isPracticeTest', String(filter.isPracticeTest));
    }
    if (filter.includeShared != null) {
      params = params.set('includeShared', String(filter.includeShared));
    }
    if (filter.sortBy) {
      params = params.set('sortBy', filter.sortBy);
    }
    if (filter.sortDir) {
      params = params.set('sortDir', filter.sortDir);
    }

    return this.http.get<Paged<Test>>(`${this.baseUrl}/list`, { params });
  }

  getLatest(pageNumber: number = 1, pageSize = 10): Observable<Test[]> {
    return this.http.get<Test[]>(`${this.baseUrl}/latest?pageNumber=${pageNumber}&pageSize=${pageSize}`);
  }

  getPopular(pageNumber: number = 1, pageSize = 10, sinceDays = 30): Observable<Test[]> {
    return this.http.get<Test[]>(
      `${this.baseUrl}/popular?pageNumber=${pageNumber}&pageSize=${pageSize}&sinceDays=${sinceDays}`
    );
  }

  getCompleted(pageNumber: number = 1): Observable<Paged<InstanceSummary>> {
    return this.http.get<Paged<InstanceSummary>>(`${this.baseUrl}/CompletedTests?pageNumber=${pageNumber}&pageSize=10`);
  }

  getActiveAssignments(): Observable<AssignedWorksheet[]> {
    return this.http.get<AssignedWorksheet[]>(`${this.baseUrl}/assignments/active`);
  }

  getWorksheetAssignmentsForTeacher(worksheetId: number): Observable<TeacherWorksheetAssignments> {
    return this.http.get<TeacherWorksheetAssignments>(`${this.baseUrl}/${worksheetId}/assignments/overview`);
  }

  assignWorksheet(request: WorksheetAssignmentRequest): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/assignments`, request);
  }

  create(test: Test): Observable<{ message: string; examId: number }> {
    return this.http.post<any>(this.baseUrl, test);
  }

  delete(id: number): Observable<any> {
    return this.http.delete<any>(`${this.baseUrl}/${id}`);
  }

  bulkImport(bulkData: any): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/bulk-import`, bulkData);
  }

  updateWorksheetBackgroundImage(
    worksheetId: number,
    file: File
  ): Observable<{ success: boolean; message: string; imageUrl?: string }> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.put<{ success: boolean; message: string; imageUrl?: string }>(
      `${this.baseUrl}/${worksheetId}/background-image`,
      formData
    );
  }

  /** Öğretmen görünürlük eksenlerini günceller (issue #10). Sahibi/admin dışında 403 döner. */
  updateVisibility(
    worksheetId: number,
    payload: { teacherSharing: WorksheetTeacherSharing; studentVisibility: WorksheetStudentVisibility }
  ): Observable<Test> {
    return this.http.put<Test>(`${this.baseUrl}/${worksheetId}/visibility`, payload);
  }

  get(id: number): Observable<Test> {
    var reqUrl = `${this.baseUrl}/list?id=${id}`;
    return this.http.get<Paged<Test>>(reqUrl).pipe(
      switchMap((response) => {
        return of(response.items[0]);
      })
    );
  }

  convertAnswerToAnswerChoice(answer: Answer): AnswerChoice {
    return {
      id: answer.id,
      label: answer.text,
      x: answer.x,
      y: answer.y,
      width: answer.width,
      height: answer.height,
      imageUrl: answer.imageUrl,
    };
  }

  convertTestInstanceToRegions(testInstance: TestInstance): QuestionRegion[] {
    if (!testInstance || !testInstance.testInstanceQuestions || testInstance.testInstanceQuestions.length === 0) {
      return [];
    }

    const imageSrc = testInstance.testInstanceQuestions[0].question.imageUrl;

    const regions: QuestionRegion[] = testInstance.testInstanceQuestions
      .sort((a, b) => (a.order ?? 0) - (b.order ?? 0))
      .map((qInstance, index) => {
        const question = qInstance.question;
        return {
          id: question.id,
          name: `Soru ${qInstance.order ?? index + 1}`,
          x: question.x,
          y: question.y,
          width: question.width,
          height: question.height,
          sanitizedHeight: question.sanitizedHeight,
          classificationSource: question.classificationSource ?? 0,
          isExample: question.isExample,
          passageId: question.passage ? question.passage.id.toString() : '',
          imageId: question.imageUrl,
          imageUrl: question.imageUrl,
          exampleAnswer: question.isExample ? question.practiceCorrectAnswer : null,
          answers: question.answers.map((answer) => ({
            id: answer.id,
            label: answer.text,
            x: answer.x,
            y: answer.y,
            width: answer.width,
            height: answer.height,
            imageUrl: answer.imageUrl,
            isCorrect: question.correctAnswerId === answer.id, // Set isCorrect based on correctAnswerId
          })),
          passage: question.passage
            ? {
                id: question.passage.id,
                title: question.passage.title,
                x: question.passage.x,
                y: question.passage.y,
                width: question.passage.width,
                height: question.passage.height,
                imageUrl: question.passage.imageUrl,
                imageId: question.passage.imageUrl,
              }
            : undefined,
        };
      });

    return regions;
  }

  studentStatistics(): Observable<StudentStatisticsResponse> {
    return this.http.get<StudentStatisticsResponse>(`${this.baseUrl}/student/statistics`);
  }

  convertQuestionsToRegions(qeuestions: Question[]): QuestionRegion[] {
    if (!qeuestions || qeuestions.length === 0) {
      return [];
    }

    const regions: QuestionRegion[] = qeuestions
      .sort((a, b) => (a.order ?? 0) - (b.order ?? 0))
      .map((question, index) => {
        const subtopicIds = (question.subTopics || []).map((st) => Number(st?.id) || 0).filter((id) => id > 0);
        const firstSubtopicId = subtopicIds.length > 0 ? subtopicIds[0] : 0;
        return {
          id: question.id,
          name: `Soru ${question.order ?? index + 1}`,
          x: question.x,
          y: question.y,
          width: question.width,
          height: question.height,
          sanitizedHeight: question.sanitizedHeight,
          isExample: question.isExample,
          subjectId: question.subjectId || 0,
          topicId: question.topicId || 0,
          difficultyLevel: question.difficultyLevel,
          classificationSource: question.classificationSource ?? 0,
          subtopicId: firstSubtopicId,
          subtopicIds,
          passageId: question.passage ? question.passage.id.toString() : '',
          imageId: question.imageUrl,
          imageUrl: question.imageUrl,
          exampleAnswer: question.isExample ? question.practiceCorrectAnswer : null,
          answers: question.answers.map((answer) => ({
            id: answer.id,
            label: answer.text,
            x: answer.x,
            y: answer.y,
            width: answer.width,
            height: answer.height,
            imageUrl: answer.imageUrl,
            isCorrect: question.correctAnswerId === answer.id, // Set isCorrect based on correctAnswerId
          })),
          passage: question.passage
            ? {
                id: question.passage.id,
                title: question.passage.title,
                x: question.passage.x,
                y: question.passage.y,
                width: question.passage.width,
                height: question.passage.height,
                imageUrl: question.passage.imageUrl,
                imageId: question.passage.imageUrl,
              }
            : undefined,
        };
      });

    return regions;
  }
}
