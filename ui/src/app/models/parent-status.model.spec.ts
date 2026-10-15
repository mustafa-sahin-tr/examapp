import { HttpErrorResponse } from '@angular/common/http';

import {
  isParentRestrictionError,
  isSubjectToParentRule,
  parseServerDate,
  safeDevActivationUrl,
} from './parent-status.model';

/** Issue #440 / #441: model yardımcıları. */
describe('parent-status.model', () => {
  it('isSubjectToParentRule_StudentWithoutAdultRole', () => {
    expect(isSubjectToParentRule(['Student'])).toBeTrue();
    expect(isSubjectToParentRule(['Student', 'Teacher'])).toBeFalse();
    expect(isSubjectToParentRule(['Student', 'Parent'])).toBeFalse();
    expect(isSubjectToParentRule(['Teacher'])).toBeFalse();
    expect(isSubjectToParentRule([])).toBeFalse();
  });

  it('isParentRestrictionError_Requires403AndCode', () => {
    const err = (status: number, errorCode: string) => new HttpErrorResponse({ status, error: { errorCode } });
    expect(isParentRestrictionError(err(403, 'ParentRequired'), 'ParentRequired')).toBeTrue();
    expect(isParentRestrictionError(err(400, 'ParentRequired'), 'ParentRequired')).toBeFalse();
    expect(isParentRestrictionError(err(403, 'TeacherNotApproved'), 'ParentRequired')).toBeFalse();
    expect(isParentRestrictionError(new Error('x'), 'ParentRequired')).toBeFalse();
  });

  it('parseServerDate_ZonelessIsUtc', () => {
    expect(parseServerDate('2026-10-15T10:00:00')?.toISOString()).toBe('2026-10-15T10:00:00.000Z');
    expect(parseServerDate('2026-10-15T10:00:00Z')?.toISOString()).toBe('2026-10-15T10:00:00.000Z');
    expect(parseServerDate('nope')).toBeNull();
    expect(parseServerDate(null)).toBeNull();
  });

  it('safeDevActivationUrl_AllowsRelativeAndHttpOnly', () => {
    expect(safeDevActivationUrl('/app/parent-activate?token=a')).toBe('/app/parent-activate?token=a');
    expect(safeDevActivationUrl('http://localhost:5678/app/parent-activate?token=a')).toBe(
      'http://localhost:5678/app/parent-activate?token=a'
    );
    expect(safeDevActivationUrl('javascript:alert(1)')).toBeNull();
    expect(safeDevActivationUrl('//evil.example/x')).toBeNull();
    expect(safeDevActivationUrl('')).toBeNull();
    expect(safeDevActivationUrl(undefined)).toBeNull();
  });
});
