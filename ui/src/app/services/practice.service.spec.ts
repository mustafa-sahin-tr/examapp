import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { PracticeService } from './practice.service';
import { Question } from '../models/question';
import { ClassificationSource } from '../models/draws';

describe('PracticeService', () => {
  let service: PracticeService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [PracticeService, provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(PracticeService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  describe('startSession', () => {
    it('startSession_Called_PostsRequestToSessionsEndpoint', () => {
      const request = { subjectIds: [1], topicIds: [] };
      service.startSession(request).subscribe();

      const req = httpMock.expectOne('/api/exam/practice/sessions');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      req.flush({});
    });
  });

  describe('getSession', () => {
    it('getSession_Called_GetsSessionByIdEndpoint', () => {
      service.getSession(42).subscribe();

      const req = httpMock.expectOne('/api/exam/practice/sessions/42');
      expect(req.request.method).toBe('GET');
      req.flush({});
    });
  });

  describe('listSessions', () => {
    it('listSessions_Called_GetsSessionsEndpointWithPageParams', () => {
      service.listSessions(2, 10).subscribe();

      const req = httpMock.expectOne((r) => r.url === '/api/exam/practice/sessions');
      expect(req.request.method).toBe('GET');
      expect(req.request.params.get('page')).toBe('2');
      expect(req.request.params.get('pageSize')).toBe('10');
      req.flush({ pageNumber: 2, pageSize: 10, totalCount: 0, items: [] });
    });

    it('listSessions_NoArgs_DefaultsToFirstPageOfTwenty', () => {
      service.listSessions().subscribe();

      const req = httpMock.expectOne((r) => r.url === '/api/exam/practice/sessions');
      expect(req.request.params.get('page')).toBe('1');
      expect(req.request.params.get('pageSize')).toBe('20');
      req.flush({ pageNumber: 1, pageSize: 20, totalCount: 0, items: [] });
    });
  });

  describe('getSessionReview', () => {
    it('getSessionReview_Called_GetsReviewEndpointForSession', () => {
      service.getSessionReview(12).subscribe();

      const req = httpMock.expectOne('/api/exam/practice/sessions/12/review');
      expect(req.request.method).toBe('GET');
      req.flush({ session: {}, questions: [] });
    });
  });

  describe('getNextQuestion', () => {
    it('getNextQuestion_Called_GetsNextEndpointForSession', () => {
      service.getNextQuestion(7).subscribe();

      const req = httpMock.expectOne('/api/exam/practice/sessions/7/next');
      expect(req.request.method).toBe('GET');
      req.flush({});
    });
  });

  describe('submitAnswer', () => {
    it('submitAnswer_Called_PostsAnswerBodyToAnswerEndpoint', () => {
      const request = { questionId: 5, selectedAnswerId: 9, skipped: false, timeTaken: 12 };
      service.submitAnswer(7, request).subscribe();

      const req = httpMock.expectOne('/api/exam/practice/sessions/7/answer');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      req.flush({});
    });
  });

  describe('endSession', () => {
    it('endSession_Called_PutsNullBodyToEndEndpoint', () => {
      service.endSession(7).subscribe();

      const req = httpMock.expectOne('/api/exam/practice/sessions/7/end');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toBeNull();
      req.flush({});
    });
  });

  describe('toQuestionRegion', () => {
    function buildQuestion(overrides: Partial<Question> = {}): Question {
      return {
        id: 10,
        text: 'soru metni',
        imageUrl: 'https://example.test/question.jpg',
        category: {} as any,
        answers: [
          { id: 100, index: 0, text: 'A şıkkı', imageUrl: 'a.jpg', x: 1, y: 2, width: 3, height: 4, isCanvasQuestion: true, tag: 'A', order: 0 },
          { id: 101, index: 1, text: 'B şıkkı', imageUrl: 'b.jpg', x: 5, y: 6, width: 7, height: 8, isCanvasQuestion: true, tag: 'B', order: 1 },
        ],
        isExample: false,
        subjectId: 3,
        topicId: 4,
        answerColCount: 2,
        x: 10,
        y: 20,
        width: 300,
        height: 400,
        isCanvasQuestion: true,
        ...overrides,
      };
    }

    it('toQuestionRegion_WithoutCorrectAnswerId_MapsBaseFieldsAndAllAnswersUncorrect', () => {
      const question = buildQuestion({ sanitizedHeight: 380, classificationSource: ClassificationSource.AI, difficultyLevel: 2, showPassageFirst: false });

      const region = service.toQuestionRegion(question);

      expect(region.id).toBe(10);
      expect(region.name).toBe('Soru');
      expect(region.x).toBe(10);
      expect(region.y).toBe(20);
      expect(region.width).toBe(300);
      expect(region.height).toBe(400);
      expect(region.sanitizedHeight).toBe(380);
      expect(region.classificationSource).toBe(ClassificationSource.AI);
      expect(region.isExample).toBeFalse();
      expect(region.subjectId).toBe(3);
      expect(region.topicId).toBe(4);
      expect(region.difficultyLevel).toBe(2);
      expect(region.showPassageFirst).toBeFalse();
      expect(region.passageId).toBe('');
      expect(region.imageId).toBe(question.imageUrl);
      expect(region.imageUrl).toBe(question.imageUrl);
      expect(region.passage).toBeUndefined();
      expect(region.answers.length).toBe(2);
      expect(region.answers.every((a) => a.isCorrect === false)).toBeTrue();
    });

    it('toQuestionRegion_WithCorrectAnswerId_MarksMatchingAnswerAsCorrect', () => {
      const question = buildQuestion();

      const region = service.toQuestionRegion(question, 101);

      const correct = region.answers.find((a) => a.id === 101);
      const incorrect = region.answers.find((a) => a.id === 100);
      expect(correct?.isCorrect).toBeTrue();
      expect(incorrect?.isCorrect).toBeFalse();
    });

    it('toQuestionRegion_MissingClassificationSourceAndSubjectTopicIds_DefaultsToZero', () => {
      const question = buildQuestion({ classificationSource: undefined, subjectId: 0, topicId: 0 });

      const region = service.toQuestionRegion(question);

      expect(region.classificationSource).toBe(0);
      expect(region.subjectId).toBe(0);
      expect(region.topicId).toBe(0);
    });

    it('toQuestionRegion_IsExampleTrue_UsesPracticeCorrectAnswerAsExampleAnswer', () => {
      const question = buildQuestion({ isExample: true, practiceCorrectAnswer: 'C' });

      const region = service.toQuestionRegion(question);

      expect(region.exampleAnswer).toBe('C');
    });

    it('toQuestionRegion_IsExampleFalse_ExampleAnswerIsNull', () => {
      const question = buildQuestion({ isExample: false, practiceCorrectAnswer: 'C' });

      const region = service.toQuestionRegion(question);

      expect(region.exampleAnswer).toBeNull();
    });

    it('toQuestionRegion_WithPassage_MapsPassageFieldsAndPassageIdAsString', () => {
      const question = buildQuestion({
        passage: {
          id: 55,
          title: 'okuma parçası',
          text: '...',
          imageUrl: 'passage.jpg',
          x: 1,
          y: 2,
          width: 500,
          height: 600,
          isCanvasQuestion: true,
        },
      });

      const region = service.toQuestionRegion(question);

      expect(region.passageId).toBe('55');
      expect(region.passage).toEqual({
        id: 55,
        title: 'okuma parçası',
        x: 1,
        y: 2,
        width: 500,
        height: 600,
        imageUrl: 'passage.jpg',
        imageId: 'passage.jpg',
      });
    });

    it('toQuestionRegion_WithoutAnswers_ReturnsEmptyAnswersArray', () => {
      const question = buildQuestion({ answers: undefined as any });

      const region = service.toQuestionRegion(question);

      expect(region.answers).toEqual([]);
    });
  });
});
