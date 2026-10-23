import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, Subject } from 'rxjs';

import { EnhancedLayoutComponent } from './enhanced-layout.component';
import { AuthService } from '../../services/auth.service';
import { SignalRService } from '../../services/signalr.service';
import { WorksheetAccessRequestService } from '../../services/worksheet-access-request.service';
import { StudentSchoolRequestService } from '../../services/student-school-request.service';
import { NotificationService } from '../../services/notification.service';
import { DirectMessageService } from '../../services/direct-message.service';
import { UserThemeService } from '../../services/user-theme.service';
import { ThemeConfigService } from '../../services/theme-config.service';
import { routes } from '../../app.routes';
import { approvedTeacherGuard } from '../../shared/guards/approved-teacher.guard';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';

/**
 * issue #471: "Soru Bankası" menü girişi ve rotası. Kurulum enhanced-layout.component.spec.ts ile aynıdır;
 * ayrı dosyada tutuldu ki mevcut menü sayıları (ve CRLF'li spec) değişmesin.
 */
describe('EnhancedLayoutComponent question bank menu (issue #471)', () => {
    function create(roles: string[]): EnhancedLayoutComponent {
        const authStub: Partial<AuthService> = {
            hasRealmRole: (role: string) => roles.includes(role),
            isAuthenticated: () => of(true),
            isUnapprovedTeacher: signal(false),
            user: signal(null),
        };
        TestBed.configureTestingModule({
            imports: [EnhancedLayoutComponent, translocoTestingModule()],
            providers: [
                provideRouter([]),
                { provide: AuthService, useValue: authStub },
                { provide: SignalRService, useValue: { accessRequestUpdates$: new Subject() } },
                { provide: WorksheetAccessRequestService, useValue: { pendingCount: signal(0) } },
                { provide: StudentSchoolRequestService, useValue: { pendingCount: signal(0), refreshPendingCount: () => of(0) } },
                {
                    provide: DirectMessageService,
                    useValue: {
                        unreadCount: signal(0).asReadonly(),
                        refreshUnreadCount: () => of(0),
                        resetUnreadCount: () => undefined,
                    },
                },
                { provide: NotificationService, useValue: { unreadCount: signal(0) } },
                { provide: UserThemeService, useValue: {} },
                { provide: ThemeConfigService, useValue: {} },
            ],
        });
        return TestBed.createComponent(EnhancedLayoutComponent).componentInstance;
    }

    const questionBankItem = (component: EnhancedLayoutComponent) => component.visibleMenuItems().find((i) => i.id === 'question-bank');

    it('visibleMenuItems_Teacher_ContainsQuestionBankEntry', () => {
        const item = questionBankItem(create(['Teacher']));

        expect(item).toBeDefined();
        expect(item?.type).toBe('menu');
        expect(item?.route).toBe('/question-bank');
        expect(item?.labelKey).toBe('menu.questionBank');
        expect(item?.icon).toBe('inventory_2');
        expect(item?.roles).toEqual(['Teacher', 'Admin']);
    });

    it('visibleMenuItems_Admin_ContainsQuestionBankEntry', () => {
        expect(questionBankItem(create(['Admin']))).toBeDefined();
    });

    it('visibleMenuItems_Student_DoesNotContainQuestionBankEntry', () => {
        expect(questionBankItem(create(['Student']))).toBeUndefined();
    });

    it('visibleMenuItems_Parent_DoesNotContainQuestionBankEntry', () => {
        expect(questionBankItem(create(['Parent']))).toBeUndefined();
    });

    it('visibleMenuItems_QuestionBankEntry_AppearsOnlyOnce', () => {
        const component = create(['Teacher']);
        const matches = component.visibleMenuItems().filter((i) => i.route === '/question-bank');
        expect(matches.length).toBe(1);
    });

    it('routes_QuestionBank_UsesRoleAndApprovedTeacherGuards', () => {
        const children = routes.find((r) => Array.isArray(r.children))?.children ?? [];
        const route = children.find((r) => r.path === 'question-bank');

        expect(route).toBeDefined();
        expect(route?.canActivate?.length).toBe(3);
        expect(route?.canActivate).toContain(approvedTeacherGuard);
        // Üçüncü guard roleGuard('Teacher', 'Admin') — closure olduğu için yalnız varlığı kontrol edilir.
        expect(route?.canActivate?.some((g) => g !== approvedTeacherGuard)).toBe(true);
    });

    it('routes_QuestionBank_LazyLoadsPage', () => {
        const children = routes.find((r) => Array.isArray(r.children))?.children ?? [];
        const route = children.find((r) => r.path === 'question-bank');
        expect(route?.loadComponent).toBeDefined();
    });
});
