import { ComponentFixture, TestBed } from '@angular/core/testing';

import { QuestionCanvasViewComponent } from './question-canvas-view.component';
import { translocoTestingModule } from '../../testing/transloco-testing';

describe('QuestionCanvasViewComponent', () => {
    let component: QuestionCanvasViewComponent;
    let fixture: ComponentFixture<QuestionCanvasViewComponent>;

    beforeEach(async () => {
        await TestBed.configureTestingModule({
            imports: [translocoTestingModule(), QuestionCanvasViewComponent]
        })
            .compileComponents();

        fixture = TestBed.createComponent(QuestionCanvasViewComponent);
        component = fixture.componentInstance;
        fixture.detectChanges();
    });

    it('should create', () => {
        expect(component).toBeTruthy();
    });

    describe('zoomPercent', () => {
        it('zoomPercent_DefaultScale_Returns100', () => {
            component.contentScale = 1;
            expect(component.zoomPercent).toBe(100);
        });

        it('zoomPercent_ScaledUp_ReturnsRoundedPercent', () => {
            component.contentScale = 1.234;
            expect(component.zoomPercent).toBe(123);
        });

        it('zoomPercent_ZeroOrMissingScale_FallsBackTo100', () => {
            component.contentScale = 0 as unknown as number;
            expect(component.zoomPercent).toBe(100);
        });
    });

    // #1185: geniş kırpıntı kartı aşmaz; küçülen tuvalde tıklama doğru şıkkı seçer.
    it('wideQuestion_FitsItsCard_AndAClickOnTheScaledCanvasPicksThatOption', () => {
        const host = fixture.nativeElement as HTMLElement;
        host.style.width = '400px';
        document.body.appendChild(host);
        const answers = [
            { id: 1, label: 'A', x: 100, y: 500, width: 200, height: 80, imageUrl: '' },
            { id: 2, label: 'B', x: 800, y: 500, width: 200, height: 80, imageUrl: '' },
        ];
        component.questionRegion = { id: 9, x: 100, y: 100, width: 1200, height: 600, answers } as any;
        const canvas = host.querySelector('.canvas-container canvas:last-child') as HTMLCanvasElement;
        canvas.width = 1200;
        canvas.height = 600;
        const picked: number[] = [];
        component.choiceSelected.subscribe((a) => picked.push(a.id));

        const rect = canvas.getBoundingClientRect();
        expect(rect.width).toBeLessThanOrEqual(400);

        // B: kırpıntıda x 700–900, y 400–480 → küçültülmüş tuvalde ortası.
        const ratio = rect.width / 1200;
        canvas.dispatchEvent(new MouseEvent('click', {
            clientX: rect.left + 800 * ratio,
            clientY: rect.top + 440 * ratio,
            bubbles: true,
        }));

        expect(picked).toEqual([2]);
        host.remove();
    });
});

