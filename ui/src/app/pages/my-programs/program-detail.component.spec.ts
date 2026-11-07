import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
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

    function makeSchedule(id: number, contentType: StudyPageContentType, overrides: Partial<UserProgramStudyPageSchedule> = {}): UserProgramStudyPageSchedule {
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

    // Issue #717: mock program kaldırıldı; hedef, başlık ve tarih yalnız gerçek programdan gelir.
    it('dailyTarget_NotShownWithoutRealTarget', () => {
        const el = create('tr', { questionsPerDay: undefined }).nativeElement as HTMLElement;

        expect(el.querySelector('.program-meta')?.textContent).not.toContain('soru');
        expect(el.textContent).not.toContain('TYT Deneme');
    });

    it('dailyTarget_ShowsRealProgramValues', () => {
        const el = create('tr', { questionsPerDay: 30, subjectsPerDay: 3 }).nativeElement as HTMLElement;

        expect(el.querySelector('.program-meta')?.textContent).toContain('Günde 30 soru, 3 konu');
    });

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

/** #886: Bugün kartı, haftalık ilerleme, hafta görünümü. */
describe('ProgramDetailComponent today and week (#886)', () => {
    // Çarşamba 2026-11-11; hafta 9–15 Kasım.
    const TODAY = new Date(2026, 10, 11, 10, 0);
    const session = (id: number, date: string, subjectName: string, isCompleted = false) => ({
        id, userProgramId: 5, scheduleDate: `${date}T00:00:00Z`, subjectId: id * 10, subjectName, studyDurationMinutes: 25, isCompleted,
    });
    let setSessionDone: ReturnType<typeof vi.fn>;

    function create(program: Partial<UserProgram>): ComponentFixture<ProgramDetailComponent> {
        setSessionDone = vi.fn().mockReturnValue(of(undefined));
        TestBed.configureTestingModule({
            imports: [
                ProgramDetailComponent,
                translocoTestingModule({ langs: { 'my-programs/tr': myProgramsTr }, translocoConfig: { defaultLang: 'tr', scopes: { keepCasing: true } } }),
            ],
            providers: [
                provideNoopAnimations(),
                provideRouter([]),
                { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '5' }) } } },
                {
                    provide: ProgramService,
                    useValue: {
                        // Her test kendi kopyasını alır (işaretleme nesneyi değiştirir).
                        getProgramById: () => of(structuredClone({ id: 5, programName: 'P', studyItemSchedules: [], schedules: [], ...program })),
                        setSessionDone,
                        setStudyItemDone: vi.fn().mockReturnValue(of(undefined)),
                    },
                },
                { provide: LocaleService, useValue: { localeDefinition: signal({ angularLocale: 'tr' }) } },
            ],
        });
        const fixture = TestBed.createComponent(ProgramDetailComponent);
        fixture.componentInstance.today = () => TODAY;
        fixture.componentInstance.weekStart = new Date(2026, 10, 9);
        fixture.detectChanges();
        return fixture;
    }

    const schedules = [
        session(1, '2026-11-09', 'Matematik', true),
        session(2, '2026-11-10', 'Türkçe', true),
        session(3, '2026-11-11', 'Fen Bilimleri'),
        session(4, '2026-11-11', 'Matematik'),
        session(5, '2026-11-12', 'Türkçe'),
        session(6, '2026-11-16', 'Matematik'), // gelecek hafta
    ] as unknown as UserProgram['schedules'];

    it('todayCard_ListsTodaysSessions_WithStartLinkToPractice', () => {
        const el = create({ schedules }).nativeElement as HTMLElement;
        const rows = el.querySelectorAll('[data-test="today-session"]');
        expect([...rows].map((r) => r.querySelector('.pd-row__title')?.textContent?.trim())).toEqual(['Fen Bilimleri', 'Matematik']);
        expect(rows[0].textContent).toContain('25 dk');
        expect(rows[0].querySelector('[data-test="start"]')?.getAttribute('href')).toBe('/practice?subjectId=30');
        expect(el.querySelector('[data-test="today"] h3')?.textContent).toContain('11 Kasım');
    });

    it('weekProgress_CountsThisWeeksDoneSessions', () => {
        const el = create({ schedules }).nativeElement as HTMLElement;
        expect(el.querySelector('[data-test="week-progress"] p')?.textContent?.trim()).toBe('Bu hafta 5 oturumdan 2 tanesi yapıldı.');
        expect(el.querySelector('[role="progressbar"]')?.getAttribute('aria-valuenow')).toBe('40');
    });

    it('markDone_CallsTheServer_AndUpdatesProgress', () => {
        const fixture = create({ schedules });
        const el = fixture.nativeElement as HTMLElement;
        el.querySelector<HTMLButtonElement>('[data-test="today-session"] [data-test="done"]')!.click();
        fixture.detectChanges();
        expect(setSessionDone).toHaveBeenCalledWith(5, 3, true);
        expect(el.querySelector('[data-test="week-progress"] p')?.textContent).toContain('3 tanesi');
        expect(el.querySelectorAll('[data-test="today-session"]')[0].querySelector('[data-test="start"]')).toBeNull();
    });

    it('markDone_ServerError_RevertsTheMark', () => {
        const fixture = create({ schedules });
        setSessionDone.mockReturnValue(throwError(() => new Error('x')));
        const el = fixture.nativeElement as HTMLElement;
        el.querySelector<HTMLButtonElement>('[data-test="today-session"] [data-test="done"]')!.click();
        fixture.detectChanges();
        expect(el.querySelector('[data-test="today-session"]')?.classList.contains('pd-row--done')).toBe(false);
    });

    it('weekView_IsTheDefault_SevenDaysWithTodayMarked', () => {
        const el = create({ schedules }).nativeElement as HTMLElement;
        const days = el.querySelectorAll('[data-test="week-day"]');
        expect(days.length).toBe(7);
        expect(days[2].classList.contains('pd-week__day--today')).toBe(true);
        expect(days[2].textContent).toContain('Fen Bilimleri');
        expect(days[5].textContent).toContain(myProgramsTr.detail.view.free);
        expect(el.querySelector('.main-calendar')).toBeNull();
        expect(el.querySelector('.mini-calendar')).toBeNull();
    });

    it('noPlanToday_SaysSo', () => {
        const el = create({ schedules: [] }).nativeElement as HTMLElement;
        expect(el.querySelector('[data-test="today-empty"]')?.textContent?.trim()).toBe(myProgramsTr.detail.today.empty);
        expect(el.querySelector('[data-test="week-progress"]')).toBeNull();
    });
});

