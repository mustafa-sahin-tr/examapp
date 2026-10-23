import type { MockedObject } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';
import { AddStudyPagesDialogComponent, AddStudyPagesDialogData } from './add-study-pages-dialog.component';
import { StudyPageService } from '../../services/study-page.service';
import { StudyPage, StudyPageContentType, StudyPageLinkPlatform } from '../../models/study-page';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';

describe('AddStudyPagesDialogComponent', () => {
    beforeEach(() => {
        vi.useFakeTimers();
    });
    afterEach(() => {
        vi.useRealTimers();
    });
    let component: AddStudyPagesDialogComponent;
    let fixture: ComponentFixture<AddStudyPagesDialogComponent>;
    let studyPageService: MockedObject<StudyPageService>;
    let dialogRef: MockedObject<MatDialogRef<AddStudyPagesDialogComponent>>;

    function makeStudyPage(id: number, title: string, contentType: StudyPageContentType = StudyPageContentType.Image, overrides: Partial<StudyPage> = {}): StudyPage {
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
        dialogRef = {
            close: vi.fn().mockName("MatDialogRef.close")
        } as unknown as MockedObject<MatDialogRef<AddStudyPagesDialogComponent, any>>;
        studyPageService = {
            getPaged: vi.fn().mockName("StudyPageService.getPaged")
        } as unknown as MockedObject<StudyPageService>;

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
        it('NoFilterApplied_CallsGetPagedWithNullContentTypeAndNullSearch', async () => {
            const pages = [makeStudyPage(1, 'Page 1')];
            studyPageService.getPaged.mockReturnValue(of({ items: pages, totalCount: 1, pageNumber: 1, pageSize: 200 } as any));

            fixture = TestBed.createComponent(AddStudyPagesDialogComponent);
            component = fixture.componentInstance;
            fixture.detectChanges();
            await vi.advanceTimersByTimeAsync(0);

            expect(studyPageService.getPaged).toHaveBeenCalledWith(expect.objectContaining({
                contentType: null,
                search: null,
            }));
            expect(component.items()).toEqual(pages);
        });


        it('AllTypeFiltersAvailable_CanSelectEach', async () => {
            const allPages = [
                makeStudyPage(1, 'Image', StudyPageContentType.Image),
                makeStudyPage(2, 'Link', StudyPageContentType.Link, {
                    url: 'https://example.com',
                }),
                makeStudyPage(3, 'Book', StudyPageContentType.BookPageRange, {
                    bookName: 'Book',
                }),
            ];
            studyPageService.getPaged.mockReturnValue(of({ items: allPages, totalCount: 3, pageNumber: 1, pageSize: 200 } as any));

            fixture = TestBed.createComponent(AddStudyPagesDialogComponent);
            component = fixture.componentInstance;
            fixture.detectChanges();
            await vi.advanceTimersByTimeAsync(0);

            // All three types should be selectable
            expect(component.typeFilterOptions.length).toBe(4); // all + 3 types
            expect(component.typeFilterOptions.map((o) => o.value)).toContain('all');
            expect(component.typeFilterOptions.map((o) => o.value)).toContain(StudyPageContentType.Image);
            expect(component.typeFilterOptions.map((o) => o.value)).toContain(StudyPageContentType.Link);
            expect(component.typeFilterOptions.map((o) => o.value)).toContain(StudyPageContentType.BookPageRange);
        });
    });

    describe('Card rendering by content type', () => {
        it('ImageType_RendersImageDisplay', async () => {
            const page = makeStudyPage(1, 'Sheet', StudyPageContentType.Image);
            studyPageService.getPaged.mockReturnValue(of({ items: [page], totalCount: 1, pageNumber: 1, pageSize: 200 } as any));

            fixture = TestBed.createComponent(AddStudyPagesDialogComponent);
            component = fixture.componentInstance;
            fixture.detectChanges();
            await vi.advanceTimersByTimeAsync(0);

            expect(component.cards()[0].display.kind).toBe('image');
        });

        it('LinkType_RendersWithPlatformIconAndHost', async () => {
            const page = makeStudyPage(2, 'YouTube', StudyPageContentType.Link, {
                url: 'https://www.youtube.com/watch?v=test',
                platform: StudyPageLinkPlatform.YouTube,
            });
            studyPageService.getPaged.mockReturnValue(of({ items: [page], totalCount: 1, pageNumber: 1, pageSize: 200 } as any));

            fixture = TestBed.createComponent(AddStudyPagesDialogComponent);
            component = fixture.componentInstance;
            fixture.detectChanges();
            await vi.advanceTimersByTimeAsync(0);

            const card = component.cards()[0];
            expect(card.display.kind).toBe('link');
            expect(card.display.platformLabel).toBeTruthy();
            expect(card.display.host).toContain('youtube');
        });

        it('BookPageRangeType_RendersFormattedLine', async () => {
            const page = makeStudyPage(3, 'Book', StudyPageContentType.BookPageRange, {
                bookName: 'Matematik',
                bookTestName: 'Test 1',
                startPage: 10,
                endPage: 20,
            });
            studyPageService.getPaged.mockReturnValue(of({ items: [page], totalCount: 1, pageNumber: 1, pageSize: 200 } as any));

            fixture = TestBed.createComponent(AddStudyPagesDialogComponent);
            component = fixture.componentInstance;
            fixture.detectChanges();
            await vi.advanceTimersByTimeAsync(0);

            const card = component.cards()[0];
            expect(card.display.kind).toBe('book');
            expect(card.display.bookLine).toContain('Matematik');
        });
    });

    describe('Error handling', () => {
        it('ServiceError_ShowsErrorState', async () => {
            studyPageService.getPaged.mockReturnValue(throwError(() => new Error('Network error')));

            fixture = TestBed.createComponent(AddStudyPagesDialogComponent);
            component = fixture.componentInstance;
            fixture.detectChanges();
            await vi.advanceTimersByTimeAsync(0);

            expect(component.error()).toBe(true);
        });
    });
});
