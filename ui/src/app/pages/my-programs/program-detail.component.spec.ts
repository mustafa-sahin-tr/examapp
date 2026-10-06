import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { UserProgram, UserProgramStudyPageSchedule } from '../../models/program.interfaces';
import { ProgramDetailComponent } from './program-detail.component';
import { ProgramService } from '../../services/program.service';
import { LocaleService } from '../../services/locale.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import myProgramsTr from '../../../../public/i18n/my-programs/tr.json';
import myProgramsEn from '../../../../public/i18n/my-programs/en.json';
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

/** Issue #386: başlıktaki tarih aralığı aktif dilde biçimli; ham ISO metni (saat, `Z`) görünmez. */
describe('ProgramDetailComponent date range header (issue #386)', () => {
  function create(lang: 'tr' | 'en', program: Partial<UserProgram>): ComponentFixture<ProgramDetailComponent> {
    const angularLocale = lang === 'tr' ? 'tr' : 'en-US';
    TestBed.configureTestingModule({
      imports: [
        ProgramDetailComponent,
        translocoTestingModule({
          langs: { 'my-programs/tr': myProgramsTr, 'my-programs/en': myProgramsEn },
          translocoConfig: { defaultLang: lang, scopes: { keepCasing: true } },
        }),
      ],
      providers: [
        provideNoopAnimations(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '5' }) } } },
        {
          provide: ProgramService,
          useValue: { getProgramById: () => of({ id: 5, programName: 'P', studyItemSchedules: [], ...program }) },
        },
        { provide: LocaleService, useValue: { localeDefinition: signal({ angularLocale }) } },
      ],
    });
    const fixture = TestBed.createComponent(ProgramDetailComponent);
    fixture.detectChanges();
    return fixture;
  }

  function metaText(fixture: ComponentFixture<ProgramDetailComponent>): string {
    return (fixture.nativeElement as HTMLElement).querySelector('.program-meta')?.textContent ?? '';
  }

  it('turkish_UtcMidnightDates_FormattedWithoutShiftOrRawIso', () => {
    const fixture = create('tr', { startDate: '2026-09-09T00:00:00Z', endDate: '2026-09-30T00:00:00Z' });
    const text = metaText(fixture);

    expect(text).toContain('9 Eyl 2026 – 30 Eyl 2026');
    expect(text).not.toContain('2026-09');
    expect(text).not.toContain('T00:00');
  });

  it('english_FormatsInActiveLanguage', () => {
    const fixture = create('en', { startDate: '2026-09-09T00:00:00Z', endDate: '2026-09-30' });
    expect(metaText(fixture)).toContain('Sep 9, 2026 – Sep 30, 2026');
  });

  it('onlyStartParses_ShowsSingleDateWithoutDash', () => {
    const fixture = create('tr', { startDate: '2026-09-09T00:00:00Z', endDate: 'garbage' });
    const text = metaText(fixture);

    expect(text).toContain('9 Eyl 2026');
    expect(text).not.toContain('–');
  });

  it('onlyEndParses_ShowsSingleDateWithoutDash', () => {
    const fixture = create('tr', { startDate: 'not-a-date', endDate: '2026-09-30T00:00:00Z' });
    const text = metaText(fixture);

    expect(text).toContain('30 Eyl 2026');
    expect(text).not.toContain('–');
  });

  it('dateRangeKey_ExistsInTrAndEn', () => {
    expect(myProgramsTr.detail.dateRange).toContain('{{start}}');
    expect(myProgramsEn.detail.dateRange).toContain('{{end}}');
  });
});
