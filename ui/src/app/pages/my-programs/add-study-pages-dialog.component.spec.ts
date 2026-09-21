import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';
import { AddStudyPagesDialogComponent, AddStudyPagesDialogData } from './add-study-pages-dialog.component';
import { StudyPageService } from '../../services/study-page.service';
import { StudyPage, StudyPageContentType, StudyPageLinkPlatform } from '../../models/study-page';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';

describe('AddStudyPagesDialogComponent', () => {
  let component: AddStudyPagesDialogComponent;
  let fixture: ComponentFixture<AddStudyPagesDialogComponent>;
  let studyPageService: jasmine.SpyObj<StudyPageService>;
  let dialogRef: jasmine.SpyObj<MatDialogRef<AddStudyPagesDialogComponent>>;

  function makeStudyPage(
    id: number,
    title: string,
    contentType: StudyPageContentType = StudyPageContentType.Image,
    overrides: Partial<StudyPage> = {},
  ): StudyPage {
    return {
      id,
      title,
      description: '',
      contentType,
      isPublished: true,
      createdByUserId: 1,
      createdByName: 'Teacher',
      createdByRole: 'Teacher',
      ...overrides,
    } as StudyPage;
  }

  beforeEach(async () => {
    dialogRef = jasmine.createSpyObj<MatDialogRef<AddStudyPagesDialogComponent>>('MatDialogRef', ['close']);
    studyPageService = jasmine.createSpyObj<StudyPageService>('StudyPageService', ['getPaged']);

    await TestBed.configureTestingModule({
      imports: [AddStudyPagesDialogComponent, translocoTestingModule()],
      providers: [
        provideNoopAnimations(),
        { provide: MAT_DIALOG_DATA, useValue: {} },
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: StudyPageService, useValue: studyPageService },
      ],
    }).compileComponents();
  });

  describe('Filtering - AC #4: Etkinlik seçici tüm tipleri listeliyor ve filtrelenebiliyor', () => {
    it('NoFilterApplied_CallsGetPagedWithNullContentTypeAndNullSearch', fakeAsync(() => {
      const pages = [makeStudyPage(1, 'Page 1')];
      studyPageService.getPaged.and.returnValue(
        of({ items: pages, totalCount: 1, pageNumber: 1, pageSize: 200 } as any),
      );

      fixture = TestBed.createComponent(AddStudyPagesDialogComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();
      tick();

      expect(studyPageService.getPaged).toHaveBeenCalledWith(
        jasmine.objectContaining({
          contentType: null,
          search: null,
        }),
      );
      expect(component.items()).toEqual(pages);
    }));


    it('AllTypeFiltersAvailable_CanSelectEach', fakeAsync(() => {
      const allPages = [
        makeStudyPage(1, 'Image', StudyPageContentType.Image),
        makeStudyPage(2, 'Link', StudyPageContentType.Link, {
          url: 'https://example.com',
        }),
        makeStudyPage(3, 'Book', StudyPageContentType.BookPageRange, {
          bookName: 'Book',
        }),
      ];
      studyPageService.getPaged.and.returnValue(
        of({ items: allPages, totalCount: 3, pageNumber: 1, pageSize: 200 } as any),
      );

      fixture = TestBed.createComponent(AddStudyPagesDialogComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();
      tick();

      // All three types should be selectable
      expect(component.typeFilterOptions.length).toBe(4); // all + 3 types
      expect(component.typeFilterOptions.map((o) => o.value)).toContain('all');
      expect(component.typeFilterOptions.map((o) => o.value)).toContain(StudyPageContentType.Image);
      expect(component.typeFilterOptions.map((o) => o.value)).toContain(StudyPageContentType.Link);
      expect(component.typeFilterOptions.map((o) => o.value)).toContain(StudyPageContentType.BookPageRange);
    }));
  });

  describe('Card rendering by content type', () => {
    it('ImageType_RendersImageDisplay', fakeAsync(() => {
      const page = makeStudyPage(1, 'Sheet', StudyPageContentType.Image);
      studyPageService.getPaged.and.returnValue(
        of({ items: [page], totalCount: 1, pageNumber: 1, pageSize: 200 } as any),
      );

      fixture = TestBed.createComponent(AddStudyPagesDialogComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();
      tick();

      expect(component.cards()[0].display.kind).toBe('image');
    }));

    it('LinkType_RendersWithPlatformIconAndHost', fakeAsync(() => {
      const page = makeStudyPage(2, 'YouTube', StudyPageContentType.Link, {
        url: 'https://www.youtube.com/watch?v=test',
        platform: StudyPageLinkPlatform.YouTube,
      });
      studyPageService.getPaged.and.returnValue(
        of({ items: [page], totalCount: 1, pageNumber: 1, pageSize: 200 } as any),
      );

      fixture = TestBed.createComponent(AddStudyPagesDialogComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();
      tick();

      const card = component.cards()[0];
      expect(card.display.kind).toBe('link');
      expect(card.display.platformLabel).toBeTruthy();
      expect(card.display.host).toContain('youtube');
    }));

    it('BookPageRangeType_RendersFormattedLine', fakeAsync(() => {
      const page = makeStudyPage(3, 'Book', StudyPageContentType.BookPageRange, {
        bookName: 'Matematik',
        bookTestName: 'Test 1',
        startPage: 10,
        endPage: 20,
      });
      studyPageService.getPaged.and.returnValue(
        of({ items: [page], totalCount: 1, pageNumber: 1, pageSize: 200 } as any),
      );

      fixture = TestBed.createComponent(AddStudyPagesDialogComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();
      tick();

      const card = component.cards()[0];
      expect(card.display.kind).toBe('book');
      expect(card.display.bookLine).toContain('Matematik');
    }));
  });

  describe('Error handling', () => {
    it('ServiceError_ShowsErrorState', fakeAsync(() => {
      studyPageService.getPaged.and.returnValue(throwError(() => new Error('Network error')));

      fixture = TestBed.createComponent(AddStudyPagesDialogComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();
      tick();

      expect(component.error()).toBe(true);
    }));
  });
});
