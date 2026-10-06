import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router } from '@angular/router';

import { TestService } from './test.service';
import { Question } from '../models/question';
import { TestInstance } from '../models/test-instance';

describe('TestService', () => {
  let service: TestService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        TestService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Router, useValue: jasmine.createSpyObj<Router>('Router', ['navigate']) },
      ],
    });

    service = TestBed.inject(TestService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  describe('copyWorksheet', () => {
    it('copyWorksheet_Called_PostsToCopyEndpointWithNullBody', () => {
      service.copyWorksheet(42).subscribe();

      const req = httpMock.expectOne('/api/exam/worksheet/42/copy');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toBeNull();
      req.flush({ worksheetId: 99 });
    });

    it('copyWorksheet_ServerReturnsNewWorksheetId_PassesResponseThrough', (done) => {
      service.copyWorksheet(7).subscribe((res) => {
        expect(res).toEqual({ worksheetId: 123 } as any);
        done();
      });

      httpMock.expectOne('/api/exam/worksheet/7/copy').flush({ worksheetId: 123 });
    });

    it('copyWorksheet_ServerErrors_PropagatesError', (done) => {
      service.copyWorksheet(5).subscribe({
        next: () => done.fail('should not succeed'),
        error: (err) => {
          expect(err.status).toBe(403);
          done();
        },
      });

      httpMock
        .expectOne('/api/exam/worksheet/5/copy')
        .flush({ message: 'yetki yok' }, { status: 403, statusText: 'Forbidden' });
    });
  });

  describe('region mapping with signed image URLs (issue #365 S2)', () => {
    const signed = '/img/exam-questions/questions/q1/question.jpg?X-Amz-Signature=v1sig';
    const signedV2 = '/img/exam-questions/questions/q1/question-v2.jpg?X-Amz-Signature=v2sig';
    const signedPassage = '/img/exam-questions/passages/p1.jpg?X-Amz-Signature=psig';

    function buildQuestion(overrides: Partial<Question> = {}): Question {
      return {
        id: 1,
        text: '',
        imageUrl: signed,
        imageUrlV2: signedV2,
        category: {} as Question['category'],
        answers: [],
        isExample: false,
        subjectId: 0,
        topicId: 0,
        answerColCount: 2,
        x: 0,
        y: 0,
        width: 100,
        height: 100,
        isCanvasQuestion: true,
        passage: { id: 9, title: '', text: '', imageUrl: signedPassage, x: 0, y: 0, width: 1, height: 1, isCanvasQuestion: true },
        ...overrides,
      };
    }

    function buildInstance(question: Question): TestInstance {
      return { testInstanceQuestions: [{ id: 1, order: 1, question }] } as unknown as TestInstance;
    }

    it('convertTestInstanceToRegions_SignedUrls_PassesV2VerbatimAndUsesUnsignedPathsAsImageIds', () => {
      const [region] = service.convertTestInstanceToRegions(buildInstance(buildQuestion()));

      expect(region.imageUrl).toBe(signed);
      expect(region.imageUrlV2).toBe(signedV2);
      expect(region.imageId).toBe('/img/exam-questions/questions/q1/question.jpg');
      expect(region.passage?.imageUrl).toBe(signedPassage);
      expect(region.passage?.imageId).toBe('/img/exam-questions/passages/p1.jpg');
    });

    it('convertTestInstanceToRegions_NoV2FromServer_MapsV2AsNull', () => {
      const [region] = service.convertTestInstanceToRegions(buildInstance(buildQuestion({ imageUrlV2: null })));

      expect(region.imageUrlV2).toBeNull();
      expect(region.imageUrl).toBe(signed);
    });

    it('convertQuestionsToRegions_SignedUrls_PassesV2VerbatimAndUsesUnsignedPathAsImageId', () => {
      const [region] = service.convertQuestionsToRegions([buildQuestion()]);

      expect(region.imageUrl).toBe(signed);
      expect(region.imageUrlV2).toBe(signedV2);
      expect(region.imageId).toBe('/img/exam-questions/questions/q1/question.jpg');
      expect(region.passage?.imageId).toBe('/img/exam-questions/passages/p1.jpg');
    });
  });
});
