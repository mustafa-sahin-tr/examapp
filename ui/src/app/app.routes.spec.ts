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
        'question/:id',
        'questioncanvas',
        'questioncanvas/:id',
    ];

    for (const path of authoringPaths) {
        it(`${path}_HasRoleAndApprovalGuards`, () => {
            const route = flatten(routes).find((r) => r.path === path);

            expect(route?.canActivate).toContain(authGuard);
            expect(route?.canActivate).toContain(approvedTeacherGuard);
            // #1087: soru tuvali ayrıca planda `manual-labeling` ister.
            expect(route?.canActivate).toHaveLength(path.startsWith('questioncanvas') ? 4 : 3);
        });
    }
});
