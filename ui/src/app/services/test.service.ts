import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, of, switchMap, tap } from 'rxjs';
import { CheckStudentResponse } from '../models/check-student-response';
import { Router } from '@angular/router';
import { Subject } from '../models/subject';
import { Topic } from '../models/topic';
import { SubTopic } from '../models/subtopic';
import { InstanceSummary, Exam, Paged, Test, TestInstance } from '../models/test-instance';
import {
  ApiResponse,
  AssignedWorksheet,
  TeacherWorksheetAssignments,
  WorksheetAssignmentRequest,
} from '../models/assignment';
import { StudentAnswer } from '../models/student-answer';
import { AnswerChoice, CanvasLayoutPlan, CanvasLayoutPlanOverrides, QuestionRegion } from '../models/draws';
import { Answer } from '../models/answer';
import { StudentStatisticsResponse } from '../models/statistics';
import { Question } from '../models/question';

@Injectable({
  providedIn: 'root',
})
export class TestService {
  private baseUrl = '/api/exam/worksheet';

  constructor(
    private http: HttpClient,
    private router: Router
  ) {}

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

  getLatest(pageNumber: number = 1, pageSize = 10): Observable<Test[]> {
    return this.http.get<Test[]>(`${this.baseUrl}/latest?pageNumber=${pageNumber}&pageSize=${pageSize}`);
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
          isExample: question.isExample,
          passageId: question.passage ? question.passage.id.toString() : '',
          imageId: question.imageUrl,
          imageUrl: question.imageUrl,
          exampleAnswer: question.isExample ? question.practiceCorrectAnswer : null,
          layoutPlan: this.parseLayoutPlan(question.layoutPlan),
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
          isExample: question.isExample,
          subjectId: question.subjectId || 0,
          topicId: question.topicId || 0,
          subtopicId: firstSubtopicId,
          subtopicIds,
          passageId: question.passage ? question.passage.id.toString() : '',
          imageId: question.imageUrl,
          imageUrl: question.imageUrl,
          exampleAnswer: question.isExample ? question.practiceCorrectAnswer : null,
          layoutPlan: this.parseLayoutPlan(question.layoutPlan),
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

  private parseLayoutPlan(layoutPlan?: string | null): CanvasLayoutPlan | undefined {
    if (!layoutPlan || typeof layoutPlan !== 'string') {
      return undefined;
    }

    try {
      const parsed = JSON.parse(layoutPlan) as Partial<CanvasLayoutPlan>;
      if (!parsed || typeof parsed.layoutClass !== 'string') {
        return undefined;
      }

      const answerColumns = Math.max(1, Number(parsed.answerColumns) || 0);
      const overrides = this.normalizeLayoutOverrides(parsed.overrides);
      return {
        layoutClass: parsed.layoutClass,
        answerColumns,
        hasPassage: typeof parsed.hasPassage === 'boolean' ? parsed.hasPassage : undefined,
        overrides,
        questionFlex: typeof parsed.questionFlex === 'number' ? Math.floor(parsed.questionFlex) : undefined,
        answersFlex: typeof parsed.answersFlex === 'number' ? parsed.answersFlex : undefined,
      };
    } catch {
      return undefined;
    }
  }

  private normalizeLayoutOverrides(raw: unknown): CanvasLayoutPlanOverrides | undefined {
    if (!raw || typeof raw !== 'object') {
      return undefined;
    }

    const source = raw as Record<string, unknown>;
    const overrides: CanvasLayoutPlanOverrides = {};

    const initialScale = Number(source['initialScale']);
    if (Number.isFinite(initialScale) && initialScale > 0) {
      overrides.initialScale = initialScale;
    }

    const minScale = Number(source['minScale']);
    if (Number.isFinite(minScale) && minScale > 0) {
      overrides.minScale = minScale;
    }

    const maxScale = Number(source['maxScale']);
    if (Number.isFinite(maxScale) && maxScale > 0) {
      overrides.maxScale = maxScale;
    }

    const answerScale = Number(source['answerScale']);
    if (Number.isFinite(answerScale) && answerScale > 0) {
      overrides.answerScale = answerScale;
    }

    if (source['question'] && typeof source['question'] === 'object') {
      const questionRaw = source['question'] as Record<string, unknown>;
      const question: NonNullable<CanvasLayoutPlanOverrides['question']> = {};
      const maxHeight = Number(questionRaw['maxHeight']);
      if (Number.isFinite(maxHeight) && maxHeight > 0) {
        question.maxHeight = maxHeight;
      }
      const maxWidth = Number(questionRaw['maxWidth']);
      if (Number.isFinite(maxWidth) && maxWidth > 0) {
        question.maxWidth = maxWidth;
      }

      if (Object.keys(question).length > 0) {
        overrides.question = question;
      }
    }

    if (source['answers'] && typeof source['answers'] === 'object') {
      const answersRaw = source['answers'] as Record<string, unknown>;
      const answers: NonNullable<CanvasLayoutPlanOverrides['answers']> = {};
      const maxHeight = Number(answersRaw['maxHeight']);
      if (Number.isFinite(maxHeight) && maxHeight > 0) {
        answers.maxHeight = maxHeight;
      }
      const maxWidth = Number(answersRaw['maxWidth']);
      if (Number.isFinite(maxWidth) && maxWidth > 0) {
        answers.maxWidth = maxWidth;
      }

      if (Object.keys(answers).length > 0) {
        overrides.answers = answers;
      }
    }

    return Object.keys(overrides).length > 0 ? overrides : undefined;
  }
}
