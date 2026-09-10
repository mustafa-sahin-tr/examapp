import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { StudyPageService } from './study-page.service';
import {
  StudyPageContentType,
  StudyPageLinkPlatform,
  StudyPageUpdateRequest,
  StudyPageWriteRequest,
} from '../models/study-page';

describe('StudyPageService', () => {
  let service: StudyPageService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [StudyPageService, provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(StudyPageService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function baseRequest(overrides: Partial<StudyPageWriteRequest> = {}): StudyPageWriteRequest {
    return {
      title: 'Baslik',
      description: 'Aciklama',
      gradeId: null,
      subjectId: null,
      topicId: null,
      subTopicId: null,
      isPublished: true,
      contentType: StudyPageContentType.Image,
      ...overrides,
    };
  }

  describe('create', () => {
    it('create_Called_PostsToStudyItemsEndpoint', () => {
      service.create(baseRequest(), []).subscribe();

      const req = httpMock.expectOne('/api/exam/study-items');
      expect(req.request.method).toBe('POST');
      req.flush({});
    });

    it('create_LinkType_FormDataContainsUrlAndPlatformOnly', () => {
      const request = baseRequest({
        contentType: StudyPageContentType.Link,
        url: 'https://www.eba.gov.tr/ders  ',
        platform: StudyPageLinkPlatform.Eba,
        bookId: 5,
        startPage: 3,
      });

      service.create(request, []).subscribe();

      const req = httpMock.expectOne('/api/exam/study-items');
      const formData = req.request.body as FormData;

      expect(formData.get('ContentType')).toBe(String(StudyPageContentType.Link));
      expect(formData.get('Url')).toBe('https://www.eba.gov.tr/ders');
      expect(formData.get('Platform')).toBe(String(StudyPageLinkPlatform.Eba));
      // Diger tiplere ait alanlar Link secildiginde gonderilmemeli
      expect(formData.get('BookId')).toBeNull();
      expect(formData.get('StartPage')).toBeNull();

      req.flush({});
    });

    it('create_BookPageRangeTypeWithExistingBook_FormDataContainsBookIdsAndPageRange', () => {
      const request = baseRequest({
        contentType: StudyPageContentType.BookPageRange,
        bookId: 10,
        bookTestId: 20,
        startPage: 5,
        endPage: 12,
        url: 'https://should-not-be-sent.example.com',
      });

      service.create(request, []).subscribe();

      const req = httpMock.expectOne('/api/exam/study-items');
      const formData = req.request.body as FormData;

      expect(formData.get('ContentType')).toBe(String(StudyPageContentType.BookPageRange));
      expect(formData.get('BookId')).toBe('10');
      expect(formData.get('BookTestId')).toBe('20');
      expect(formData.get('StartPage')).toBe('5');
      expect(formData.get('EndPage')).toBe('12');
      expect(formData.get('NewBookName')).toBeNull();
      expect(formData.get('NewBookTestName')).toBeNull();
      // Link alani Image/BookPageRange durumunda gonderilmemeli
      expect(formData.get('Url')).toBeNull();

      req.flush({});
    });

    it('create_BookPageRangeTypeWithInlineNewBook_FormDataContainsNewBookAndTestNames', () => {
      const request = baseRequest({
        contentType: StudyPageContentType.BookPageRange,
        bookId: null,
        bookTestId: null,
        newBookName: 'Yeni Kitap',
        newBookTestName: 'Yeni Test',
        startPage: 1,
        endPage: 4,
      });

      service.create(request, []).subscribe();

      const req = httpMock.expectOne('/api/exam/study-items');
      const formData = req.request.body as FormData;

      expect(formData.get('NewBookName')).toBe('Yeni Kitap');
      expect(formData.get('NewBookTestName')).toBe('Yeni Test');
      expect(formData.get('BookId')).toBeNull();
      expect(formData.get('BookTestId')).toBeNull();

      req.flush({});
    });

    it('create_ImageTypeWithMinioImages_FormDataContainsSerializedMinioImagesAndFiles', () => {
      const file = new File(['content'], 'page1.webp', { type: 'image/webp' });
      const request = baseRequest({
        contentType: StudyPageContentType.Image,
        minioImages: [{ bookName: 'kitap', pageNumber: 1, minioUrl: '/img/x.webp' }],
      });

      service.create(request, [file]).subscribe();

      const req = httpMock.expectOne('/api/exam/study-items');
      const formData = req.request.body as FormData;

      expect(formData.get('MinioImages')).toBe(
        JSON.stringify([{ bookName: 'kitap', pageNumber: 1, minioUrl: '/img/x.webp' }])
      );
      expect(formData.getAll('images').length).toBe(1);
      // Image tipinde Link/BookPageRange alanlari gonderilmemeli
      expect(formData.get('Url')).toBeNull();
      expect(formData.get('BookId')).toBeNull();

      req.flush({});
    });
  });

  describe('update', () => {
    it('update_Called_PutsToStudyItemsIdEndpointWithRemovedImageIds', () => {
      const request: StudyPageUpdateRequest = {
        ...baseRequest(),
        removedImageIds: [1, 2],
      };

      service.update(7, request, []).subscribe();

      const req = httpMock.expectOne('/api/exam/study-items/7');
      expect(req.request.method).toBe('PUT');
      const formData = req.request.body as FormData;
      expect(formData.getAll('RemovedImageIds')).toEqual(['1', '2']);

      req.flush({});
    });
  });

  describe('getPaged', () => {
    it('getPaged_WithContentTypeFilter_IncludesContentTypeQueryParam', () => {
      service
        .getPaged({ contentType: StudyPageContentType.Link, pageNumber: 2, pageSize: 5 })
        .subscribe();

      const req = httpMock.expectOne((r) => r.url.startsWith('/api/exam/study-items'));
      expect(req.request.urlWithParams).toContain('contentType=1');
      expect(req.request.urlWithParams).toContain('pageNumber=2');
      expect(req.request.urlWithParams).toContain('pageSize=5');
      req.flush({ items: [], totalCount: 0, pageNumber: 2, pageSize: 5 });
    });
  });
});
