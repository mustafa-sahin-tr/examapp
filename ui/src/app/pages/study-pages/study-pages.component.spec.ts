import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of } from 'rxjs';

import { StudyPagesComponent } from './study-pages.component';
import { StudyPageService } from '../../services/study-page.service';
import { SubjectService } from '../../services/subject.service';
import { StudyPage, StudyPageContentType, StudyPageLinkPlatform } from '../../models/study-page';

function makePage(overrides: Partial<StudyPage> = {}): StudyPage {
  return {
    id: 1,
    title: 'Baslik',
    description: '',
    isPublished: true,
    createdByUserId: 1,
    createdByName: 'Ogretmen',
    createdByRole: 'Teacher',
    createTime: new Date().toISOString(),
    contentType: StudyPageContentType.Image,
    imageCount: 1,
    images: [],
    ...overrides,
  } as StudyPage;
}

describe('StudyPagesComponent', () => {
  let component: StudyPagesComponent;
  let studyPageService: jasmine.SpyObj<StudyPageService>;

  beforeEach(() => {
    studyPageService = jasmine.createSpyObj<StudyPageService>('StudyPageService', [
      'getPaged',
      'delete',
    ]);
    studyPageService.getPaged.and.returnValue(
      of({ items: [], totalCount: 0, pageNumber: 1, pageSize: 10 })
    );

    const subjectService = jasmine.createSpyObj<SubjectService>('SubjectService', ['loadCategories']);
    subjectService.loadCategories.and.returnValue(of([]));

    TestBed.configureTestingModule({
      imports: [StudyPagesComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        { provide: StudyPageService, useValue: studyPageService },
        { provide: SubjectService, useValue: subjectService },
        { provide: MatDialog, useValue: jasmine.createSpyObj<MatDialog>('MatDialog', ['open']) },
        { provide: MatSnackBar, useValue: jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']) },
      ],
    });

    component = TestBed.createComponent(StudyPagesComponent).componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  // Kriter 5: her etkinligin tipi ikon/etiketle ayirt edilebiliyor
  describe('content type badge helpers', () => {
    it('contentTypeIcon_ImagePage_ReturnsImageIcon', () => {
      const page = makePage({ contentType: StudyPageContentType.Image });
      expect(component.contentTypeIcon(page)).toBe('image');
      expect(component.contentTypeLabel(page)).toBe('Görsel');
      expect(component.isImage(page)).toBeTrue();
      expect(component.isLink(page)).toBeFalse();
      expect(component.isBookPageRange(page)).toBeFalse();
    });

    it('contentTypeIcon_LinkPage_ReturnsLinkIcon', () => {
      const page = makePage({ contentType: StudyPageContentType.Link });
      expect(component.contentTypeIcon(page)).toBe('link');
      expect(component.contentTypeLabel(page)).toBe('Link');
      expect(component.isLink(page)).toBeTrue();
    });

    it('contentTypeIcon_BookPageRangePage_ReturnsMenuBookIcon', () => {
      const page = makePage({ contentType: StudyPageContentType.BookPageRange });
      expect(component.contentTypeIcon(page)).toBe('menu_book');
      expect(component.contentTypeLabel(page)).toBe('Kitap Sayfası');
      expect(component.isBookPageRange(page)).toBeTrue();
    });

    it('contentTypeIcon_LegacyPageWithoutContentType_TreatsAsImage', () => {
      const page = makePage({ contentType: undefined as unknown as StudyPageContentType });
      expect(component.isImage(page)).toBeTrue();
      expect(component.contentTypeIcon(page)).toBe('image');
    });

    it('contentTypeClass_EachType_ReturnsMatchingCssClass', () => {
      expect(component.contentTypeClass(makePage({ contentType: StudyPageContentType.Image }))).toBe('type-image');
      expect(component.contentTypeClass(makePage({ contentType: StudyPageContentType.Link }))).toBe('type-link');
      expect(component.contentTypeClass(makePage({ contentType: StudyPageContentType.BookPageRange }))).toBe(
        'type-book'
      );
    });

    it('platformIcon_EbaPlatform_ReturnsSchoolIconAndIsKnownPlatformTrue', () => {
      const page = makePage({ contentType: StudyPageContentType.Link, platform: StudyPageLinkPlatform.Eba });
      expect(component.platformIcon(page)).toBe('school');
      expect(component.platformLabel(page)).toBe('EBA');
      expect(component.isKnownPlatform(page)).toBeTrue();
    });

    it('platformIcon_YouTubePlatform_ReturnsSmartDisplayIconAndIsKnownPlatformTrue', () => {
      const page = makePage({ contentType: StudyPageContentType.Link, platform: StudyPageLinkPlatform.YouTube });
      expect(component.platformIcon(page)).toBe('smart_display');
      expect(component.isKnownPlatform(page)).toBeTrue();
    });

    it('platformIcon_OtherOrMissingPlatform_IsKnownPlatformFalse', () => {
      const page = makePage({ contentType: StudyPageContentType.Link, platform: StudyPageLinkPlatform.Other });
      expect(component.isKnownPlatform(page)).toBeFalse();

      const noPlatform = makePage({ contentType: StudyPageContentType.Link, platform: undefined });
      expect(component.platformIcon(noPlatform)).toBe('link');
      expect(component.isKnownPlatform(noPlatform)).toBeFalse();
    });
  });
});
