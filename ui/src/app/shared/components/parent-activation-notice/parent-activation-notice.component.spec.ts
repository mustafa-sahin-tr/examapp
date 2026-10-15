import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';

import { ParentActivationNoticeComponent, ParentRestrictedFeature } from './parent-activation-notice.component';
import { ParentStatusStore } from '../../../services/parent-status.store';
import { awaitingStatusOf, parentStatusOf } from '../../testing/parent-status-testing';
import { translocoTestingModule } from '../../testing/transloco-testing';

/** Issue #440: kısıtlı mod bilgi bandı. */
describe('ParentActivationNoticeComponent', () => {
  function create(feature: ParentRestrictedFeature) {
    TestBed.configureTestingModule({ imports: [ParentActivationNoticeComponent, translocoTestingModule()] });
    const fixture = TestBed.createComponent(ParentActivationNoticeComponent);
    fixture.componentRef.setInput('feature', feature);
    return fixture;
  }

  it('awaitingActivation_RendersFeatureMessage', () => {
    const fixture = create('booking');
    TestBed.inject(ParentStatusStore).set(awaitingStatusOf());
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('özel ders talebi gönderemezsin');
  });

  it('otherModesOrNoStatus_RendersNothing', () => {
    const fixture = create('messages');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent.trim()).toBe('');

    TestBed.inject(ParentStatusStore).set(parentStatusOf({ mode: 'Grace', daysLeft: 3 }));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent.trim()).toBe('');
  });

  it('allFeatureKeysExistInBothLanguages', () => {
    create('comments');
    const transloco = TestBed.inject(TranslocoService);
    for (const feature of ['booking', 'messages', 'comments', 'leaderboard']) {
      for (const lang of ['tr', 'en']) {
        const key = `parentRequirement.restricted.${feature}`;
        expect(transloco.translate(key, {}, lang)).not.toBe(key);
      }
    }
  });
});
