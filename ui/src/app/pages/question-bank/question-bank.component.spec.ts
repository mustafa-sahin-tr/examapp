import { Component, ChangeDetectionStrategy, input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';

import { QuestionBankComponent } from './question-bank.component';
import { QuestionBankPanelComponent } from '../../shared/components/question-bank-panel/question-bank-panel.component';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import { AuthService } from '../../services/auth.service';
import qbTr from '../../../../public/i18n/question-bank/tr.json';

/** Panelin kendi HTTP/dialog bağımlılıkları yok sayılır; sayfa yalnız panel'i yerleştirir. */
@Component({ selector: 'app-question-bank-panel', standalone: true, changeDetection: ChangeDetectionStrategy.Eager,
    template: '<div class="panel-stub"></div>' })
class QuestionBankPanelStub {
    readonly canApproveSolutions = input(false);
    readonly canApproveOthersSolutions = input(false);
}

describe('QuestionBankComponent (issue #471)', () => {
    let fixture: ComponentFixture<QuestionBankComponent>;
    let realmRoles: string[] = [];

    function create(roles: string[]): void {
        realmRoles = roles;
        fixture = TestBed.createComponent(QuestionBankComponent);
        fixture.detectChanges();
    }

    function panel(): QuestionBankPanelStub {
        return fixture.debugElement.query((de) => de.componentInstance instanceof QuestionBankPanelStub)
            .componentInstance as QuestionBankPanelStub;
    }

    beforeEach(() => {
        TestBed.overrideComponent(QuestionBankComponent, {
            remove: { imports: [QuestionBankPanelComponent] },
            add: { imports: [QuestionBankPanelStub] },
        });
        TestBed.configureTestingModule({
            imports: [QuestionBankComponent, translocoTestingModule({ langs: { 'question-bank/tr': qbTr } })],
            providers: [
                provideNoopAnimations(),
                { provide: AuthService, useValue: { hasRealmRole: (role: string) => realmRoles.includes(role) } },
            ],
        });
        create([]);
    });

    it('template_Rendered_ShowsTranslatedTitle', () => {
        const heading = fixture.nativeElement.querySelector('h1') as HTMLElement;
        expect(heading.textContent?.trim()).toBe(qbTr.title);
    });

    it('template_Rendered_EmbedsSearchPanel', () => {
        expect(fixture.nativeElement.querySelector('app-question-bank-panel')).not.toBeNull();
    });

    // Issue #580: çözüm onayı yalnız Admin + QuestionTrainer realm rolüyle açılır.
    it('canApproveSolutions_PassedFromRealmRoles', () => {
        expect(panel().canApproveSolutions()).toBe(false);
        fixture.destroy();

        create(['Teacher', 'QuestionTrainer']);
        expect(panel().canApproveSolutions()).toBe(true);
        expect(panel().canApproveOthersSolutions()).toBe(false);
        fixture.destroy();

        create(['Admin']);
        expect(panel().canApproveSolutions()).toBe(true);
        expect(panel().canApproveOthersSolutions()).toBe(true);
    });
});
