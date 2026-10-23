import { Component, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';

import { QuestionBankComponent } from './question-bank.component';
import { QuestionBankPanelComponent } from '../../shared/components/question-bank-panel/question-bank-panel.component';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import qbTr from '../../../../public/i18n/question-bank/tr.json';

/** Panelin kendi HTTP/dialog bağımlılıkları yok sayılır; sayfa yalnız panel'i yerleştirir. */
@Component({ selector: 'app-question-bank-panel', standalone: true, changeDetection: ChangeDetectionStrategy.Eager,
    template: '<div class="panel-stub"></div>' })
class QuestionBankPanelStub {
}

describe('QuestionBankComponent (issue #471)', () => {
    let fixture: ComponentFixture<QuestionBankComponent>;

    beforeEach(() => {
        TestBed.overrideComponent(QuestionBankComponent, {
            remove: { imports: [QuestionBankPanelComponent] },
            add: { imports: [QuestionBankPanelStub] },
        });
        TestBed.configureTestingModule({
            imports: [QuestionBankComponent, translocoTestingModule({ langs: { 'question-bank/tr': qbTr } })],
            providers: [provideNoopAnimations()],
        });
        fixture = TestBed.createComponent(QuestionBankComponent);
        fixture.detectChanges();
    });

    it('template_Rendered_ShowsTranslatedTitle', () => {
        const heading = fixture.nativeElement.querySelector('h1') as HTMLElement;
        expect(heading.textContent?.trim()).toBe(qbTr.title);
    });

    it('template_Rendered_EmbedsSearchPanel', () => {
        expect(fixture.nativeElement.querySelector('app-question-bank-panel')).not.toBeNull();
    });
});
