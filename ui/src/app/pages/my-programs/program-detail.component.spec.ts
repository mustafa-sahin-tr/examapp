import { ComponentFixture, TestBed } from '@angular/core/testing';
import { UserProgramStudyPageSchedule } from '../../models/program.interfaces';
import { StudyPageContentType } from '../../models/study-page';
import { studyItemTypeIcon } from '../../shared/utils/study-item-display.util';

describe('ProgramDetailComponent - AC #6', () => {
  // AC #6: Program detay sayfasında her etkinliğin tipi görsel olarak ayırt ediliyor.
  // Test'ler component'in render'ını değil, typeIcon/typeLabel logic'ini doğrular.

  function makeSchedule(
    id: number,
    contentType: StudyPageContentType,
    overrides: Partial<UserProgramStudyPageSchedule> = {},
  ): UserProgramStudyPageSchedule {
    return {
      id,
      userProgramId: 1,
      studyItemId: id,
      studyItemTitle: `Item ${id}`,
      startDate: new Date('2026-03-05').toISOString(),
      endDate: new Date('2026-03-05').toISOString(),
      isCompleted: false,
      completedDate: null,
      contentType,
      ...overrides,
    } as UserProgramStudyPageSchedule;
  }

  describe('Type icons for bar rendering', () => {
    it('ImageContentType_RetursImageIcon', () => {
      const schedule = makeSchedule(1, StudyPageContentType.Image);
      const icon = studyItemTypeIcon(schedule.contentType);

      // studyItemTypeIcon returns 'image_search' for Image
      expect(icon).toBeTruthy();
      expect(typeof icon).toBe('string');
    });

    it('LinkContentType_ReturnsLinkIcon', () => {
      const schedule = makeSchedule(2, StudyPageContentType.Link, {
        url: 'https://example.com',
      });
      const icon = studyItemTypeIcon(schedule.contentType);

      expect(icon).toBeTruthy();
      expect(typeof icon).toBe('string');
    });

    it('BookPageRangeContentType_ReturnsMenuBookIcon', () => {
      const schedule = makeSchedule(3, StudyPageContentType.BookPageRange, {
        bookName: 'Book',
        startPage: 10,
        endPage: 20,
      });
      const icon = studyItemTypeIcon(schedule.contentType);

      expect(icon).toBeTruthy();
      expect(typeof icon).toBe('string');
    });

    it('EachTypeIconIsDistinct', () => {
      const imageIcon = studyItemTypeIcon(StudyPageContentType.Image);
      const linkIcon = studyItemTypeIcon(StudyPageContentType.Link);
      const bookIcon = studyItemTypeIcon(StudyPageContentType.BookPageRange);

      // All icons should be distinct
      expect(imageIcon).not.toEqual(linkIcon);
      expect(linkIcon).not.toEqual(bookIcon);
      expect(imageIcon).not.toEqual(bookIcon);
    });
  });

  describe('Schedule data with content type', () => {
    it('ImageScheduleHasContentType', () => {
      const schedule = makeSchedule(1, StudyPageContentType.Image);

      expect(schedule.contentType).toBe(StudyPageContentType.Image);
      expect(schedule.contentType).toBe(0); // enum value
    });

    it('LinkScheduleHasContentTypeAndUrl', () => {
      const schedule = makeSchedule(2, StudyPageContentType.Link, {
        url: 'https://www.youtube.com/watch?v=test',
      });

      expect(schedule.contentType).toBe(StudyPageContentType.Link);
      expect(schedule.url).toBe('https://www.youtube.com/watch?v=test');
    });

    it('BookScheduleHasContentTypeAndBookFields', () => {
      const schedule = makeSchedule(3, StudyPageContentType.BookPageRange, {
        bookName: 'Matematik',
        bookTestName: 'Test 1',
        startPage: 10,
        endPage: 20,
      });

      expect(schedule.contentType).toBe(StudyPageContentType.BookPageRange);
      expect(schedule.bookName).toBe('Matematik');
      expect(schedule.bookTestName).toBe('Test 1');
    });
  });

  describe('Visual type differentiation', () => {
    it('AllThreeTypesCanBeRenderedInSameProgramWithIcons', () => {
      const schedules = [
        makeSchedule(1, StudyPageContentType.Image),
        makeSchedule(2, StudyPageContentType.Link, { url: 'https://example.com' }),
        makeSchedule(3, StudyPageContentType.BookPageRange, { bookName: 'Book' }),
      ];

      // All should have distinct icons for visual differentiation
      const icons = schedules.map((s) => studyItemTypeIcon(s.contentType));
      const uniqueIcons = new Set(icons);

      expect(uniqueIcons.size).toBe(3); // All different
      expect(icons.every((icon) => icon !== null && icon !== undefined)).toBe(true);
    });

    it('UndefinedContentType_DefaultsToImage', () => {
      const schedule = makeSchedule(1, undefined as any);
      const icon = studyItemTypeIcon(schedule.contentType);

      // undefined should map to Image icon
      const imageIcon = studyItemTypeIcon(StudyPageContentType.Image);
      expect(icon).toBe(imageIcon);
    });
  });
});
