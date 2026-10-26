import { HttpErrorResponse } from '@angular/common/http';

import { activityErrorCode, parseActivityFeedback, saveValidationMessage } from './activity';

describe('activity model helpers (#573)', () => {
  it('parseActivityFeedback_ValidRowsKept_InvalidIgnored', () => {
    expect(parseActivityFeedback('{"rows":[{"id":"z1","correct":true},{"id":2,"correct":true}]}')).toEqual([
      { id: 'z1', correct: true },
    ]);
    expect(parseActivityFeedback('{bad')).toEqual([]);
    expect(parseActivityFeedback(null)).toEqual([]);
  });

  it('activityErrorCode_ReadsBodyOfHttpErrorOrPlainBody', () => {
    expect(activityErrorCode(new HttpErrorResponse({ status: 409, error: { errorCode: 'ActivityCheckRequired' } }))).toBe(
      'ActivityCheckRequired'
    );
    expect(activityErrorCode({ errorCode: 'RateLimited' })).toBe('RateLimited');
    expect(activityErrorCode(null)).toBeNull();
  });

  it('saveValidationMessage_ServiceMessage_ModelErrors_OrNull', () => {
    expect(saveValidationMessage(new HttpErrorResponse({ status: 400, error: { message: 'Yönerge geçersiz.' } }))).toBe(
      'Yönerge geçersiz.'
    );
    expect(
      saveValidationMessage(
        new HttpErrorResponse({ status: 400, error: { errors: { '$.questions[0].x': ['bad path'], 'Passages[0].Instruction': ['Kontrol karakteri.'] } } })
      )
    ).toBe('Kontrol karakteri.');
    expect(saveValidationMessage(new HttpErrorResponse({ status: 500, error: { message: 'x' } }))).toBeNull();
    expect(saveValidationMessage(new HttpErrorResponse({ status: 400, error: {} }))).toBeNull();
  });
});
