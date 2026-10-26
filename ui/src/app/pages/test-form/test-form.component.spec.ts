import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormBuilder } from '@angular/forms';
import { By } from '@angular/platform-browser';

import { TestFormComponent } from './test-form.component';

import { TranslocoTestingModule } from '@jsverse/transloco';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../models/locale';
import rootTr from '../../../../public/i18n/tr.json';
import testCreateTr from '../../../../public/i18n/test-create/tr.json';

/** Gercek scope sozlugu yuklenir; anahtar bozulursa test kirilir (issue #183). */
const translocoTesting = TranslocoTestingModule.forRoot({
    // Scope sozlugu hem scope yolu (provideTranslocoScope yukleyicisi) hem de kok 'tr' icine
    // gomulu olarak verilir; sablondaki 'prefix' bicimi ikincisinden cozulur.
    langs: { tr: { ...rootTr, 'test-create': testCreateTr }, 'test-create/tr': testCreateTr },
    translocoConfig: {
        availableLangs: [...SUPPORTED_LOCALE_CODES],
        defaultLang: DEFAULT_LOCALE,
        // TranslocoTestingModule uygulamanin config'ini almaz; scope oneki kebab kalsin (issue #183).
        scopes: { keepCasing: true },
    },
    preloadLangs: true,
});

describe('TestFormComponent', () => {
    let fixture: ComponentFixture<TestFormComponent>;
    let component: TestFormComponent;

    beforeEach(async () => {
        await TestBed.configureTestingModule({
            imports: [TestFormComponent, translocoTesting],
        }).compileComponents();

        fixture = TestBed.createComponent(TestFormComponent);
        component = fixture.componentInstance;
        component.form = new FormBuilder().group({
            name: [''],
            subtitle: [''],
            description: [''],
            bookId: [null],
            bookTestId: [null],
            newBookName: [''],
            newBookTestName: [''],
            gradeId: [null],
            subjectId: [null],
            topicId: [null],
            subtopicId: [null],
            maxDurationMinutes: [10],
            isPracticeTest: [false],
            imageUrl: [''],
        });
    });

    it('builds', () => {
        fixture.detectChanges();
        expect(component).toBeTruthy();
    });

    // Issue #544: Excel toplu yükleme kalktı; ona ait "Hepsine uygula" düğmeleri formda yok.
    it('renders_NoApplyToAllTriggers', () => {
        fixture.detectChanges();

        const icons = fixture.debugElement
            .queryAll(By.css('button mat-icon'))
            .map((i) => (i.nativeElement.textContent || '').trim());
        expect(icons).not.toContain('done_all');
    });

    // Issue #574: soru tuvalindeki hızlı oluşturmada da Tür (Test / Etkinlik sayfası) seçilir.
    describe('compact kind toggle (#574)', () => {
        it('compactWithKindControl_ShowsToggleAndActivityHidesDurationAndPractice', () => {
            component.form.addControl('kind', new FormBuilder().control(0));
            component.compact = true;
            fixture.detectChanges();
            const el = fixture.nativeElement as HTMLElement;
            expect(el.querySelector('[data-testid="kind-toggle"]')).not.toBeNull();
            expect(el.querySelector('input[formControlName="maxDurationMinutes"]')).not.toBeNull();

            (el.querySelector('[data-testid="kind-activity"] button') as HTMLButtonElement).click();
            fixture.detectChanges();

            expect(component.form.get('kind')!.value).toBe(1);
            expect(el.querySelector('input[formControlName="maxDurationMinutes"]')).toBeNull();
            expect(el.textContent).not.toContain(testCreateTr.form.practiceLabel);
        });

        it('compactWithoutKindControl_HidesToggle', () => {
            component.compact = true;
            fixture.detectChanges();
            expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="kind-toggle"]')).toBeNull();
        });
    });

    describe('compact input', () => {
        it('compact_DefaultsToFalse_RendersFullForm', () => {
            fixture.detectChanges();
            const form = fixture.debugElement.query(By.css('form.tf'));
            expect(component.compact).toBe(false);
            expect(form.nativeElement.classList.contains('compact')).toBe(false);
        });

        it('compact_WhenTrue_AddsCompactClassToForm', () => {
            component.compact = true;
            fixture.detectChanges();
            const form = fixture.debugElement.query(By.css('form.tf'));
            expect(form.nativeElement.classList.contains('compact')).toBe(true);
        });
    });

    /** Issue #105: öğrenci yorum/soru anahtarı (varsayılan açık, togglePractice() deseni). */
    describe('commentsEnabled toggle', () => {
        const toggle = () => fixture.debugElement.query(By.css('[data-testid="comments-toggle"]')).nativeElement as HTMLButtonElement;

        beforeEach(() => {
            component.form.addControl('commentsEnabled', new FormBuilder().control(true));
        });

        it('commentsToggle_FullForm_RendersOnWithLabel', () => {
            fixture.detectChanges();

            expect(toggle().classList.contains('off')).toBe(false);
            expect(toggle().getAttribute('aria-pressed')).toBe('true');
            expect(fixture.nativeElement.textContent).toContain(testCreateTr.form.commentsLabel);
        });

        it('commentsToggle_Click_FlipsControlValue', () => {
            fixture.detectChanges();

            toggle().click();
            fixture.detectChanges();
            expect(component.form.get('commentsEnabled')!.value).toBe(false);
            expect(toggle().classList.contains('off')).toBe(true);
            expect(toggle().getAttribute('aria-pressed')).toBe('false');

            toggle().click();
            expect(component.form.get('commentsEnabled')!.value).toBe(true);
        });

        it('commentsToggle_Compact_AlsoRendered', () => {
            component.compact = true;
            fixture.detectChanges();

            toggle().click();
            expect(component.form.get('commentsEnabled')!.value).toBe(false);
        });

        it('commentsToggle_DisabledForm_DoesNotChange', () => {
            component.form.disable();
            fixture.detectChanges();

            expect(toggle().disabled).toBe(true);
            component.toggleComments();
            expect(component.form.get('commentsEnabled')!.value).toBe(true);
        });

        it('toggleComments_MissingControl_IsNoop', () => {
            component.form.removeControl('commentsEnabled');
            expect(() => component.toggleComments()).not.toThrow();
        });
    });
});
