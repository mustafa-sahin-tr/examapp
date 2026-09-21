import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { ScheduleDetailDialogComponent, ScheduleDetailDialogData } from './schedule-detail-dialog.component';
import { UserProgramStudyPageSchedule } from '../../models/program.interfaces';
import { StudyPageContentType, StudyPageLinkPlatform } from '../../models/study-page';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';

describe('ScheduleDetailDialogComponent', () => {
  let component: ScheduleDetailDialogComponent;
  let fixture: ComponentFixture<ScheduleDetailDialogComponent>;
  let dialogRef: jasmine.SpyObj<MatDialogRef<ScheduleDetailDialogComponent>>;

  function makeSchedule(overrides: Partial<UserProgramStudyPageSchedule> = {}): UserProgramStudyPageSchedule {
    return {
      id: 1,
      userProgramId: 1,
      studyItemId: 1,
      studyItemTitle: 'Test Item',
      startDate: new Date('2026-03-05').toISOString(),
      endDate: new Date('2026-03-05').toISOString(),
      isCompleted: false,
      completedDate: null,
      ...overrides,
    } as UserProgramStudyPageSchedule;
  }

  function createComponent(schedule: UserProgramStudyPageSchedule): ComponentFixture<ScheduleDetailDialogComponent> {
    dialogRef = jasmine.createSpyObj<MatDialogRef<ScheduleDetailDialogComponent>>('MatDialogRef', ['close']);

    TestBed.configureTestingModule({
      imports: [ScheduleDetailDialogComponent, translocoTestingModule()],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: { schedule, color: '#FF5722' } as ScheduleDetailDialogData },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    });

    return TestBed.createComponent(ScheduleDetailDialogComponent);
  }

  describe('Link type rendering', () => {
    // AC #1: Link tipi etkinliğe tıklandığında yeni sekmede açılıyor, platform ikonu gösteriliyor

    it('LinkType_YouTubeUrl_RendersOpenButtonAndPlatformIcon', () => {
      const schedule = makeSchedule({
        contentType: StudyPageContentType.Link,
        url: 'https://www.youtube.com/watch?v=test',
        platform: StudyPageLinkPlatform.YouTube,
      });
      fixture = createComponent(schedule);
      component = fixture.componentInstance;
      fixture.detectChanges();

      expect(component.display.kind).toBe('link');
      expect(component.display.href).toBe('https://www.youtube.com/watch?v=test');
      expect(component.display.platformLabel).toBe('YouTube');
    });

    it('LinkType_EbaUrl_RendersWithEbaPlatformIcon', () => {
      const schedule = makeSchedule({
        contentType: StudyPageContentType.Link,
        url: 'https://www.eba.gov.tr/path',
        platform: StudyPageLinkPlatform.Eba,
      });
      fixture = createComponent(schedule);
      component = fixture.componentInstance;
      fixture.detectChanges();

      expect(component.display.kind).toBe('link');
      expect(component.display.href).toBe('https://www.eba.gov.tr/path');
      expect(component.display.platformLabel).toBe('EBA');
    });

    it('LinkType_UnsafeUrl_DoesNotRenderAsLink', () => {
      const schedule = makeSchedule({
        contentType: StudyPageContentType.Link,
        url: 'javascript:alert("xss")',
        platform: StudyPageLinkPlatform.Other,
      });
      fixture = createComponent(schedule);
      component = fixture.componentInstance;
      fixture.detectChanges();

      expect(component.display.kind).toBe('link');
      expect(component.display.href).toBeNull();
    });

    it('LinkType_DataUrl_DoesNotRenderAsLink', () => {
      const schedule = makeSchedule({
        contentType: StudyPageContentType.Link,
        url: 'data:text/html,<script>alert("xss")</script>',
        platform: StudyPageLinkPlatform.Other,
      });
      fixture = createComponent(schedule);
      component = fixture.componentInstance;
      fixture.detectChanges();

      expect(component.display.href).toBeNull();
    });
  });

  describe('BookPageRange type rendering', () => {
    // AC #2: BookPageRange "Kitap Adı — Test Adı, sayfa X-Y" formatında

    it('BookPageRangeType_FullFormat_ReturnsFormattedLine', () => {
      const schedule = makeSchedule({
        contentType: StudyPageContentType.BookPageRange,
        bookName: 'Matematik',
        bookTestName: 'Test 1',
        startPage: 10,
        endPage: 20,
      });
      fixture = createComponent(schedule);
      component = fixture.componentInstance;
      fixture.detectChanges();

      expect(component.display.kind).toBe('book');
      expect(component.display.bookLine).toContain('Matematik — Test 1');
      expect(component.display.bookLine).toMatch(/10.*20/);
    });

    it('BookPageRangeType_BookAndPagesOnly_ReturnsFormattedLine', () => {
      const schedule = makeSchedule({
        contentType: StudyPageContentType.BookPageRange,
        bookName: 'Fizik',
        startPage: 5,
        endPage: 15,
      });
      fixture = createComponent(schedule);
      component = fixture.componentInstance;
      fixture.detectChanges();

      expect(component.display.kind).toBe('book');
      expect(component.display.bookLine).toContain('Fizik');
      expect(component.display.bookLine).toMatch(/5.*15/);
    });

    it('BookPageRangeType_SinglePage_ReturnsSinglePageFormat', () => {
      const schedule = makeSchedule({
        contentType: StudyPageContentType.BookPageRange,
        bookName: 'Kimya',
        startPage: 42,
        endPage: 42,
      });
      fixture = createComponent(schedule);
      component = fixture.componentInstance;
      fixture.detectChanges();

      expect(component.display.bookLine).toContain('Kimya');
      expect(component.display.bookLine).toContain('42');
    });
  });

  describe('Image type rendering', () => {
    // AC #3: Image mevcut davranışıyla değişmeden

    it('ImageType_ReturnsImageDisplay', () => {
      const schedule = makeSchedule({
        contentType: StudyPageContentType.Image,
      });
      fixture = createComponent(schedule);
      component = fixture.componentInstance;
      fixture.detectChanges();

      expect(component.display.kind).toBe('image');
      expect(component.display.bookLine).toBeNull();
      expect(component.display.href).toBeNull();
    });

    it('ImageType_DefaultContentType_TreatsAsImage', () => {
      const schedule = makeSchedule({
        contentType: undefined,
      });
      fixture = createComponent(schedule);
      component = fixture.componentInstance;
      fixture.detectChanges();

      expect(component.display.kind).toBe('image');
    });
  });

  describe('Dialog interaction', () => {
    it('Close_CallsDialogRefClose', () => {
      const schedule = makeSchedule();
      fixture = createComponent(schedule);
      component = fixture.componentInstance;
      fixture.detectChanges();

      component.close();

      expect(dialogRef.close).toHaveBeenCalled();
    });
  });
});
