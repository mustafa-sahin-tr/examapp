import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MockedObject } from 'vitest';
import { of } from 'rxjs';
import { AssistantPanelService } from '../../../services/assistant-panel.service';
import { AssistantService } from '../../../services/assistant.service';
import { AuthService } from '../../../services/auth.service';
import { translocoTestingModule } from '../../testing/transloco-testing';
import assistantTr from '../../../../../public/i18n/assistant/tr.json';
import { AssistantLauncherComponent } from './assistant-launcher.component';

describe('AssistantLauncherComponent (#507)', () => {
    let fixture: ComponentFixture<AssistantLauncherComponent>;
    let api: MockedObject<AssistantService>;
    let panel: AssistantPanelService;

    const fab = () => document.querySelector<HTMLButtonElement>('[data-testid="assistant-fab"]');
    const pane = () => document.querySelector('.assistant-overlay-pane app-assistant-panel');

    function setup(options: { enabled?: boolean; allowed?: boolean; student?: boolean } = {}): void {
        const { enabled = true, allowed = true, student = true } = options;
        api = {
            status: vi.fn().mockReturnValue(of({ enabled, code: enabled ? null : 'AssistantRoleNotEnabled', roles: [], limits: null })),
            studentAccess: vi.fn().mockReturnValue(of({ allowed })),
            usage: vi.fn().mockReturnValue(of({ messagesThisHour: 0, messagesPerHour: 30, spentTodayUsd: 0, dailyBudgetUsd: 0.1, totalBudgetReached: false })),
        } as unknown as MockedObject<AssistantService>;
        TestBed.configureTestingModule({
            imports: [AssistantLauncherComponent, translocoTestingModule({ langs: { 'assistant/tr': assistantTr } })],
            providers: [
                provideRouter([]),
                { provide: AssistantService, useValue: api },
                {
                    provide: AuthService,
                    useValue: { hasToken: () => true, hasRealmRole: (r: string) => student && r === 'Student' },
                },
            ],
        });
        panel = TestBed.inject(AssistantPanelService);
        panel.load();
        fixture = TestBed.createComponent(AssistantLauncherComponent);
        fixture.detectChanges();
    }

    afterEach(() => fixture?.destroy());

    it('enabledStudent_ShowsFab_ClickOpensPanelInOverlay', () => {
        setup();
        expect(fab()).toBeTruthy();

        fab()!.click();
        fixture.detectChanges();

        expect(panel.isOpen()).toBe(true);
        expect(pane()).toBeTruthy();
        expect(fab()).toBeNull();
        expect(api.studentAccess).toHaveBeenCalledTimes(2);
    });

    it('roleNotEnabled_NoFab_ShortcutDoesNothing', () => {
        setup({ enabled: false });
        expect(fab()).toBeNull();
        expect(api.studentAccess).not.toHaveBeenCalled();

        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true }));
        fixture.detectChanges();

        expect(panel.isOpen()).toBe(false);
    });

    it('parentSwitchedOff_NoFab', () => {
        setup({ allowed: false });
        expect(fab()).toBeNull();
        expect(panel.available()).toBe(false);
    });

    it('ctrlK_And_MetaK_Toggle_EscapeCloses', () => {
        setup();
        const event = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, cancelable: true });
        document.dispatchEvent(event);
        fixture.detectChanges();
        expect(panel.isOpen()).toBe(true);
        expect(event.defaultPrevented).toBe(true);

        document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        fixture.detectChanges();
        expect(panel.isOpen()).toBe(false);
        expect(pane()).toBeNull();

        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'K', metaKey: true }));
        fixture.detectChanges();
        expect(panel.isOpen()).toBe(true);
    });

    it('suppressed_ExamRoute_HidesFabAndClosesOpenPanel', () => {
        setup();
        panel.open();
        fixture.detectChanges();
        expect(pane()).toBeTruthy();

        panel.suppressed.set(true);
        fixture.detectChanges();

        expect(panel.isOpen()).toBe(false);
        expect(pane()).toBeNull();
        expect(fab()).toBeNull();

        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true }));
        fixture.detectChanges();
        expect(panel.isOpen()).toBe(false);
    });

    it('panelService_NotStudent_SkipsParentCheck', () => {
        setup({ student: false });
        expect(api.studentAccess).not.toHaveBeenCalled();
        expect(fab()).toBeTruthy();
        expect(panel.visible()).toBe(true);
    });
});
