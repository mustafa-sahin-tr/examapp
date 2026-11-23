import { Component, ChangeDetectionStrategy, input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';

import { QuestionBankComponent } from './question-bank.component';
import { QuestionBankPanelComponent } from '../../shared/components/question-bank-panel/question-bank-panel.component';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import qbTr from '../../../../public/i18n/question-bank/tr.json';

/** Panelin kendi HTTP/dialog bağımlılıkları yok sayılır; sayfa yalnız panel'i yerleştirir. */
@Component({ selector: 'app-question-bank-panel', standalone: true, changeDetection: ChangeDetectionStrategy.Eager,
    template: '<div class="panel-stub"></div>' })
class QuestionBankPanelStub {
    readonly filterLayout = input<string>();
    readonly initialScope = input<string>();
    readonly initialSource = input<string>();
    readonly initialStatus = input<string>();
}

describe('QuestionBankComponent (issue #471)', () => {
    let fixture: ComponentFixture<QuestionBankComponent>;

    function setup(query: Record<string, string> = {}): void {
        TestBed.overrideComponent(QuestionBankComponent, {
            remove: { imports: [QuestionBankPanelComponent] },
            add: { imports: [QuestionBankPanelStub] },
        });
        TestBed.configureTestingModule({
            imports: [QuestionBankComponent, translocoTestingModule({ langs: { 'question-bank/tr': qbTr } })],
            providers: [
                provideNoopAnimations(),
                provideRouter([]),
                { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } },
            ],
        });
        fixture = TestBed.createComponent(QuestionBankComponent);
        fixture.detectChanges();
    }

    beforeEach(() => setup());

    it('template_Rendered_ShowsTranslatedTitle', () => {
        const heading = fixture.nativeElement.querySelector('h1') as HTMLElement;
        expect(heading.textContent?.trim()).toBe(qbTr.title);
    });

    it('template_Rendered_LinksToQuickAdd', () => {
        const link = fixture.nativeElement.querySelector('.question-bank-page__quick-add') as HTMLAnchorElement;
        expect(link.getAttribute('href')).toBe('/quick-add');
        expect(link.textContent).toContain(qbTr.quickAdd);
    });

    it('template_Rendered_EmbedsSearchPanel', () => {
        expect(fixture.nativeElement.querySelector('app-question-bank-panel')).not.toBeNull();
    });

    // Issue #1315: admin panosundaki sınıflandırma grupları bankayı süzülmüş açar.
    it('queryParams_KnownValues_PassedToPanelAsInitialFilters', () => {
        TestBed.resetTestingModule();
        setup({ scope: 'all', source: 'Book', status: 'unclassified' });
        const panel = fixture.debugElement.children[0].query((d) => d.name === 'app-question-bank-panel').componentInstance as QuestionBankPanelStub;
        expect(panel.initialScope()).toBe('all');
        expect(panel.initialSource()).toBe('Book');
        expect(panel.initialStatus()).toBe('unclassified');
    });

    it('queryParams_UnknownValues_FallBackToDefaults', () => {
        TestBed.resetTestingModule();
        setup({ scope: 'everything', source: 'x' });
        const panel = fixture.debugElement.children[0].query((d) => d.name === 'app-question-bank-panel').componentInstance as QuestionBankPanelStub;
        expect(panel.initialScope()).toBe('mine');
        expect(panel.initialSource()).toBe('any');
        expect(panel.initialStatus()).toBe('any');
    });
});
