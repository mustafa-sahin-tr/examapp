import { TestBed } from '@angular/core/testing';
import { BehaviorSubject } from 'rxjs';

import { AuthService } from '../../services/auth.service';
import { rootRedirect } from './root-redirect';

describe('rootRedirect (#875)', () => {
  function run(signedIn: boolean): string {
    TestBed.configureTestingModule({
      providers: [{ provide: AuthService, useValue: { isAuthenticatedSubject: new BehaviorSubject(signedIn) } }],
    });
    return TestBed.runInInjectionContext(() => rootRedirect());
  }

  it('sends a signed-in user to the dashboard', () => {
    expect(run(true)).toBe('/dashboard');
  });

  it('sends a visitor to the welcome page', () => {
    expect(run(false)).toBe('/welcome');
  });
});
