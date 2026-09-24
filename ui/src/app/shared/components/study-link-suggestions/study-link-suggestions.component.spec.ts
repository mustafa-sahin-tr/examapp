import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Subject, of, throwError } from 'rxjs';

import { StudyLinkSuggestionsComponent } from './study-link-suggestions.component';
import { StudyLinkService } from '../../../services/study-link.service';
import { QuestionStudyLinkSuggestion } from '../../../models/study-link';
import { translocoTestingModule } from '../../testing/transloco-testing';
import studyLinksTr from '../../../../../public/i18n/study-links/tr.json';

const suggestions: QuestionStudyLinkSuggestion[] = [
  {
    questionId: 100,
    testInstanceQuestionId: 1000,
    groups: [
      {
        kind: 'SubTopic',
        subTopicId: 11,
        topicId: 1,
        name: 'Kesirler',
        links: [
          { id: 1, title: 'Kesir videosu', url: 'https://youtu.be/a', sourceType: 'YouTube' },
          { id: 2, title: 'Kesir makalesi', url: 'https://example.com/k', sourceType: 'Other' },
        ],
      },
      {
        kind: 'SubTopic',
        subTopicId: 12,
        topicId: 1,
        name: 'Ondalık Sayılar',
        links: [{ id: 3, title: 'Ondalık', url: 'https://example.com/o', sourceType: 'Other' }],
      },
      { kind: 'Topic', subTopicId: null, topicId: 1, name: 'Sayılar', links: [{ id: 4, title: 'Genel', url: 'https://example.com/g', sourceType: 'Other' }] },
      { kind: 'SubTopic', subTopicId: 13, topicId: 1, name: 'Boş grup', links: [] },
    ],
  },
  { questionId: 200, testInstanceQuestionId: 2000, groups: [] },
];

describe('StudyLinkSuggestionsComponent (issue #61)', () => {
  const texts = studyLinksTr.suggestions;
  let fixture: ComponentFixture<StudyLinkSuggestionsComponent>;
  let service: jasmine.SpyObj<StudyLinkService>;

  function configure(inputs: Record<string, unknown>, response: ReturnType<StudyLinkService['getForResult']> = of(suggestions)) {
    service = jasmine.createSpyObj<StudyLinkService>('StudyLinkService', ['getForResult']);
    service.getForResult.and.returnValue(response);
    TestBed.configureTestingModule({
      imports: [StudyLinkSuggestionsComponent, translocoTestingModule({ langs: { 'study-links/tr': studyLinksTr } })],
      providers: [{ provide: StudyLinkService, useValue: service }],
    });
    fixture = TestBed.createComponent(StudyLinkSuggestionsComponent);
    setInputs(inputs);
  }

  function setInputs(inputs: Record<string, unknown>): void {
    Object.entries(inputs).forEach(([k, v]) => fixture.componentRef.setInput(k, v));
    fixture.detectChanges();
    fixture.detectChanges();
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  it('wrongQuestion_RendersGroupsWithTitlesAndSkipsEmptyGroups', () => {
    configure({ testInstanceId: 55, questionId: 100, testInstanceQuestionId: 1000, answeredWrong: true });

    expect(service.getForResult).toHaveBeenCalledOnceWith(55);
    expect(el().textContent).toContain(texts.title);
    const groups = Array.from(el().querySelectorAll('[data-testid="suggestion-group"] h5')).map((h) => h.textContent?.trim());
    expect(groups).toEqual(['Kesirler', 'Ondalık Sayılar', 'Sayılar (genel)']);
    expect(el().querySelectorAll('[data-testid="suggestion-link"]').length).toBe(4);
  });

  it('links_OpenInNewTabWithNoopener', () => {
    configure({ testInstanceId: 55, questionId: 100, answeredWrong: true });

    const anchors = Array.from(el().querySelectorAll<HTMLAnchorElement>('a[data-testid="suggestion-link"]'));
    expect(anchors.length).toBeGreaterThan(0);
    anchors.forEach((a) => {
      expect(a.getAttribute('target')).toBe('_blank');
      expect(a.getAttribute('rel')).toBe('noopener noreferrer');
    });
    expect(anchors[0].getAttribute('href')).toBe('https://youtu.be/a');
    expect(anchors[0].textContent).toContain('Kesir videosu');
    expect(anchors[0].textContent).toContain('YouTube');
  });

  it('correctQuestion_RendersNothing', () => {
    configure({ testInstanceId: 55, questionId: 100, answeredWrong: false });
    expect(el().querySelector('[data-testid="study-suggestions"]')).toBeNull();
    expect(el().textContent?.trim()).toBe('');
  });

  it('wrongQuestionWithoutLinks_RendersNothing', () => {
    configure({ testInstanceId: 55, questionId: 200, answeredWrong: true });
    expect(el().querySelector('[data-testid="study-suggestions"]')).toBeNull();
    expect(el().textContent?.trim()).toBe('');
  });

  it('emptyResponse_RendersNothing', () => {
    configure({ testInstanceId: 55, questionId: 100, answeredWrong: true }, of([]));
    expect(el().textContent?.trim()).toBe('');
  });

  it('matchesByTestInstanceQuestionIdFirst', () => {
    // questionId 200'ün grubu yok; tiq 1000 eşleşmesi öncelikli.
    configure({ testInstanceId: 55, questionId: 200, testInstanceQuestionId: 1000, answeredWrong: true });
    expect(el().querySelector('[data-testid="study-suggestions"]')).toBeTruthy();
  });

  it('navigatingBetweenQuestions_LoadsOnlyOncePerInstance', () => {
    configure({ testInstanceId: 55, questionId: 100, answeredWrong: true });
    setInputs({ questionId: 200, answeredWrong: false });
    expect(el().querySelector('[data-testid="study-suggestions"]')).toBeNull();
    setInputs({ questionId: 100, answeredWrong: true });
    expect(el().querySelector('[data-testid="study-suggestions"]')).toBeTruthy();
    expect(service.getForResult).toHaveBeenCalledTimes(1);
  });

  it('loading_ShowsSkeletonOnlyForWrongQuestion', () => {
    const pending = new Subject<QuestionStudyLinkSuggestion[]>();
    configure({ testInstanceId: 55, questionId: 100, answeredWrong: true }, pending.asObservable());
    expect(el().querySelector('[data-testid="suggestions-loading"]')).toBeTruthy();

    setInputs({ answeredWrong: false });
    expect(el().querySelector('[data-testid="suggestions-loading"]')).toBeNull();

    setInputs({ answeredWrong: true });
    pending.next(suggestions);
    pending.complete();
    fixture.detectChanges();
    expect(el().querySelector('[data-testid="suggestions-loading"]')).toBeNull();
    expect(el().querySelector('[data-testid="study-suggestions"]')).toBeTruthy();
  });

  it('error_ShowsSmallNonBlockingNote', () => {
    configure(
      { testInstanceId: 55, questionId: 100, answeredWrong: true },
      throwError(() => new HttpErrorResponse({ status: 500 }))
    );
    expect(el().querySelector('[data-testid="suggestions-error"]')?.textContent).toContain(texts.loadFailed);
    expect(el().querySelector('[data-testid="study-suggestions"]')).toBeNull();
  });

  it('noInstanceId_SendsNoRequest', () => {
    configure({ testInstanceId: null, questionId: 100, answeredWrong: true });
    expect(service.getForResult).not.toHaveBeenCalled();
    expect(el().textContent?.trim()).toBe('');
  });
});
