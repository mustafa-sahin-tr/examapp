import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { QuestionService } from './question.service';

describe('QuestionService', () => {
    let service: QuestionService;
    let http: HttpTestingController;

    beforeEach(() => {
        TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr()), provideHttpClientTesting()] });
        service = TestBed.inject(QuestionService);
        http = TestBed.inject(HttpTestingController);
    });

    afterEach(() => http.verify());

    it('linkBankQuestions_PostsQuestionIdsToTestBankQuestionsEndpoint (issue #472)', () => {
        let result: unknown;
        service.linkBankQuestions(12, [5, 9]).subscribe((r) => (result = r));

        const req = http.expectOne('/api/exam/questions/test/12/bank-questions');
        expect(req.request.method).toBe('POST');
        expect(req.request.body).toEqual({ questionIds: [5, 9] });
        req.flush({ addedQuestionIds: [5], alreadyInTestQuestionIds: [9] });

        expect(result).toEqual({ addedQuestionIds: [5], alreadyInTestQuestionIds: [9] });
    });
});
