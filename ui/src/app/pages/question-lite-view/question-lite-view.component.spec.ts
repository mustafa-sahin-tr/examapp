import { ComponentFixture, TestBed } from '@angular/core/testing';

import { QuestionLiteViewComponent } from './question-lite-view.component';
import { Question } from '../../models/question';

import { TranslocoTestingModule } from '@jsverse/transloco';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../models/locale';
import questionTr from '../../../../public/i18n/question/tr.json';

/** Gercek sozluk yuklenir; anahtar bozulursa test kirilir (issue #183). */
const translocoTesting = TranslocoTestingModule.forRoot({
    langs: { 'question/tr': questionTr },
    translocoConfig: {
        availableLangs: [...SUPPORTED_LOCALE_CODES],
        defaultLang: DEFAULT_LOCALE,
        // Uygulama config'i ile ayni: scope oneki klasor adiyla birebir ayni kalsin (issue #183).
        scopes: { keepCasing: true },
    },
    preloadLangs: true,
});

describe('QuestionLiteViewComponent', () => {
    let component: QuestionLiteViewComponent;
    let fixture: ComponentFixture<QuestionLiteViewComponent>;

    beforeEach(async () => {
        await TestBed.configureTestingModule({
            imports: [QuestionLiteViewComponent, translocoTesting]
        })
            .compileComponents();

        fixture = TestBed.createComponent(QuestionLiteViewComponent);
        component = fixture.componentInstance;
        fixture.detectChanges();
    });

    it('should create', () => {
        expect(component).toBeTruthy();
    });

    // #552: soru metni, alt metin ve örnek cevap Angular sanitizer'ından geçer (bypassSecurityTrustHtml yok).
    it('sanitizes question text, subText and practice answer HTML', () => {
        component.question = {
            id: 1,
            text: '<p><em>Soru</em></p><img src="x" onerror="alert(1)">',
            subText: '<script>alert(2)</script><u>alt</u>',
            practiceCorrectAnswer: '<a href="javascript:alert(3)">cevap</a>',
            imageUrl: '',
            isExample: true,
            answers: [],
            answerColCount: 1,
        } as unknown as Question;
        component.isPracticeTest = true;
        component.correctAnswerVisible = true;
        fixture.detectChanges();

        const root: HTMLElement = fixture.nativeElement;
        expect(root.querySelector('script')).toBeNull();
        expect(root.querySelector('[onerror]')).toBeNull();
        expect(root.querySelector('.correct-answer a')?.getAttribute('href')).not.toMatch(/^javascript:/);
        expect(root.querySelector('em')?.textContent).toBe('Soru');
        expect(root.querySelector('u')?.textContent).toBe('alt');
    });
});
