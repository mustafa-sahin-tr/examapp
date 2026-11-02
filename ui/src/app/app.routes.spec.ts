import { Route } from '@angular/router';
import { routes } from './app.routes';
import { authGuard } from './shared/guards/auth.guard';
import { approvedTeacherGuard } from './shared/guards/approved-teacher.guard';

function flatten(list: Route[]): Route[] {
    return list.flatMap((route) => [route, ...flatten(route.children ?? [])]);
}

/** Issue #709: öğrenci adresi yazarak soru yazma ekranlarını açamaz. */
describe('authoring routes (issue #709)', () => {
    const authoringPaths = [
        'question',
        'question/:id',
        'questioncanvas',
        'questioncanvas/:id',
        'questioncanvas/preview',
        'questioncanvas/preview/:testId',
        'imageselect',
        'questions/view',
    ];

    for (const path of authoringPaths) {
        it(`${path}_HasRoleAndApprovalGuards`, () => {
            const route = flatten(routes).find((r) => r.path === path);

            expect(route?.canActivate).toContain(authGuard);
            expect(route?.canActivate).toContain(approvedTeacherGuard);
            expect(route?.canActivate).toHaveLength(3);
        });
    }
});
