import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MockExamRanking } from '../models/target-goal.model';

export interface ExamTemplateSection {
  id?: number;
  groupName?: string | null;
  name: string;
  questionCount: number;
}

export interface ExamTemplateSession {
  id?: number;
  name: string;
  durationSeconds: number;
  sections: ExamTemplateSection[];
}

/** Issue #693: sınav kuralı veridir. `wrongAnswersPerCorrect`: kaç yanlış bir doğruyu götürür, 0 ceza yok. */
export interface ExamTemplate {
  id?: number;
  code: string;
  name: string;
  wrongAnswersPerCorrect: number;
  sourceReference?: string | null;
  sessions: ExamTemplateSession[];
  inUse?: boolean;
  /** Bu şablondan kurulmuş deneme sayısı (#899). */
  mockExamCount?: number;
  /** #917: resmî şablon (LGS, YKS) — değiştirilemez, kopyalanarak düzenlenir. */
  isOfficial?: boolean;
}

export interface MockExamListItem {
  id: number;
  name: string;
  templateName: string;
  gradeName: string;
  publishedAt: string | null;
  isFreeTrial?: boolean;
}

export interface MockExamQuestionCard {
  id: number;
  text: string | null;
  imageUrl: string | null;
}

export interface MockExamSection {
  sectionId: number;
  groupName: string | null;
  name: string;
  requiredCount: number;
  questionIds: number[];
  questions: MockExamQuestionCard[];
}

export interface MockExamSession {
  id: number;
  name: string;
  durationSeconds: number;
  worksheetId: number;
  sections: MockExamSection[];
}

export interface MockExamProblem {
  code: 'WrongCount' | 'Unpublished' | 'LicenseEnded' | 'NoCorrectAnswer' | 'ExampleQuestion' | 'Unmapped' | string;
  section: string;
  questionId?: number | null;
  required?: number | null;
  actual?: number | null;
}

export interface MockExamDetail {
  id: number;
  name: string;
  examTemplateId: number;
  templateName: string;
  gradeId: number;
  gradeName: string;
  publishedAt: string | null;
  openToGrade?: boolean;
  /** Issue #1031: sınıfın Free öğrencilerine açık deneme. */
  isFreeTrial?: boolean;
  sessions: MockExamSession[];
  problems: MockExamProblem[];
}

export interface StudentMockExamSession {
  name: string;
  worksheetId: number;
  durationSeconds: number;
  questionCount: number;
  status: 'NotStarted' | 'InProgress' | 'Finished';
  instanceId: number | null;
  /** Atama yalnız bir oturumu kapsıyorsa diğerleri başlatılamaz. */
  canStart: boolean;
}

export interface StudentMockExam {
  id: number;
  name: string;
  templateName: string;
  isFreeTrial?: boolean;
  sessions: StudentMockExamSession[];
}

export interface MockExamScore {
  groupName: string | null;
  name: string;
  questionCount: number;
  correct: number;
  wrong: number;
  blank: number;
  net: number;
  pending: boolean;
}

export interface MockExamResult {
  id: number;
  name: string;
  templateName: string;
  wrongAnswersPerCorrect: number;
  complete: boolean;
  sections: MockExamScore[];
  correct: number;
  wrong: number;
  blank: number;
  net: number;
  /** #1094: sonuç tamamsa denemeyi bitirenler arasında yer (şu ana kadar). */
  ranking?: MockExamRanking | null;
}

@Injectable({ providedIn: 'root' })
export class MockExamService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = '/api/exam/mock-exams';

  listTemplates(): Observable<ExamTemplate[]> {
    return this.http.get<ExamTemplate[]>(`${this.apiUrl}/templates`);
  }

  saveTemplate(template: ExamTemplate): Observable<ExamTemplate> {
    return template.id
      ? this.http.put<ExamTemplate>(`${this.apiUrl}/templates/${template.id}`, template)
      : this.http.post<ExamTemplate>(`${this.apiUrl}/templates`, template);
  }

  listMine(): Observable<MockExamListItem[]> {
    return this.http.get<MockExamListItem[]>(this.apiUrl);
  }

  create(examTemplateId: number, name: string, gradeId: number): Observable<MockExamDetail> {
    return this.http.post<MockExamDetail>(this.apiUrl, { examTemplateId, name, gradeId });
  }

  get(id: number): Observable<MockExamDetail> {
    return this.http.get<MockExamDetail>(`${this.apiUrl}/${id}`);
  }

  setSectionQuestions(id: number, sectionId: number, questionIds: number[]): Observable<MockExamDetail> {
    return this.http.put<MockExamDetail>(`${this.apiUrl}/${id}/sections/${sectionId}/questions`, { questionIds });
  }

  publish(id: number, openToGrade: boolean): Observable<MockExamDetail> {
    return this.http.post<MockExamDetail>(`${this.apiUrl}/${id}/publish`, { openToGrade });
  }

  setFreeTrial(id: number, isFreeTrial: boolean): Observable<MockExamDetail> {
    return this.http.put<MockExamDetail>(`${this.apiUrl}/${id}/free-trial`, { isFreeTrial });
  }

  deleteDraft(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }

  listForStudent(): Observable<StudentMockExam[]> {
    return this.http.get<StudentMockExam[]>(`${this.apiUrl}/mine`);
  }

  getResult(id: number): Observable<MockExamResult> {
    return this.http.get<MockExamResult>(`${this.apiUrl}/${id}/result`);
  }
}
